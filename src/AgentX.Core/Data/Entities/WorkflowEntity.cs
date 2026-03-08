namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a reusable multi-step AI workflow (prompt chain).
/// Each workflow contains an ordered collection of steps that execute
/// sequentially, with each step's output feeding into the next.
/// </summary>
public class WorkflowEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>User-friendly display name for the workflow.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description explaining what the workflow does.</summary>
    public string? Description { get; set; }

    /// <summary>Optional Segoe MDL2 Assets glyph for the workflow icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Category for grouping: Custom, Research, Writing, Analysis.</summary>
    public string Category { get; set; } = "Custom";

    /// <summary>Indicates whether this workflow was seeded as a built-in template.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Controls whether the workflow appears in the user's workflow list.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Timestamp when the workflow was created (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Timestamp when the workflow was last modified (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Number of times this workflow has been executed.</summary>
    public int RunCount { get; set; }

    // Navigation
    /// <summary>Ordered collection of steps that define this workflow's pipeline.</summary>
    public ICollection<WorkflowStepEntity> Steps { get; set; } = new List<WorkflowStepEntity>();

    /// <summary>Historical runs of this workflow.</summary>
    public ICollection<WorkflowRunEntity> Runs { get; set; } = new List<WorkflowRunEntity>();
}
