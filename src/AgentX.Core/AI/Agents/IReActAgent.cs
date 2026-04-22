using AgentX.Core.AI.Models;

namespace AgentX.Core.AI.Agents;

/// <summary>
/// ReAct (Reasoning + Acting) agent that performs multi-step reasoning
/// by alternating between thought, action (tool calls), and observation.
/// </summary>
public interface IReActAgent
{
    /// <summary>
    /// Executes a task using the ReAct loop.
    /// </summary>
    /// <param name="task">The user's task/request.</param>
    /// <param name="availableTools">Tools the agent can use.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <param name="maxIterations">Maximum number of reasoning iterations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result including final answer and all reasoning steps.</returns>
    Task<ReActResult> ExecuteAsync(
        string task,
        IReadOnlyList<ToolDefinition> availableTools,
        string? systemPrompt = null,
        int maxIterations = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Streams the ReAct execution progress.
    /// </summary>
    /// <param name="task">The user's task/request.</param>
    /// <param name="availableTools">Tools the agent can use.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <param name="maxIterations">Maximum number of reasoning iterations.</param>
    /// <param name="onStep">Callback invoked for each reasoning step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final result.</returns>
    Task<ReActResult> ExecuteStreamingAsync(
        string task,
        IReadOnlyList<ToolDefinition> availableTools,
        string? systemPrompt = null,
        int maxIterations = 10,
        Action<ReActStep>? onStep = null,
        CancellationToken ct = default);
}
