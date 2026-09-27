using AgentX.Core.Services.TemporalIdentity.Models;

namespace AgentX.Core.Services.TemporalIdentity;

/// <summary>
/// "Draft as Me": has the active AI provider write a draft in the user's voice, from what
/// Temporal Identity has recorded about how they write and what they thought.
/// </summary>
public interface IVoiceDraftService
{
    /// <summary>
    /// Prepares a draft for <paramref name="request"/> and returns it ready to stream. The prompt
    /// carries the learned voice profile, the stances the user held on related topics at
    /// <see cref="VoiceDraftRequest.At"/>, and the insights saved by then; it asks the model not
    /// to invent facts about the user.
    /// </summary>
    /// <returns>
    /// The draft, whose <see cref="VoiceDraft.Text"/> streams from the provider as it is written,
    /// or <c>null</c> when no AI provider is available (none is set up, or it cannot be reached).
    /// Nothing is sent to a provider then.
    /// </returns>
    /// <exception cref="ArgumentException">The request has no context.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<VoiceDraft?> StartDraftAsync(VoiceDraftRequest request, CancellationToken ct = default);
}

/// <summary>What to draft, and as of when.</summary>
/// <param name="Context">What the draft is about. Required.</param>
/// <param name="Goal">What the draft should achieve. Optional.</param>
/// <param name="At">
/// The point in time whose views the draft follows: only stances and insights recorded by then
/// are used. Null uses everything recorded up to now.
/// </param>
public sealed record VoiceDraftRequest(string Context, string? Goal, DateTime? At);

/// <summary>A draft being written.</summary>
/// <param name="Basis">What the draft is based on.</param>
/// <param name="Text">
/// The draft as the provider writes it, piece by piece. Enumerating it runs the model; a provider
/// failure surfaces here as the provider's exception, and cancelling the token passed to
/// <see cref="IVoiceDraftService.StartDraftAsync"/> stops it with an
/// <see cref="OperationCanceledException"/>.
/// </param>
public sealed record VoiceDraft(VoiceDraftBasis Basis, IAsyncEnumerable<string> Text);

/// <summary>What a draft was written from, so the page can say so next to it.</summary>
/// <param name="WrittenBy">The model, and the provider that runs it, e.g. "llama3.2 (Ollama)".</param>
/// <param name="AsOf">The point in time the views and insights were taken at.</param>
/// <param name="VoiceProfile">The voice profile the prompt described, or null before any was learned.</param>
/// <param name="Views">The recorded stances on related topics that the prompt carried.</param>
/// <param name="Insights">The saved insights that the prompt carried as background.</param>
public sealed record VoiceDraftBasis(
    string WrittenBy,
    DateTime AsOf,
    VoiceProfileEntity? VoiceProfile,
    IReadOnlyList<VoiceDraftView> Views,
    IReadOnlyList<VoiceDraftInsight> Insights);

/// <summary>A stance the user held on a topic at the chosen point in time.</summary>
public sealed record VoiceDraftView(string Topic, string Stance);

/// <summary>An insight saved by the chosen point in time.</summary>
public sealed record VoiceDraftInsight(string Text, DateTime SavedAt);
