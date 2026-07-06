using System.Collections.ObjectModel;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class HardwareAdvisorViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────
    private readonly IHardwareDetector _hardwareDetector;
    private readonly IModelManager _modelManager;

    // ── Page Properties ────────────────────────────────────────
    [ObservableProperty] private bool _isDetecting = true;

    // ── GPU ────────────────────────────────────────────────────
    [ObservableProperty] private string _gpuName = "Detecting...";
    [ObservableProperty] private string _gpuVram = "Detecting...";
    [ObservableProperty] private string _gpuTier = "Unknown";

    // ── CPU ────────────────────────────────────────────────────
    [ObservableProperty] private string _cpuName = "Detecting...";
    [ObservableProperty] private int _cpuCores;
    [ObservableProperty] private string _cpuArchitecture = "x64";

    // ── Memory ─────────────────────────────────────────────────
    [ObservableProperty] private string _totalRam = "Detecting...";
    [ObservableProperty] private string _availableRam = "Detecting...";
    [ObservableProperty] private double _ramUsagePercent;

    // ── NPU ────────────────────────────────────────────────────
    [ObservableProperty] private bool _hasNpu;
    [ObservableProperty] private string _npuName = "None detected";

    // ── Recommendations ────────────────────────────────────────
    [ObservableProperty] private string _recommendedModelSize = "Analyzing...";
    [ObservableProperty] private string _advisoryMessage = string.Empty;
    [ObservableProperty] private string _performanceTier = "Analyzing...";
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
    public HardwareAdvisorViewModel(IHardwareDetector hardwareDetector, IModelManager modelManager)
    {
        _hardwareDetector = hardwareDetector;
        _modelManager = modelManager;
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
            SetError("Hardware detection failed. Some information may be unavailable.");
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
        GpuName = Friendly(capability.GpuName, "GPU not detected");
        GpuVram = capability.GpuVramFormatted;
        GpuTier = DetermineGpuTier(capability.GpuVramBytes);

        CpuName = Friendly(capability.CpuName, "CPU not detected");
        CpuCores = capability.CpuCores;
        CpuArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

        TotalRam = capability.TotalRamBytes > 0 ? capability.TotalRamFormatted : "Not detected";
        AvailableRam = capability.TotalRamBytes > 0 ? capability.AvailableRamFormatted : "Not detected";
        RamUsagePercent = capability.TotalRamBytes > 0
            ? (double)(capability.TotalRamBytes - capability.AvailableRamBytes) / capability.TotalRamBytes * 100.0
            : 0;

        HasNpu = capability.HasNpu;
        NpuName = capability.HasNpu ? capability.NpuName : "None detected";

        RecommendedModelSize = capability.RecommendedMaxModelSize;

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
    private static List<RecommendedModel> BuildModelList(double effectiveMemoryGb)
    {
        var models = new List<RecommendedModel>();

        if (effectiveMemoryGb < 4)
        {
            // Ultra-light tier: < 4GB
            models.Add(new RecommendedModel
            {
                Name = "phi3:mini",
                Description = "Microsoft Phi-3 Mini (3.8B) -- Surprisingly capable for its size. Great for quick Q&A and summarization.",
                Size = "2.3 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5:0.5b",
                Description = "Alibaba Qwen 2.5 (0.5B) -- Ultra-lightweight model, fast responses with basic reasoning.",
                Size = "0.4 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:1.5b",
                Description = "Qwen 2.5 Coder (1.5B) -- Lightweight code completion and generation.",
                Size = "1.0 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "all-minilm:l6-v2",
                Description = "Sentence Transformers MiniLM -- Fast, efficient text embeddings for semantic search.",
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
                Description = "Meta Llama 3.2 (3B) -- Excellent balance of quality and speed for everyday tasks.",
                Size = "2.0 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "phi3:medium",
                Description = "Microsoft Phi-3 Medium (14B, Q4) -- Strong reasoning in a compact package.",
                Size = "4.9 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "mistral:7b",
                Description = "Mistral 7B -- Fast, versatile, and great at following instructions.",
                Size = "4.1 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:7b",
                Description = "Qwen 2.5 Coder (7B) -- Solid code generation, completion, and debugging.",
                Size = "4.7 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "all-minilm:l6-v2",
                Description = "Sentence Transformers MiniLM -- Fast, efficient text embeddings for semantic search.",
                Size = "0.1 GB",
                Category = "Embedding"
            });
        }
        else if (effectiveMemoryGb < 16)
        {
            // Standard tier: 8-16GB
            models.Add(new RecommendedModel
            {
                Name = "llama3.2:latest",
                Description = "Meta Llama 3.2 (8B) -- Top-tier open model. Excellent for chat, analysis, and writing.",
                Size = "4.7 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "mistral:latest",
                Description = "Mistral 7B v0.3 -- Fast and instruction-tuned. Great all-rounder.",
                Size = "4.1 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "deepseek-r1:8b",
                Description = "DeepSeek R1 (8B) -- Strong reasoning model with chain-of-thought capabilities.",
                Size = "4.9 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:7b",
                Description = "Qwen 2.5 Coder (7B) -- Excellent code generation, review, and debugging.",
                Size = "4.7 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "deepseek-coder-v2:16b",
                Description = "DeepSeek Coder V2 (16B) -- Advanced code understanding across 300+ languages.",
                Size = "8.9 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "nomic-embed-text",
                Description = "Nomic Embed Text -- High-quality embeddings with 8K context window.",
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
                Description = "Meta Llama 3.1 (70B, Q4) -- Flagship open model. Near-GPT-4 quality for complex tasks.",
                Size = "40 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5:32b",
                Description = "Alibaba Qwen 2.5 (32B) -- Exceptional multilingual model with strong reasoning.",
                Size = "20 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "mistral-large:latest",
                Description = "Mistral Large -- Enterprise-grade model with excellent instruction following.",
                Size = "23 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "llama3.2:latest",
                Description = "Meta Llama 3.2 (8B) -- Fast, efficient option for quick everyday tasks.",
                Size = "4.7 GB",
                Category = "Chat"
            });
            models.Add(new RecommendedModel
            {
                Name = "deepseek-coder-v2:16b",
                Description = "DeepSeek Coder V2 (16B) -- Advanced code understanding across 300+ languages.",
                Size = "8.9 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "qwen2.5-coder:32b",
                Description = "Qwen 2.5 Coder (32B) -- Top-tier code model for complex multi-file tasks.",
                Size = "20 GB",
                Category = "Code"
            });
            models.Add(new RecommendedModel
            {
                Name = "nomic-embed-text",
                Description = "Nomic Embed Text -- High-quality embeddings with 8K context window.",
                Size = "0.3 GB",
                Category = "Embedding"
            });
            models.Add(new RecommendedModel
            {
                Name = "mxbai-embed-large",
                Description = "Mixedbread Embed Large -- State-of-the-art embeddings for RAG and search.",
                Size = "0.7 GB",
                Category = "Embedding"
            });
        }

        return models;
    }

    // ── Advisory Message Builder ───────────────────────────────
    private static string BuildAdvisoryMessage(HardwareCapability capability, double effectiveMemoryGb)
    {
        var lines = new List<string>();

        if (capability.GpuVramBytes > 0)
        {
            lines.Add($"Your GPU ({capability.GpuName}) has {capability.GpuVramFormatted} of VRAM, which enables GPU-accelerated inference for significantly faster responses.");
        }
        else
        {
            lines.Add("No dedicated GPU detected. Models will run on CPU, which is slower but still functional. Consider a GPU with at least 8GB VRAM for the best experience.");
        }

        if (effectiveMemoryGb < 4)
        {
            lines.Add("With limited memory, stick to small models (3B parameters or less). These models are fast and surprisingly capable for basic tasks.");
        }
        else if (effectiveMemoryGb < 8)
        {
            lines.Add("You can run 7B parameter models comfortably. These provide a great balance of quality and speed for most everyday AI tasks.");
        }
        else if (effectiveMemoryGb < 16)
        {
            lines.Add("Your system can handle up to 13B parameter models, offering strong performance across chat, code, and reasoning tasks.");
        }
        else
        {
            lines.Add("Your hardware is excellent for local AI. You can run large 30-70B parameter models for near-frontier quality reasoning and generation.");
        }

        if (capability.HasNpu)
        {
            lines.Add($"NPU detected ({capability.NpuName}). Some models may leverage your NPU for additional acceleration.");
        }

        return string.Join(" ", lines);
    }

    // ── Tier Determination ─────────────────────────────────────
    private static string DetermineGpuTier(long gpuVramBytes)
    {
        return gpuVramBytes switch
        {
            0 => "No dedicated GPU",
            < 4_000_000_000L => "Entry",
            < 8_000_000_000L => "Mainstream",
            < 16_000_000_000L => "Performance",
            < 24_000_000_000L => "Enthusiast",
            _ => "Professional"
        };
    }

    private static string DeterminePerformanceTier(double effectiveMemoryGb)
    {
        return effectiveMemoryGb switch
        {
            < 4 => "Basic",
            < 8 => "Standard",
            < 16 => "Performance",
            < 32 => "High-End",
            _ => "Professional"
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
            SetError($"Failed to install {modelName}. Open the Model Manager for detailed download progress.");
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
        GpuName = "Detection failed";
        GpuVram = "Unknown";
        GpuTier = "Unknown";
        CpuName = $"{Environment.ProcessorCount}-core CPU";
        CpuCores = Environment.ProcessorCount;
        TotalRam = "Unknown";
        AvailableRam = "Unknown";
        RecommendedModelSize = "Unable to determine";
        AdvisoryMessage = "Hardware detection was unable to complete. Please ensure the application has the necessary permissions and try refreshing.";
        PerformanceTier = "Unknown";
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
