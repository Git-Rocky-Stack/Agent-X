namespace AgentX.App.Services;

/// <summary>
/// Represents a staged request to open the workflow runner with prefilled input.
/// </summary>
public sealed class WorkflowLaunchRequest
{
    public required string InputText { get; init; }
    public required string SourceLabel { get; init; }
    public string? RecommendedWorkflowName { get; init; }
}
