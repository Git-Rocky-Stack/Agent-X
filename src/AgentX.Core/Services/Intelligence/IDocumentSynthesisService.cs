using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

public interface IDocumentSynthesisService
{
    Task<ComparisonSynthesisResult> SynthesizeComparisonAsync(
        ComparisonSynthesisRequest request,
        CancellationToken ct = default);
}

public sealed class ComparisonSynthesisRequest
{
    public IReadOnlyDictionary<string, string> ContentByDocument { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public ComparisonOptions Options { get; init; } = new();
}

public sealed class ComparisonSynthesisResult
{
    public string RawResponse { get; init; } = string.Empty;
    public long EstimatedPromptTokens { get; init; }
}
