namespace AgentX.App.Services;

/// <summary>
/// Holds a single pending workflow launch request until the workflow page consumes it.
/// </summary>
public sealed class WorkflowLaunchService : IWorkflowLaunchService
{
    private readonly object _gate = new();
    private WorkflowLaunchRequest? _pendingRequest;

    public void StageRequest(WorkflowLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            _pendingRequest = request;
        }
    }

    public WorkflowLaunchRequest? ConsumePendingRequest()
    {
        lock (_gate)
        {
            var request = _pendingRequest;
            _pendingRequest = null;
            return request;
        }
    }
}
