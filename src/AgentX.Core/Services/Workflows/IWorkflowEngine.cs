using AgentX.Core.Services.Workflows.Models;

namespace AgentX.Core.Services.Workflows;

/// <summary>
/// Executes workflow pipelines by iterating through each step in order,
/// feeding outputs from one step into the next, tracking progress,
/// and persisting run history. Supports cancellation and progress reporting.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Executes all steps of a workflow sequentially with the provided input.
    /// Each step receives the original input and the previous step's output.
    /// Progress is reported after each step completes.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to execute.</param>
    /// <param name="input">The initial user-provided input text.</param>
    /// <param name="progress">Optional progress reporter that receives a <see cref="WorkflowStepResult"/> after each step completes.</param>
    /// <param name="ct">Cancellation token to cancel execution between steps.</param>
    /// <returns>The aggregate result of the entire workflow execution.</returns>
    Task<WorkflowRunResult> ExecuteWorkflowAsync(
        long workflowId,
        string input,
        IProgress<WorkflowStepResult>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Requests cancellation of the currently executing workflow.
    /// The engine will stop after the current step finishes and mark the run as cancelled.
    /// </summary>
    Task CancelExecutionAsync();

    /// <summary>
    /// Indicates whether a workflow is currently being executed by this engine instance.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Raised after each step completes during workflow execution.
    /// Provides the <see cref="WorkflowStepResult"/> for the completed step.
    /// </summary>
    event EventHandler<WorkflowStepResult>? StepCompleted;
}
