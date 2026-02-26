namespace AgentX.Core.AI.Models;

public class AiModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
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
}
