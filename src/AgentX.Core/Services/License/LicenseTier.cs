namespace AgentX.Core.Services.License;

/// <summary>
/// Defines the available license tiers for Agent-X.
/// Each tier unlocks progressively more features and higher limits.
/// </summary>
public enum LicenseTier
{
    /// <summary>Free tier — limited to 50 documents, basic models only.</summary>
    Trial,

    /// <summary>$79 — 500 documents, all chat models.</summary>
    Starter,

    /// <summary>$149 — Unlimited documents, all features including intelligence.</summary>
    Professional,

    /// <summary>$249 — Unlimited everything plus priority support.</summary>
    Ultimate
}
