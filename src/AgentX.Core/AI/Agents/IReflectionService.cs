using AgentX.Core.AI.Models;

namespace AgentX.Core.AI.Agents;

/// <summary>
/// Provides reflection and self-correction capabilities for AI responses.
/// Critiques generated content and suggests improvements.
/// </summary>
public interface IReflectionService
{
    /// <summary>
    /// Critiques a response based on the query and context used to generate it.
    /// </summary>
    /// <param name="query">The original user query.</param>
    /// <param name="response">The generated response to critique.</param>
    /// <param name="context">The RAG context or supporting information used.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A reflection result with critique points and suggestions.</returns>
    Task<ReflectionResult> CritiqueResponseAsync(
        string query,
        string response,
        IReadOnlyList<string> context,
        CancellationToken ct = default);

    /// <summary>
    /// Refines a response based on critique feedback.
    /// </summary>
    /// <param name="original">The original response.</param>
    /// <param name="critiques">List of critiques to address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A refined response addressing the critiques.</returns>
    Task<string> RefineResponseAsync(
        string original,
        IReadOnlyList<ReflectionCritique> critiques,
        CancellationToken ct = default);

    /// <summary>
    /// Performs a full reflection cycle: critique and refine in one operation.
    /// </summary>
    /// <param name="query">The original user query.</param>
    /// <param name="response">The generated response.</param>
    /// <param name="context">The context used to generate the response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refined response after reflection.</returns>
    Task<string> ReflectAndRefineAsync(
        string query,
        string response,
        IReadOnlyList<string> context,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a reflection/critique operation.
/// </summary>
public class ReflectionResult
{
    /// <summary>
    /// Overall quality score (0-1).
    /// </summary>
    public double QualityScore { get; set; }

    /// <summary>
    /// Individual critique points.
    /// </summary>
    public List<ReflectionCritique> Critiques { get; set; } = new();

    /// <summary>
    /// Whether the response passes quality thresholds.
    /// </summary>
    public bool IsPassing => QualityScore >= 0.7 && Critiques.All(c => c.Severity != CritiqueSeverity.High);
}

/// <summary>
/// Individual critique point.
/// </summary>
public class ReflectionCritique
{
    /// <summary>
    /// The aspect being critiqued.
    /// </summary>
    public CritiqueAspect Aspect { get; set; }

    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public CritiqueSeverity Severity { get; set; }

    /// <summary>
    /// Description of the issue.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Suggested fix or improvement.
    /// </summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary>
    /// Location in the response (if applicable).
    /// </summary>
    public string? Location { get; set; }
}

/// <summary>
/// Aspects of a response that can be critiqued.
/// </summary>
public enum CritiqueAspect
{
    /// <summary>Accuracy of information</summary>
    Accuracy,
    /// <summary>Relevance to the query</summary>
    Relevance,
    /// <summary>Clarity and readability</summary>
    Clarity,
    /// <summary>Completeness of the answer</summary>
    Completeness,
    /// <summary>Proper citation of sources</summary>
    Citation,
    /// <summary>Tone and style</summary>
    Tone,
    /// <summary>Formatting and structure</summary>
    Formatting,
    /// <summary>Factual correctness</summary>
    Factuality,
    /// <summary>Avoidance of hallucinations</summary>
    Grounding
}

/// <summary>
/// Severity levels for critiques.
/// </summary>
public enum CritiqueSeverity
{
    /// <summary>Minor improvement opportunity</summary>
    Low,
    /// <summary>Moderate issue that should be addressed</summary>
    Medium,
    /// <summary>Critical issue that must be fixed</summary>
    High
}
