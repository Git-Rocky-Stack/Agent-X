namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Materializes durable cross-conversation theme clusters from the latest
/// persisted conversation summaries.
/// </summary>
public interface IConversationThemeClusterService
{
    Task<bool> MaterializeConversationThemeAsync(
        long conversationId,
        bool forceRefresh = false,
        CancellationToken ct = default);

    Task<int> RefreshStaleClustersAsync(
        int maxConversations = 4,
        CancellationToken ct = default);
}
