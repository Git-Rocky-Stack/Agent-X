namespace AgentX.Core.Services.Export.Models;

/// <summary>
/// Represents a standalone titled text artifact that can be exported to file,
/// along with optional metadata that provides provenance and context.
/// </summary>
public sealed class TextArtifactExportItem
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
