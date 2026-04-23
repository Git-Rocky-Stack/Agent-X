namespace AgentX.Core.Services.Intelligence.Models;

public abstract class DigestTrendItem
{
    public int Count { get; init; }
    public int PreviousCount { get; init; }
    public int DeltaCount => Count - PreviousCount;
    public string Trend => DeltaCount switch
    {
        > 0 when PreviousCount == 0 => "new",
        > 0 => "up",
        < 0 => "down",
        _ => "flat"
    };
}

public sealed class DigestSearchTrend : DigestTrendItem
{
    public string Query { get; init; } = string.Empty;
}

public sealed class DigestCollectionTrend : DigestTrendItem
{
    public string Name { get; init; } = string.Empty;
}

public sealed class DigestFileTypeTrend : DigestTrendItem
{
    public string Type { get; init; } = string.Empty;
}
