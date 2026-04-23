namespace AgentX.Core.AI.Context;

public interface ISemanticContextSelector
{
    Task<ContextSelectionResult> SelectRelevantContextAsync(
        ContextSelectionRequest request,
        CancellationToken ct = default);
}
