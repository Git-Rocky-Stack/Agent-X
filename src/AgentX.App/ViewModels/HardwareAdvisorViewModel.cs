using System.Collections.ObjectModel;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// The Hardware Advisor page. Every text it builds (tier names, fallbacks for values Windows did
/// not report, the advisory, model descriptions and errors) comes from the localized resources.
/// The hardware values start empty: the page keeps them hidden behind the scanning panel until
/// detection has filled them in.
/// </summary>
public partial class HardwareAdvisorViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────
    private readonly IHardwareDetector _hardwareDetector;
    private readonly IModelManager _modelManager;
    private readonly ILocalizationService _localization;

    // ── Page Properties ────────────────────────────────────────
    [ObservableProperty] private bool _isDetecting = true;

    // ── GPU ────────────────────────────────────────────────────
    [ObservableProperty] private string _gpuName = string.Empty;
    [ObservableProperty] private string _gpuVram = string.Empty;
    [ObservableProperty] private string _gpuTier = string.Empty;

    // ── CPU ────────────────────────────────────────────────────
    [ObservableProperty] private string _cpuName = string.Empty;
    [ObservableProperty] private int _cpuCores;
    [ObservableProperty] private string _cpuArchitecture = "x64";

    // ── Memory ─────────────────────────────────────────────────
    [ObservableProperty] private string _totalRam = string.Empty;
    [ObservableProperty] private string _availableRam = string.Empty;
    [ObservableProperty] private double _ramUsagePercent;

    // ── NPU ────────────────────────────────────────────────────
    // The name is shown only while HasNpu is true.
    [ObservableProperty] private bool _hasNpu;
    [ObservableProperty] private string _npuName = string.Empty;

    // ── Recommendations ────────────────────────────────────────
    [ObservableProperty] private string _recommendedModelSize = string.Empty;
    [ObservableProperty] private string _advisoryMessage = string.Empty;
    [ObservableProperty] private string _performanceTier = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // ── Elevation / detection completeness ─────────────────────
    // LibreHardwareMonitor / WMI sensor reads need admin privileges; unelevated
    // they silently return blanks (no VRAM, placeholder GPU name). When that
    // happens we surface an informational elevation hint rather than show empty
    // fields with no explanation.
    [ObservableProperty] private bool _isDetectionIncomplete;

    public ObservableCollection<RecommendedModel> RecommendedModels { get; } = new();

    // Filtered collections for section display
    public ObservableCollection<RecommendedModel> ChatModels { get; } = new();
    public ObservableCollection<RecommendedModel> CodeModels { get; } = new();
    public ObservableCollection<RecommendedModel> EmbeddingModels { get; } = new();

    // ── Constructor ────────────────────────────────────────────
    public HardwareAdvisorViewModel(
        IHardwareDetector hardwareDetector,
        IModelManager modelManager,
        ILocalizationService localization)
    {
        _hardwareDetector = hardwareDetector;
        _modelManager = modelManager;
        _localization = localization;
        Log.Debug("HardwareAdvisorViewModel created with services");
    }

    // ── Initialization ─────────────────────────────────────────
    public async Task InitializeAsync()
    {
        Log.Information("HardwareAdvisor initializing...");
        IsDetecting = true;
        IsDetectionIncomplete = false;
        ClearError();

        try
        {
            var capability = await _hardwareDetector.DetectAsync();

            PopulateFromCapability(capability);
            await BuildRecommendationsAsync(capability);

            Log.Information("HardwareAdvisor initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hardware detection failed");
            SetError(_localization.GetString("HwAdvisor_DetectionFailedError"));
            PopulateFallbackData();
        }
        finally
        {
            IsDetecting = false;
        }
    }

    // ── Populate from HardwareCapability ───────────────────────
    private void PopulateFromCapability(HardwareCapability capability)
    {
        // Coalesce placeholder/empty sensor values to friendly fallbacks so the
        // UI never shows a blank field when a read returns nothing.
        GpuName = Friendly(capability.GpuName, _localization.GetString("HwAdvisor_GpuNotDetected"));
        GpuVram = capability.GpuVramBytes > 0
            ? capability.GpuVramFormatted
            : _localization.GetString("HwAdvisor_NoDedicatedGpu");
        GpuTier = DetermineGpuTier(capability.GpuVramBytes);

        CpuName = Friendly(capability.CpuName, _localization.GetString("HwAdvisor_CpuNotDetected"));
        CpuCores = capability.CpuCores;
        CpuArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

        var ramNotDetected = _localization.GetString("HwAdvisor_RamNotDetected");
        TotalRam = capability.TotalRamBytes > 0 ? capability.TotalRamFormatted : ramNotDetected;
        AvailableRam = capability.TotalRamBytes > 0 ? capability.AvailableRamFormatted : ramNotDetected;
        RamUsagePercent = capability.TotalRamBytes > 0
            ? (double)(capability.TotalRamBytes - capability.AvailableRamBytes) / capability.TotalRamBytes * 100.0
            : 0;

        HasNpu = capability.HasNpu;
        NpuName = capability.HasNpu ? capability.NpuName : string.Empty;

        RecommendedModelSize = _localization.GetString(
            "HwAdvisor_UpToModelSize", capability.RecommendedMaxModelParameters);

        // Detection is incomplete when core sensor reads came back empty or as a
        // placeholder — the typical signature of running without elevation.
        IsDetectionIncomplete =
            IsPlaceholder(capability.GpuName) ||
            IsPlaceholder(capability.CpuName) ||
            capability.TotalRamBytes <= 0;
    }

    /// <summary>Returns the value when meaningful, otherwise a friendly fallback.</summary>
    private static string Friendly(string? value, string fallback)
        => IsPlaceholder(value) ? fallback : value!.Trim();

    /// <summary>
    /// True when a sensor field is empty or one of the known non-informative
    /// placeholders that detection emits when a read fails or is blocked.
    /// </summary>
    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        var v = value.Trim();
        return v.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Unknown GPU", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Unknown CPU", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Detection failed", StringComparison.OrdinalIgnoreCase)
            || v.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);
    }

    // ── Build Recommendations ──────────────────────────────────
    private async Task BuildRecommendationsAsync(HardwareCapability capability)
    {
        RecommendedModels.Clear();
        ChatModels.Clear();
        CodeModels.Clear();
        EmbeddingModels.Clear();

        // Determine effective memory (use VRAM if available, else available RAM)
        var effectiveMemoryGb = capability.GpuVramBytes > 0
            ? capability.GpuVramBytes / 1_000_000_000.0
            : capability.AvailableRamBytes / 1_000_000_000.0;

        // Get installed model list (for showing "Installed" badges)
        var installedModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var installed = await _modelManager.GetInstalledModelsAsync();
            foreach (var m in installed)
            {
                installedModelNames.Add(m.Name);
                installedModelNames.Add(m.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not retrieve installed models for recommendation badges");
        }

        var recommendations = BuildModelList(effectiveMemoryGb);

        foreach (var rec in recommendations)
        {
            rec.IsInstalled = installedModelNames.Contains(rec.Name);
            RecommendedModels.Add(rec);

            switch (rec.Category)
            {
                case "Chat":
                    ChatModels.Add(rec);
                    break;
                case "Code":
                    CodeModels.Add(rec);
                    break;
                case "Embedding":
                    EmbeddingModels.Add(rec);
                    break;
            }
        }

        // Build advisory message
        AdvisoryMessage = BuildAdvisoryMessage(capability, effectiveMemoryGb);
        PerformanceTier = DeterminePerformanceTier(effectiveMemoryGb);
    }

    // ── Model Recommendations by Memory Tier ───────────────────
    private List<RecommendedModel> BuildModelList(double effectiveMemoryGb)
    {
        var models = new List<RecommendedModel>();

        if (effectiveMemoryGb < 4)
        {
            // Ultra-light tier: < 4GB
            models.Add(new RecommendedModel
            {
                Name = "phi3:mini",
                Description = _localization.GetString("HwAdvisor_ModelPhi3Mini"),
                Size = "2.3 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5:0.5b",
                Description = _localization.GetString("HwAdvisor_ModelQwen25Tiny"),
                Size = "0.4 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:1.5b",
                Description = _localization.GetString("HwAdvisor_ModelQwen25Coder1_5b"),
                Size = "1.0 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "all-minilm:l6-v2",
                Description = _localization.GetString("HwAdvisor_ModelAllMiniLm"),
                Size = "0.1 GB",
                Category = "Embedding"
            });
        }
        else if (effectiveMemoryGb < 8)
        {
            // Light tier: 4-8GB
            models.Add(new RecommendedModel
            {
                Name = "llama3.2:3b",
                Description = _localization.GetString("HwAdvisor_ModelLlama32"),
                Size = "2.0 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "mistral:7b",
                Description = _localization.GetString("HwAdvisor_ModelMistral7b"),
                Size = "4.1 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:7b",
                Description = _localization.GetString("HwAdvisor_ModelQwen25Coder7b"),
                Size = "4.7 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "all-minilm:l6-v2",
                Description = _localization.GetString("HwAdvisor_ModelAllMiniLm"),
                Size = "0.1 GB",
                Category = "Embedding"
            });
        }
        else if (effectiveMemoryGb < 16)
        {
            // Standard tier: 8-16GB
            models.Add(new RecommendedModel
            {
                Name = "llama3.1:8b",
                Description = _localization.GetString("HwAdvisor_ModelLlama31_8b"),
                Size = "4.9 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "mistral:latest",
                Description = _localization.GetString("HwAdvisor_ModelMistralLatest"),
                Size = "4.1 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "deepseek-r1:8b",
                Description = _localization.GetString("HwAdvisor_ModelDeepSeekR1_8b"),
                Size = "4.9 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "phi3:medium",
                Description = _localization.GetString("HwAdvisor_ModelPhi3Medium"),
                Size = "7.9 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:7b",
                Description = _localization.GetString("HwAdvisor_ModelQwen25Coder7b"),
                Size = "4.7 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "deepseek-coder-v2:16b",
                Description = _localization.GetString("HwAdvisor_ModelDeepSeekCoderV2"),
                Size = "8.9 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "nomic-embed-text",
                Description = _localization.GetString("HwAdvisor_ModelNomicEmbed"),
                Size = "0.3 GB",
                Category = "Embedding"
            });
        }
        else
        {
            // Power tier: 16GB+
            models.Add(new RecommendedModel
            {
                Name = "llama3.1:70b-q4_0",
                Description = _localization.GetString("HwAdvisor_ModelLlama31_70b"),
                Size = "40 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5:32b",
                Description = _localization.GetString("HwAdvisor_ModelQwen25_32b"),
                Size = "20 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "mistral-large:latest",
                Description = _localization.GetString("HwAdvisor_ModelMistralLarge"),
                Size = "23 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "llama3.2:latest",
                Description = _localization.GetString("HwAdvisor_ModelLlama32Latest"),
                Size = "2.0 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "deepseek-coder-v2:16b",
                Description = _localization.GetString("HwAdvisor_ModelDeepSeekCoderV2"),
                Size = "8.9 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:32b",
                Description = _localization.GetString("HwAdvisor_ModelQwen25Coder32b"),
                Size = "20 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "nomic-embed-text",
                Description = _localization.GetString("HwAdvisor_ModelNomicEmbed"),
                Size = "0.3 GB",
                Category = "Embedding"
            });
            models.Add(new RecommendedModel
            {
                Name = "mxbai-embed-large",
                Description = _localization.GetString("HwAdvisor_ModelMxbaiEmbedLarge"),
                Size = "0.7 GB",
                Category = "Embedding"
            });
        }

        return models;
    }

    // ── Advisory Message Builder ───────────────────────────────
    private string BuildAdvisoryMessage(HardwareCapability capability, double effectiveMemoryGb)
    {
        var lines = new List<string>();

        if (capability.GpuVramBytes > 0)
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceGpu", capability.GpuName, capability.GpuVramFormatted));
        }
        else
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceNoGpu"));
        }

        if (effectiveMemoryGb < 4)
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceUnder4"));
        }
        else if (effectiveMemoryGb < 8)
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceUnder8"));
        }
        else if (effectiveMemoryGb < 16)
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceUnder16"));
        }
        else
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceOver16"));
        }

        if (capability.HasNpu)
        {
            lines.Add(_localization.GetString("HwAdvisor_AdviceNpu", capability.NpuName));
        }

        return string.Join(" ", lines);
    }

    // ── Tier Determination ─────────────────────────────────────
    private string DetermineGpuTier(long gpuVramBytes)
    {
        return gpuVramBytes switch
        {
            0 => _localization.GetString("HwAdvisor_NoDedicatedGpu"),
            < 4_000_000_000L => _localization.GetString("HwAdvisor_GpuTierEntry"),
            < 8_000_000_000L => _localization.GetString("HwAdvisor_GpuTierMainstream"),
            < 16_000_000_000L => _localization.GetString("HwAdvisor_GpuTierPerformance"),
            < 24_000_000_000L => _localization.GetString("HwAdvisor_GpuTierEnthusiast"),
            _ => _localization.GetString("HwAdvisor_GpuTierProfessional")
        };
    }

    private string DeterminePerformanceTier(double effectiveMemoryGb)
    {
        return effectiveMemoryGb switch
        {
            < 4 => _localization.GetString("HwAdvisor_PerfTierBasic"),
            < 8 => _localization.GetString("HwAdvisor_PerfTierStandard"),
            < 16 => _localization.GetString("HwAdvisor_PerfTierPerformance"),
            < 32 => _localization.GetString("HwAdvisor_PerfTierHighEnd"),
            _ => _localization.GetString("HwAdvisor_PerfTierProfessional")
        };
    }

    // ── Refresh Command ────────────────────────────────────────
    [RelayCommand]
    private async Task RefreshHardwareAsync()
    {
        Log.Debug("Refresh hardware detection requested");
        await InitializeAsync();
    }

    // ── Pull Recommended Model Command ─────────────────────────
    [RelayCommand]
    private async Task PullRecommendedModelAsync(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;

        Log.Information("Pulling recommended model: {ModelName}", modelName);

        try
        {
            // Pull the model (progress is tracked on the Model Manager page;
            // here we just await completion).
            await _modelManager.PullModelAsync(modelName);

            // Mark as installed
            var installedModel = RecommendedModels.FirstOrDefault(m => m.Name == modelName);
            if (installedModel is not null)
            {
                installedModel.IsInstalled = true;
                RefreshModelInCollections(installedModel);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to pull recommended model: {ModelName}", modelName);
            SetError(_localization.GetString("HwAdvisor_InstallFailed", modelName));
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private void RefreshModelInCollections(RecommendedModel model)
    {
        // Force observable collection update
        var index = RecommendedModels.IndexOf(model);
        if (index >= 0)
        {
            RecommendedModels[index] = model;
        }

        var chatIndex = ChatModels.IndexOf(model);
        if (chatIndex >= 0) ChatModels[chatIndex] = model;

        var codeIndex = CodeModels.IndexOf(model);
        if (codeIndex >= 0) CodeModels[codeIndex] = model;

        var embedIndex = EmbeddingModels.IndexOf(model);
        if (embedIndex >= 0) EmbeddingModels[embedIndex] = model;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    private void PopulateFallbackData()
    {
        var unknown = _localization.GetString("HwAdvisor_Unknown");
        GpuName = _localization.GetString("HwAdvisor_DetectionFailed");
        GpuVram = unknown;
        GpuTier = unknown;
        CpuName = _localization.GetString("HwAdvisor_CpuCoreCount", Environment.ProcessorCount);
        CpuCores = Environment.ProcessorCount;
        TotalRam = unknown;
        AvailableRam = unknown;
        RecommendedModelSize = _localization.GetString("HwAdvisor_UnableToDetermine");
        AdvisoryMessage = _localization.GetString("HwAdvisor_AdviceDetectionFailed");
        PerformanceTier = unknown;
        IsDetectionIncomplete = true;
    }

    public void Dispose()
    {
        Log.Debug("HardwareAdvisorViewModel disposed");
    }
}

// ── Recommended Model Item ─────────────────────────────────────
public partial class RecommendedModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _size = string.Empty;
    [ObservableProperty] private string _category = "Chat"; // Chat, Code, Embedding
    [ObservableProperty] private bool _isInstalled;

    /// <summary>
    /// Icon glyph based on category.
    /// </summary>
    public string CategoryIcon => Category switch
    {
        "Chat" => "\uE8BD",     // Chat bubble
        "Code" => "\uE943",     // Code
        "Embedding" => "\uF168", // Database/collection
        _ => "\uE946"           // Generic
    };
}
