using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Privacy;

/// <summary>
/// Derives the application's real privacy posture from its settings so the UI can make an accurate,
/// state-aware disclosure instead of an unconditional "your data never leaves this machine" claim
/// (AX-QA-008). A feature counts against full-local only when the user has actually enabled it.
/// </summary>
public interface IPrivacyStatusService
{
    /// <summary>
    /// Pure evaluation of a settings snapshot. Returns <see cref="PrivacyStatus.FullyLocal"/> when no
    /// enabled feature transmits data off the machine; otherwise a status listing every active
    /// cloud/third-party surface.
    /// </summary>
    PrivacyStatus Evaluate(AppSettings settings);

    /// <summary>Loads the current settings and evaluates them via <see cref="Evaluate"/>.</summary>
    Task<PrivacyStatus> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pure evaluation of where one message sent from chat goes. Empty when it stays on this
    /// computer. Unlike <see cref="Evaluate"/>, which covers every enabled feature, this follows the
    /// message: the provider that answers it (<paramref name="activeProviderId"/>, or the saved
    /// provider when that is null), model routing, and web search only while Research Mode is on
    /// for the message (<paramref name="researchModeOn"/>) and switched on in Settings, which is
    /// when chat searches.
    /// </summary>
    IReadOnlyList<PromptRecipient> EvaluateChatMessage(AppSettings settings, string? activeProviderId, bool researchModeOn);

    /// <summary>Loads the current settings and evaluates them via <see cref="EvaluateChatMessage"/>.</summary>
    Task<IReadOnlyList<PromptRecipient>> GetChatMessageRecipientsAsync(
        string? activeProviderId,
        bool researchModeOn,
        CancellationToken cancellationToken = default);
}
