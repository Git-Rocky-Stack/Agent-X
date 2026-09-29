using System.Management;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Reads the dedicated video memory of the display adapters.
/// <para>
/// WMI <c>Win32_VideoController.AdapterRAM</c> is a uint32, so it saturates at 4 GB and reports
/// every modern GPU as a 4 GB card (or less). The display driver also publishes the real size
/// as a 64-bit <c>HardwareInformation.qwMemorySize</c> value under the display adapter class key
/// in the registry, which is what this helper prefers; AdapterRAM remains the fallback for
/// drivers that do not publish it.
/// </para>
/// </summary>
public static class GpuMemoryReader
{
    /// <summary>Registry key of the display adapter device class.</summary>
    internal const string DisplayAdapterClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private const long FourGigabytes = 4_294_967_296L;

    /// <summary>A display adapter as published by its driver in the registry.</summary>
    /// <param name="DriverDesc">Adapter name (matches Win32_VideoController.Name).</param>
    /// <param name="MatchingDeviceId">PnP hardware id prefix (e.g. <c>pci\ven_10de&amp;dev_2684</c>).</param>
    /// <param name="MemorySizeBytes">Dedicated video memory in bytes, or 0 when not published.</param>
    public sealed record RegistryAdapter(string DriverDesc, string? MatchingDeviceId, long MemorySizeBytes);

    /// <summary>An adapter reported by WMI together with its resolved video memory.</summary>
    public sealed record VideoAdapter(string Name, long VramBytes);

    /// <summary>
    /// Enumerates the video controllers with their dedicated memory. Returns an empty list on
    /// non-Windows hosts; WMI failures propagate so callers can report detection as failed.
    /// </summary>
    public static IReadOnlyList<VideoAdapter> ReadAdapters(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!OperatingSystem.IsWindows())
            return Array.Empty<VideoAdapter>();

        var registryAdapters = ReadRegistryAdapters(logger);
        var adapters = new List<VideoAdapter>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
        using var results = searcher.Get();

        foreach (ManagementObject gpu in results)
        {
            try
            {
                var name = gpu["Name"]?.ToString() ?? "Unknown GPU";
                var pnpDeviceId = gpu["PNPDeviceID"]?.ToString();
                var adapterRamBytes = ParseAdapterRam(gpu["AdapterRAM"]);
                var vram = SelectVramBytes(name, pnpDeviceId, adapterRamBytes, registryAdapters);
                adapters.Add(new VideoAdapter(name, vram));
            }
            finally
            {
                gpu.Dispose();
            }
        }

        return adapters;
    }

    /// <summary>
    /// Converts the WMI AdapterRAM value (a uint32 that some providers box as a signed int) to
    /// an unsigned byte count.
    /// </summary>
    public static long ParseAdapterRam(object? adapterRam)
    {
        if (adapterRam is null)
            return 0;

        try
        {
            var value = Convert.ToInt64(adapterRam, System.Globalization.CultureInfo.InvariantCulture);
            return value < 0 ? value + FourGigabytes : value;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Converts a registry memory-size value to bytes. <c>qwMemorySize</c> is a REG_QWORD; older
    /// drivers publish <c>MemorySize</c> as a REG_DWORD or a 4/8-byte REG_BINARY.
    /// </summary>
    public static long ParseRegistryMemorySize(object? value) => value switch
    {
        long qword => qword > 0 ? qword : 0,
        int dword => (long)unchecked((uint)dword),
        byte[] { Length: >= 8 } bytes => Math.Max(0, BitConverter.ToInt64(bytes, 0)),
        byte[] { Length: >= 4 } bytes => BitConverter.ToUInt32(bytes, 0),
        _ => 0
    };

    /// <summary>
    /// Picks the dedicated memory for a WMI adapter: the driver-published registry size when a
    /// registry entry matches the adapter (by PnP hardware id, then by name), otherwise the
    /// AdapterRAM value.
    /// </summary>
    public static long SelectVramBytes(
        string adapterName,
        string? pnpDeviceId,
        long adapterRamBytes,
        IReadOnlyList<RegistryAdapter> registryAdapters)
    {
        ArgumentNullException.ThrowIfNull(registryAdapters);

        RegistryAdapter? match = null;

        if (!string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            match = registryAdapters.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.MatchingDeviceId) &&
                pnpDeviceId.StartsWith(r.MatchingDeviceId, StringComparison.OrdinalIgnoreCase) &&
                r.MemorySizeBytes > 0);
        }

        match ??= registryAdapters.FirstOrDefault(r =>
            string.Equals(r.DriverDesc, adapterName, StringComparison.OrdinalIgnoreCase) &&
            r.MemorySizeBytes > 0);

        return match is not null ? match.MemorySizeBytes : Math.Max(0, adapterRamBytes);
    }

    /// <summary>
    /// Reads the display adapters published under the display class key. Windows only; returns
    /// an empty list elsewhere or when the key cannot be read.
    /// </summary>
    internal static IReadOnlyList<RegistryAdapter> ReadRegistryAdapters(ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<RegistryAdapter>();

        var adapters = new List<RegistryAdapter>();
        try
        {
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(DisplayAdapterClassKey);
            if (classKey is null)
                return adapters;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                // Adapter instances are the numbered subkeys (0000, 0001, ...); "Properties"
                // and friends are access-restricted and irrelevant here.
                if (subKeyName.Length != 4 || !subKeyName.All(char.IsAsciiDigit))
                    continue;

                try
                {
                    using var adapterKey = classKey.OpenSubKey(subKeyName);
                    if (adapterKey is null)
                        continue;

                    var driverDesc = adapterKey.GetValue("DriverDesc") as string ?? string.Empty;
                    var matchingDeviceId = adapterKey.GetValue("MatchingDeviceId") as string;
                    var memory = ParseRegistryMemorySize(adapterKey.GetValue("HardwareInformation.qwMemorySize"));
                    if (memory <= 0)
                        memory = ParseRegistryMemorySize(adapterKey.GetValue("HardwareInformation.MemorySize"));

                    adapters.Add(new RegistryAdapter(driverDesc, matchingDeviceId, memory));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    logger.Debug(ex, "Could not read display adapter registry key {SubKey}", subKeyName);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.Debug(ex, "Could not read the display adapter class registry key");
        }

        return adapters;
    }
}
