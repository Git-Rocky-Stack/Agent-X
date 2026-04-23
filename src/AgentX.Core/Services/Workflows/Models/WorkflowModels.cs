using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Workflows.Models;

/// <summary>
/// Captures the result of executing a single workflow step, including
/// the output text, token usage, timing, and success/failure status.
/// </summary>
public record WorkflowStepResult
{
    /// <summary>The display name of the step that produced this result.</summary>
    public string StepName { get; init; } = string.Empty;

    /// <summary>The zero-based execution order of this step within the workflow.</summary>
    public int StepOrder { get; init; }

    /// <summary>The text output produced by this step.</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Estimated number of tokens consumed by this step.</summary>
    public long TokensUsed { get; init; }

    /// <summary>Wall-clock time in milliseconds that this step took to execute.</summary>
    public double DurationMs { get; init; }

    /// <summary>The model identifier used for this step (if applicable).</summary>
    public string? ModelUsed { get; init; }

    /// <summary>Indicates whether this step completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if the step failed; null on success.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Captures the aggregate result of executing an entire workflow,
/// including all individual step results, the final output, and totals.
/// </summary>
public record WorkflowRunResult
{
    /// <summary>The display name of the workflow that was executed.</summary>
    public string WorkflowName { get; init; } = string.Empty;

    /// <summary>Ordered list of results from each step in the workflow.</summary>
    public IReadOnlyList<WorkflowStepResult> Steps { get; init; } = Array.Empty<WorkflowStepResult>();

    /// <summary>The text output from the last successfully completed step.</summary>
    public string FinalOutput { get; init; } = string.Empty;

    /// <summary>Cumulative token count across all steps.</summary>
    public long TotalTokensUsed { get; init; }

    /// <summary>Total wall-clock time in milliseconds for the entire workflow execution.</summary>
    public double TotalDurationMs { get; init; }

    /// <summary>Indicates whether all steps completed successfully.</summary>
    public bool Success { get; init; }
}

/// <summary>
/// Read-only inspection model for a persisted workflow run.
/// Used to surface recent run history and reopen stored results.
/// </summary>
public record WorkflowRunHistoryItem
{
    /// <summary>The persisted workflow run identifier.</summary>
    public long RunId { get; init; }

    /// <summary>The workflow that produced the run.</summary>
    public long WorkflowId { get; init; }

    /// <summary>The persisted run status.</summary>
    public string Status { get; init; } = "pending";

    /// <summary>The original input used to start the run.</summary>
    public string InitialInput { get; init; } = string.Empty;

    /// <summary>The final or latest available output for the run.</summary>
    public string FinalOutput { get; init; } = string.Empty;

    /// <summary>Error message if the run failed or was cancelled.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>UTC timestamp for when the run started.</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>UTC timestamp for when the run completed, failed, or was cancelled.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Successful steps completed before the run ended.</summary>
    public int StepsCompleted { get; init; }

    /// <summary>Total steps configured at execution time.</summary>
    public int TotalSteps { get; init; }

    /// <summary>Total token usage recorded for the run.</summary>
    public long TotalTokensUsed { get; init; }

    /// <summary>Computed duration in milliseconds when the run has completed.</summary>
    public double? DurationMs { get; init; }

    /// <summary>Persisted per-step results for inspection.</summary>
    public IReadOnlyList<WorkflowStepResult> StepResults { get; init; } = Array.Empty<WorkflowStepResult>();
}

/// <summary>
/// Provides factory methods that create pre-built workflow templates.
/// These templates are used by <see cref="IWorkflowService.SeedBuiltInWorkflowsAsync"/>
/// to populate the database with starter workflows.
/// </summary>
public static class WorkflowTemplate
{
    /// <summary>
    /// Creates a "Summarize and Act" workflow that summarizes input,
    /// extracts key points, and generates actionable items.
    /// </summary>
    /// <returns>A fully configured <see cref="WorkflowEntity"/> with three steps.</returns>
    public static WorkflowEntity SummarizeAndAct()
    {
        var now = DateTime.UtcNow;

        return new WorkflowEntity
        {
            Name = "Summarize & Act",
            Description = "Summarize the input, extract key points, and generate action items.",
            Icon = "\uE8A1", // Document glyph
            Category = "Analysis",
            IsBuiltIn = true,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = new List<WorkflowStepEntity>
            {
                new()
                {
                    StepOrder = 0,
                    Name = "Summarize",
                    StepType = "AiPrompt",
                    PromptTemplate = "Provide a concise summary of the following content. Focus on the main ideas and most important details.\n\n{{input}}",
                },
                new()
                {
                    StepOrder = 1,
                    Name = "Extract Key Points",
                    StepType = "AiPrompt",
                    PromptTemplate = "Based on the following summary, extract and list the key points as a numbered list. Be specific and actionable.\n\nSummary:\n{{previous_output}}",
                },
                new()
                {
                    StepOrder = 2,
                    Name = "Generate Action Items",
                    StepType = "AiPrompt",
                    PromptTemplate = "Based on the following key points, generate a prioritized list of concrete action items. Each action item should be specific, measurable, and actionable. Use the format: [ ] Action item description.\n\nOriginal content:\n{{input}}\n\nKey points:\n{{previous_output}}",
                },
            },
        };
    }

    /// <summary>
    /// Creates a "Research Brief" workflow that analyzes a topic,
    /// identifies key arguments, and generates a structured brief.
    /// </summary>
    /// <returns>A fully configured <see cref="WorkflowEntity"/> with three steps.</returns>
    public static WorkflowEntity ResearchBrief()
    {
        var now = DateTime.UtcNow;

        return new WorkflowEntity
        {
            Name = "Research Brief",
            Description = "Analyze a topic, identify key arguments, and generate a structured research brief.",
            Icon = "\uE82D", // Research glyph
            Category = "Research",
            IsBuiltIn = true,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = new List<WorkflowStepEntity>
            {
                new()
                {
                    StepOrder = 0,
                    Name = "Topic Analysis",
                    StepType = "AiPrompt",
                    PromptTemplate = "Analyze the following topic or question in depth. Identify the core subject, relevant sub-topics, and areas that need exploration.\n\n{{input}}",
                },
                new()
                {
                    StepOrder = 1,
                    Name = "Identify Key Arguments",
                    StepType = "AiPrompt",
                    PromptTemplate = "Based on the analysis below, identify the key arguments, perspectives, and counterarguments related to this topic. Present multiple viewpoints fairly.\n\nAnalysis:\n{{previous_output}}",
                },
                new()
                {
                    StepOrder = 2,
                    Name = "Generate Structured Brief",
                    StepType = "AiPrompt",
                    PromptTemplate = "Create a well-structured research brief using the following format:\n\n## Executive Summary\n(2-3 sentence overview)\n\n## Background\n(Context and importance)\n\n## Key Findings\n(Main arguments and evidence)\n\n## Opposing Perspectives\n(Counterarguments and limitations)\n\n## Conclusions & Recommendations\n(Synthesis and next steps)\n\nOriginal topic:\n{{input}}\n\nKey arguments:\n{{previous_output}}",
                },
            },
        };
    }

    /// <summary>
    /// Creates a "Document Review" workflow that summarizes a document,
    /// identifies strengths and weaknesses, and suggests improvements.
    /// </summary>
    /// <returns>A fully configured <see cref="WorkflowEntity"/> with three steps.</returns>
    public static WorkflowEntity DocumentReview()
    {
        var now = DateTime.UtcNow;

        return new WorkflowEntity
        {
            Name = "Document Review",
            Description = "Summarize a document, identify strengths and weaknesses, and suggest improvements.",
            Icon = "\uE8FD", // Review glyph
            Category = "Writing",
            IsBuiltIn = true,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = new List<WorkflowStepEntity>
            {
                new()
                {
                    StepOrder = 0,
                    Name = "Document Summary",
                    StepType = "AiPrompt",
                    PromptTemplate = "Provide a comprehensive summary of the following document. Cover all major sections, themes, and conclusions.\n\n{{input}}",
                },
                new()
                {
                    StepOrder = 1,
                    Name = "Strengths & Weaknesses",
                    StepType = "AiPrompt",
                    PromptTemplate = "Analyze the following document and its summary. Identify:\n\n**Strengths:**\n- What the document does well (clarity, structure, arguments, evidence, etc.)\n\n**Weaknesses:**\n- Areas that need improvement (gaps, unclear sections, weak arguments, etc.)\n\nDocument:\n{{input}}\n\nSummary:\n{{previous_output}}",
                },
                new()
                {
                    StepOrder = 2,
                    Name = "Improvement Suggestions",
                    StepType = "AiPrompt",
                    PromptTemplate = "Based on the strengths and weaknesses analysis below, provide specific, actionable suggestions for improving this document. Organize suggestions by priority (High, Medium, Low).\n\nStrengths & Weaknesses:\n{{previous_output}}",
                },
            },
        };
    }

    /// <summary>
    /// Creates a "Content Repurpose" workflow that takes content and
    /// rewrites it for multiple formats: tweet thread, email, and blog post.
    /// </summary>
    /// <returns>A fully configured <see cref="WorkflowEntity"/> with four steps.</returns>
    public static WorkflowEntity ContentRepurpose()
    {
        var now = DateTime.UtcNow;

        return new WorkflowEntity
        {
            Name = "Content Repurpose",
            Description = "Take content and rewrite it for different formats: tweet thread, email, and blog post.",
            Icon = "\uE8F2", // Sync glyph
            Category = "Writing",
            IsBuiltIn = true,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = new List<WorkflowStepEntity>
            {
                new()
                {
                    StepOrder = 0,
                    Name = "Extract Core Message",
                    StepType = "AiPrompt",
                    PromptTemplate = "Analyze the following content and extract the core message, key takeaways, and most compelling points. Be concise but thorough.\n\n{{input}}",
                },
                new()
                {
                    StepOrder = 1,
                    Name = "Tweet Thread",
                    StepType = "AiPrompt",
                    PromptTemplate = "Using the core message and key points below, create an engaging Twitter/X thread (5-8 tweets). Each tweet should be under 280 characters. Start with a hook. Use numbering (1/, 2/, etc.).\n\nCore message:\n{{previous_output}}",
                },
                new()
                {
                    StepOrder = 2,
                    Name = "Professional Email",
                    StepType = "AiPrompt",
                    PromptTemplate = "Using the original content and the core message, write a professional email that communicates the key points. Include a clear subject line, greeting, body, call-to-action, and sign-off.\n\nOriginal content:\n{{input}}\n\nCore message:\n{{previous_output}}",
                    TemperatureOverride = 0.6,
                },
                new()
                {
                    StepOrder = 3,
                    Name = "Blog Post",
                    StepType = "AiPrompt",
                    PromptTemplate = "Using the original content below, write an engaging blog post (500-800 words). Include:\n- An attention-grabbing headline\n- An engaging introduction\n- Well-organized body with subheadings\n- A conclusion with a call-to-action\n\nWrite in a conversational yet professional tone.\n\nOriginal content:\n{{input}}",
                    TemperatureOverride = 0.8,
                },
            },
        };
    }
}
