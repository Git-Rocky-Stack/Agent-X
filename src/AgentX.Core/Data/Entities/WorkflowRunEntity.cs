namespace AgentX.Core.Data.Entities;

/// <summary>
/// Records the execution history of a single workflow run.
/// Tracks status, timing, per-step outputs, token usage, and any errors
/// that occurred during execution.
/// </summary>
public class WorkflowRunEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Foreign key to the workflow that was executed.</summary>
    public long WorkflowId { get; set; }

    /// <summary>
    /// Current status of the run.
    /// Valid values: pending, running, completed, failed, cancelled.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>The original user-provided input text that started the run.</summary>
    public string? InitialInput { get; set; }

    /// <summary>The final output produced by the last step in the workflow.</summary>
    public string? FinalOutput { get; set; }

    /// <summary>Error message if the run failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Timestamp when the run was started (UTC).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Timestamp when the run completed, failed, or was cancelled (UTC).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Number of steps that completed successfully before the run ended.</summary>
    public int StepsCompleted { get; set; }

    /// <summary>Total number of steps in the workflow at the time of execution.</summary>
    public int TotalSteps { get; set; }

    /// <summary>
    /// JSON-serialized array of per-step output details.
    /// Each element is a serialized <c>WorkflowStepResult</c>.
    /// </summary>
    public string? StepOutputsJson { get; set; }

    /// <summary>Cumulative token count across all steps in the run.</summary>
    public long TotalTokensUsed { get; set; }

    // Navigation
    /// <summary>The workflow that was executed.</summary>
    public WorkflowEntity Workflow { get; set; } = null!;
}
