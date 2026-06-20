namespace AgentX.Core.AI.Routing;

/// <summary>
/// Routes prompts to the optimal AI provider and model based on the active
/// <see cref="RoutingProfile"/>, detected <see cref="TaskType"/>, and
/// available providers. Emits a <see cref="DecisionMade"/> event for
/// observability and UI feedback.
/// </summary>
public interface IModelRouterService
{
    /// <summary>
    /// The currently active routing profile.
    /// </summary>
    RoutingProfile ActiveProfile { get; }

    /// <summary>
    /// Sets the active routing profile.
    /// </summary>
    /// <param name="profile">The profile to activate.</param>
    void SetActiveProfile(RoutingProfile profile);

    /// <summary>
    /// Sets the active routing profile by its identifier.
    /// Unknown IDs fall back to <see cref="RoutingProfile.Balanced"/>.
    /// </summary>
    /// <param name="profileId">The profile identifier (e.g. "balanced").</param>
    void SetActiveProfile(string profileId);

    /// <summary>
    /// Routes a prompt to the optimal provider/model based on the active profile
    /// and the detected task type.
    /// </summary>
    /// <param name="prompt">The user prompt to route.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A routing decision with provider, model, and reasoning.</returns>
    Task<RoutingDecision> RouteAsync(string prompt, CancellationToken ct = default);

    /// <summary>
    /// Routes a prompt using an explicit task type override (skipping detection).
    /// </summary>
    /// <param name="prompt">The user prompt (used for context in the decision reason).</param>
    /// <param name="taskTypeOverride">The task type to use instead of auto-detection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A routing decision with provider, model, and reasoning.</returns>
    Task<RoutingDecision> RouteAsync(string prompt, TaskType taskTypeOverride, CancellationToken ct = default);

    /// <summary>
    /// Fires whenever a routing decision is made. Useful for UI indicators and telemetry.
    /// </summary>
    event EventHandler<RoutingDecision>? DecisionMade;
}
