namespace AgentX.App.Services;

/// <summary>
/// Stages a single pending workflow launch request so another page can
/// consume it after navigation.
/// </summary>
public interface IWorkflowLaunchService
{
    void StageRequest(WorkflowLaunchRequest request);
    WorkflowLaunchRequest? ConsumePendingRequest();
}
