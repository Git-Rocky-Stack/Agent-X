using System.Collections.Generic;

namespace AgentX.Core.Services.Privacy;

/// <summary>
/// One way the current configuration causes data to leave the machine. Each disclosure names the
/// product <see cref="Surface"/> responsible and a plain-language <see cref="Detail"/> of what is
/// transmitted and to whom, so the UI can replace the blanket "no cloud" claim with an accurate,
/// state-aware statement (AX-QA-008).
/// </summary>
/// <param name="Surface">Short label for the feature, e.g. "AI model", "Web search".</param>
/// <param name="Detail">User-facing sentence describing what leaves the machine and where it goes.</param>
public sealed record PrivacyDisclosure(string Surface, string Detail);

/// <summary>
/// The aggregate privacy posture derived from the user's current settings. When
/// <see cref="IsFullyLocal"/> is true no enabled feature transmits data off the machine and the
/// strong local-only claim is accurate; otherwise <see cref="Disclosures"/> enumerates every active
/// surface that sends data to a third party.
/// </summary>
public sealed record PrivacyStatus(bool IsFullyLocal, IReadOnlyList<PrivacyDisclosure> Disclosures)
{
    /// <summary>Canonical "nothing leaves the machine" status with no disclosures.</summary>
    public static PrivacyStatus FullyLocal { get; } =
        new(true, new List<PrivacyDisclosure>());
}
