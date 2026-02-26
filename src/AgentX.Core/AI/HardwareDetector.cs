using System.Management;
using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Detects system hardware capabilities relevant to local AI inference
/// using Windows Management Instrumentation (WMI) queries. Reports GPU,
/// CPU, RAM, and NPU information to guide model selection.
/// </summary>
public sealed class HardwareDetector : IHardwareDetector
{
    private readonly ILogger _logger;

    public HardwareDetector()
    {
        _logger = Log.ForContext<HardwareDetector>();
    }

    /// <inheritdoc />
    public async Task<HardwareCapability> DetectAsync(CancellationToken ct = default)
    {
        _logger.Information("Starting hardware detection...");

        var capability = new HardwareCapability();

        // Run WMI queries on a background thread to avoid blocking the UI thread,
        // since WMI calls can be slow and are synchronous by nature.
        await Task.Run(() =>
        {
            DetectGpu(capability);
            DetectCpu(capability);
            DetectMemory(capability);
            DetectNpu(capability);
        }, ct).ConfigureAwait(false);

        _logger.Information(
            "Hardware detection complete: GPU={GpuName} ({GpuVram}), CPU={CpuName} ({CpuCores} cores), " +
            "RAM={TotalRam}, NPU={HasNpu}",
            capability.GpuName, capability.GpuVramFormatted,
            capability.CpuName, capability.CpuCores,
            capability.TotalRamFormatted, capability.HasNpu);

        return capability;
    }

    // ── GPU Detection via Win32_VideoController ─────────────────────

    private void DetectGpu(HardwareCapability capability)
    {
        try
        {
            _logger.Debug("Querying GPU information via WMI...");

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");

            using var results = searcher.Get();

            string bestGpuName = "Unknown";
            long bestVram = 0;

            foreach (ManagementObject gpu in results)
            {
                try
                {
                    var name = gpu["Name"]?.ToString() ?? "Unknown GPU";
                    var adapterRam = gpu["AdapterRAM"];

                    // AdapterRAM is a uint32 in WMI, which caps at ~4GB.
                    // For GPUs with more VRAM, we detect from the name as a heuristic.
                    long vramBytes = 0;
                    if (adapterRam is not null)
                    {
                        vramBytes = Convert.ToInt64(adapterRam);

                        // Handle uint32 overflow: if the value is suspiciously low
                        // for a known high-VRAM GPU, apply a correction heuristic.
                        if (vramBytes < 0)
                            vramBytes += 4_294_967_296L; // Convert from signed to unsigned interpretation
                    }

                    _logger.Debug("GPU found: {Name}, VRAM: {Vram} bytes", name, vramBytes);

                    // Pick the GPU with the most VRAM (skip integrated GPUs if a dedicated one exists)
                    if (vramBytes > bestVram || (bestVram == 0 && !IsIntegratedGpu(name)))
                    {
                        bestGpuName = name;
                        bestVram = vramBytes;
                    }
                }
                finally
                {
                    gpu.Dispose();
                }
            }

            capability.GpuName = bestGpuName;
            capability.GpuVramBytes = bestVram;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "GPU detection failed via WMI");
            capability.GpuName = "Detection failed";
            capability.GpuVramBytes = 0;
        }
    }

    // ── CPU Detection via Win32_Processor ───────────────────────────

    private void DetectCpu(HardwareCapability capability)
    {
        try
        {
            _logger.Debug("Querying CPU information via WMI...");

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores FROM Win32_Processor");

            using var results = searcher.Get();

            foreach (ManagementObject cpu in results)
            {
                try
                {
                    capability.CpuName = cpu["Name"]?.ToString()?.Trim() ?? "Unknown CPU";

                    var cores = cpu["NumberOfCores"];
                    if (cores is not null)
                    {
                        capability.CpuCores = Convert.ToInt32(cores);
                    }

                    _logger.Debug("CPU found: {Name}, Cores: {Cores}", capability.CpuName, capability.CpuCores);

                    // Take the first processor (multi-socket systems are rare for desktop AI)
                    break;
                }
                finally
                {
                    cpu.Dispose();
                }
            }

            // Fallback: use Environment.ProcessorCount if WMI returned 0 cores
            if (capability.CpuCores == 0)
            {
                capability.CpuCores = Environment.ProcessorCount;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "CPU detection failed via WMI");
            capability.CpuName = $"{Environment.ProcessorCount}-core CPU";
            capability.CpuCores = Environment.ProcessorCount;
        }
    }

    // ── Memory Detection ────────────────────────────────────────────

    private void DetectMemory(HardwareCapability capability)
    {
        try
        {
            _logger.Debug("Querying memory information...");

            // Use GC.GetGCMemoryInfo for total physical memory (accurate and no WMI needed)
            var gcMemInfo = GC.GetGCMemoryInfo();
            capability.TotalRamBytes = (long)gcMemInfo.TotalAvailableMemoryBytes;

            // Use Win32_OperatingSystem for FreePhysicalMemory (more accurate than GC approximation)
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");

                using var results = searcher.Get();

                foreach (ManagementObject os in results)
                {
                    try
                    {
                        var freeKb = os["FreePhysicalMemory"];
                        if (freeKb is not null)
                        {
                            capability.AvailableRamBytes = Convert.ToInt64(freeKb) * 1024; // KB to bytes
                        }
                    }
                    finally
                    {
                        os.Dispose();
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "WMI free memory query failed, using GC approximation");
                // Approximate available memory if WMI fails
                capability.AvailableRamBytes = capability.TotalRamBytes -
                    (long)GC.GetTotalMemory(forceFullCollection: false);
            }

            _logger.Debug("Memory: Total={Total}, Available={Available}",
                capability.TotalRamFormatted, capability.AvailableRamFormatted);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Memory detection failed");
            capability.TotalRamBytes = 0;
            capability.AvailableRamBytes = 0;
        }
    }

    // ── NPU Detection ───────────────────────────────────────────────

    private void DetectNpu(HardwareCapability capability)
    {
        try
        {
            _logger.Debug("Checking for NPU devices...");

            // Query PnP devices for known NPU identifiers.
            // Intel NPUs appear as "Intel(R) AI Boost" or "Intel(R) Neural Processing Unit"
            // Qualcomm NPUs appear as "Qualcomm NPU" or "Hexagon NPU"
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE " +
                "(Name LIKE '%Neural%' OR Name LIKE '%NPU%' OR Name LIKE '%AI Boost%' OR Name LIKE '%AI Engine%')");

            using var results = searcher.Get();

            foreach (ManagementObject device in results)
            {
                try
                {
                    var name = device["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        capability.HasNpu = true;
                        capability.NpuName = name;
                        _logger.Information("NPU detected: {NpuName}", name);
                        return;
                    }
                }
                finally
                {
                    device.Dispose();
                }
            }

            capability.HasNpu = false;
            capability.NpuName = "None";
            _logger.Debug("No NPU detected");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "NPU detection failed");
            capability.HasNpu = false;
            capability.NpuName = "None";
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Heuristic to detect integrated GPUs by name pattern.
    /// </summary>
    private static bool IsIntegratedGpu(string gpuName)
    {
        if (string.IsNullOrEmpty(gpuName))
            return false;

        var upper = gpuName.ToUpperInvariant();
        return upper.Contains("INTEL") && (upper.Contains("UHD") || upper.Contains("HD GRAPHICS") || upper.Contains("IRIS"))
            || upper.Contains("MICROSOFT BASIC")
            || upper.Contains("VIRTUAL");
    }
}
