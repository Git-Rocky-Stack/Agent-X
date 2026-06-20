namespace AgentX.Core.AI.Routing;

/// <summary>
/// Detects the <see cref="TaskType"/> of a given prompt using explicit tags
/// or keyword heuristics. Used by the <see cref="IModelRouterService"/>
/// to determine optimal model routing.
/// </summary>
public interface ITaskTypeDetector
{
    /// <summary>
    /// Analyzes a prompt and returns the detected <see cref="TaskType"/>.
    /// Detection priority: explicit tag override &gt; keyword matching &gt; default (chat).
    /// </summary>
    /// <param name="prompt">The user prompt to classify.</param>
    /// <returns>The detected task type, never null.</returns>
    TaskType Detect(string prompt);
}
