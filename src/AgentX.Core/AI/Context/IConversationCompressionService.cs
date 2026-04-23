namespace AgentX.Core.AI.Context;

public interface IConversationCompressionService
{
    Task<ConversationCompressionResult> CompressAsync(
        ConversationCompressionRequest request,
        CancellationToken ct = default);
}
