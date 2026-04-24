namespace AgentX.App.Services;

/// <summary>
/// Executes safe remediation actions surfaced from the Operations hub.
/// These actions are intentionally bounded so the page can trigger them
/// without needing to reach into view-specific orchestration code.
/// </summary>
public interface IOperationsActionService
{
    Task<OperationsActionResult> EnableConnectorAsync(long pluginId, CancellationToken ct = default);

    Task<OperationsActionResult> GenerateInboxPreviewsAsync(CancellationToken ct = default);

    Task<OperationsActionResult> RefreshConversationSummariesAsync(
        int maxConversations = 4,
        CancellationToken ct = default);

    Task<OperationsActionResult> RunManualSyncAsync(CancellationToken ct = default);
}

public sealed record OperationsActionResult(
    bool IsSuccess,
    string Message);
