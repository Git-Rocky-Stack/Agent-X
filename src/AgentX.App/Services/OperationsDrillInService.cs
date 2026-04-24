namespace AgentX.App.Services;

/// <summary>
/// Holds one pending drill-in request per operations surface until the target
/// page consumes it after navigation.
/// </summary>
public sealed class OperationsDrillInService : IOperationsDrillInService
{
    private readonly object _gate = new();
    private OperationsConversationDrillInRequest? _pendingConversationRequest;
    private OperationsInboxDrillInRequest? _pendingInboxRequest;
    private OperationsWorkflowRunDrillInRequest? _pendingWorkflowRunRequest;
    private OperationsSyncDrillInRequest? _pendingSyncRequest;
    private OperationsPluginDrillInRequest? _pendingPluginRequest;

    public void StageConversationRequest(OperationsConversationDrillInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            _pendingConversationRequest = request;
        }
    }

    public OperationsConversationDrillInRequest? ConsumePendingConversationRequest()
    {
        lock (_gate)
        {
            var request = _pendingConversationRequest;
            _pendingConversationRequest = null;
            return request;
        }
    }

    public void StageInboxRequest(OperationsInboxDrillInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            _pendingInboxRequest = request;
        }
    }

    public OperationsInboxDrillInRequest? ConsumePendingInboxRequest()
    {
        lock (_gate)
        {
            var request = _pendingInboxRequest;
            _pendingInboxRequest = null;
            return request;
        }
    }

    public void StageWorkflowRunRequest(OperationsWorkflowRunDrillInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            _pendingWorkflowRunRequest = request;
        }
    }

    public OperationsWorkflowRunDrillInRequest? ConsumePendingWorkflowRunRequest()
    {
        lock (_gate)
        {
            var request = _pendingWorkflowRunRequest;
            _pendingWorkflowRunRequest = null;
            return request;
        }
    }

    public void StageSyncRequest(OperationsSyncDrillInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            _pendingSyncRequest = request;
        }
    }

    public OperationsSyncDrillInRequest? ConsumePendingSyncRequest()
    {
        lock (_gate)
        {
            var request = _pendingSyncRequest;
            _pendingSyncRequest = null;
            return request;
        }
    }

    public void StagePluginRequest(OperationsPluginDrillInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            _pendingPluginRequest = request;
        }
    }

    public OperationsPluginDrillInRequest? ConsumePendingPluginRequest()
    {
        lock (_gate)
        {
            var request = _pendingPluginRequest;
            _pendingPluginRequest = null;
            return request;
        }
    }
}
