namespace AgentX.App.Services;

/// <summary>
/// Stages item-specific drill-in requests from the Operations page so the owning
/// page can consume them after navigation and focus the requested record.
/// </summary>
public interface IOperationsDrillInService
{
    void StageConversationRequest(OperationsConversationDrillInRequest request);
    OperationsConversationDrillInRequest? ConsumePendingConversationRequest();

    void StageInboxRequest(OperationsInboxDrillInRequest request);
    OperationsInboxDrillInRequest? ConsumePendingInboxRequest();

    void StageDocumentRequest(OperationsDocumentDrillInRequest request);
    OperationsDocumentDrillInRequest? ConsumePendingDocumentRequest();

    void StageWorkflowRunRequest(OperationsWorkflowRunDrillInRequest request);
    OperationsWorkflowRunDrillInRequest? ConsumePendingWorkflowRunRequest();

    void StageSyncRequest(OperationsSyncDrillInRequest request);
    OperationsSyncDrillInRequest? ConsumePendingSyncRequest();

    void StagePluginRequest(OperationsPluginDrillInRequest request);
    OperationsPluginDrillInRequest? ConsumePendingPluginRequest();
}

public sealed record OperationsConversationDrillInRequest(
    long ConversationId,
    string SourceLabel);

public sealed record OperationsInboxDrillInRequest(
    long ItemId,
    string SourceLabel);

public sealed record OperationsDocumentDrillInRequest(
    long DocumentId,
    string SourceLabel);

public sealed record OperationsWorkflowRunDrillInRequest(
    long WorkflowId,
    long RunId,
    string SourceLabel);

public sealed record OperationsSyncDrillInRequest(
    long SyncLogId,
    string SourceLabel);

public sealed record OperationsPluginDrillInRequest(
    long PluginId,
    string SourceLabel);
