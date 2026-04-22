namespace AgentX.Core.AI.Models;

public class AiModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
    public long SizeBytes { get; set; }
    public string QuantizationLevel { get; set; } = string.Empty;
    public int ParameterCount { get; set; }
    public int ContextLength { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string Digest { get; set; } = string.Empty;

    public string SizeFormatted => SizeBytes switch
    {
        < 1_000_000_000 => $"{SizeBytes / 1_000_000.0:F1} MB",
        _ => $"{SizeBytes / 1_000_000_000.0:F1} GB"
    };
}

public class ModelDownloadProgress
{
    public string ModelId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long CompletedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => TotalBytes > 0 ? (double)CompletedBytes / TotalBytes * 100.0 : 0;
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tool calls made by the assistant in this message (for role="assistant").
    /// </summary>
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// The ID of the tool call this message is responding to (for role="tool").
    /// </summary>
    public string? ToolCallId { get; set; }

    /// <summary>
    /// Creates a user message.
    /// </summary>
    public static ChatMessage User(string content) =>
        new() { Role = "user", Content = content };

    /// <summary>
    /// Creates an assistant message.
    /// </summary>
    public static ChatMessage Assistant(string content) =>
        new() { Role = "assistant", Content = content };

    /// <summary>
    /// Creates an assistant message with tool calls.
    /// </summary>
    public static ChatMessage AssistantWithTools(List<ToolCall> toolCalls) =>
        new() { Role = "assistant", ToolCalls = toolCalls };

    /// <summary>
    /// Creates a system message.
    /// </summary>
    public static ChatMessage System(string content) =>
        new() { Role = "system", Content = content };

    /// <summary>
    /// Creates a tool result message.
    /// </summary>
    public static ChatMessage ToolResult(string toolCallId, string content) =>
        new() { Role = "tool", Content = content, ToolCallId = toolCallId };
}

public class HardwareCapability
{
    public string GpuName { get; set; } = "Unknown";
    public long GpuVramBytes { get; set; }
    public bool HasNpu { get; set; }
    public string NpuName { get; set; } = "None";
    public int CpuCores { get; set; }
    public string CpuName { get; set; } = "Unknown";
    public long TotalRamBytes { get; set; }
    public long AvailableRamBytes { get; set; }

    public string GpuVramFormatted => GpuVramBytes switch
    {
        0 => "No dedicated GPU",
        < 1_000_000_000 => $"{GpuVramBytes / 1_000_000.0:F0} MB",
        _ => $"{GpuVramBytes / 1_000_000_000.0:F1} GB"
    };

    public string TotalRamFormatted => $"{TotalRamBytes / 1_000_000_000.0:F0} GB";
    public string AvailableRamFormatted => $"{AvailableRamBytes / 1_000_000_000.0:F1} GB";

    public string RecommendedMaxModelSize => AvailableRamBytes switch
    {
        < 4_000_000_000L => "Up to 3B parameter models",
        < 8_000_000_000L => "Up to 7B parameter models",
        < 16_000_000_000L => "Up to 13B parameter models",
        < 32_000_000_000L => "Up to 34B parameter models",
        _ => "Up to 70B+ parameter models"
    };

    /// <summary>Whether the detected GPU is an NVIDIA GPU (CUDA-capable).</summary>
    public bool IsNvidiaGpu =>
        !string.IsNullOrEmpty(GpuName) &&
        GpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recommended GPU layer count based on available VRAM.
    /// Returns 0 for non-NVIDIA GPUs or when VRAM is insufficient.
    /// </summary>
    public int RecommendedGpuLayers => IsNvidiaGpu ? GpuVramBytes switch
    {
        < 2_000_000_000L => 0,    // < 2 GB: CPU only
        < 4_000_000_000L => 16,   // 2-4 GB: partial offload
        < 6_000_000_000L => 28,   // 4-6 GB: most layers
        < 8_000_000_000L => 33,   // 6-8 GB: all layers for 3B model
        _ => 33                    // 8+ GB: full offload
    } : 0;

    /// <summary>GPU acceleration summary for display.</summary>
    public string GpuAccelerationSummary => IsNvidiaGpu
        ? $"CUDA acceleration available ({GpuVramFormatted} VRAM, {RecommendedGpuLayers} layers)"
        : "CPU inference (no NVIDIA GPU detected)";
}
