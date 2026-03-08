namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a single step within a workflow pipeline.
/// Steps execute in <see cref="StepOrder"/> sequence. Each step receives
/// the original user input and the previous step's output, then produces
/// its own output for the next step in the chain.
/// </summary>
public class WorkflowStepEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Foreign key to the parent workflow.</summary>
    public long WorkflowId { get; set; }

    /// <summary>Zero-based execution order within the workflow.</summary>
    public int StepOrder { get; set; }

    /// <summary>User-friendly display name for this step.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of operation this step performs.
    /// Supported values: AiPrompt, DocumentLookup, TextTransform, ConditionalBranch, OutputFormat.
    /// </summary>
    public string StepType { get; set; } = "AiPrompt";

    /// <summary>
    /// The prompt template for this step. Supports <c>{{input}}</c> (original user input)
    /// and <c>{{previous_output}}</c> (output from the preceding step) placeholders.
    /// </summary>
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>Optional model identifier to use for this specific step, overriding the default.</summary>
    public string? ModelOverride { get; set; }

    /// <summary>Optional temperature override for this specific step.</summary>
    public double? TemperatureOverride { get; set; }

    /// <summary>Optional maximum token limit override for this specific step.</summary>
    public int? MaxTokensOverride { get; set; }

    /// <summary>
    /// Additional step-specific configuration stored as a JSON string.
    /// The schema depends on <see cref="StepType"/>. For example, a TextTransform
    /// step might store <c>{"transform":"uppercase"}</c>.
    /// </summary>
    public string? ConfigJson { get; set; }

    // Navigation
    /// <summary>The parent workflow this step belongs to.</summary>
    public WorkflowEntity Workflow { get; set; } = null!;
}
