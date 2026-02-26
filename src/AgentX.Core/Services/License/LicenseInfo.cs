namespace AgentX.Core.Services.License;

/// <summary>
/// Immutable snapshot of the current license state.
/// Provides feature gates, document limits, and display helpers for the UI layer.
/// </summary>
public class LicenseInfo
{
    // ── Core Properties ──────────────────────────────────────────────

    public LicenseTier Tier { get; init; } = LicenseTier.Trial;
    public bool IsActivated { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int MaxDocuments { get; init; }

    // ── Feature Gates ────────────────────────────────────────────────

    /// <summary>Starter+ can use advanced/larger chat models.</summary>
    public bool CanUseAdvancedModels => Tier >= LicenseTier.Starter;

    /// <summary>Professional+ can use intelligence features (summaries, duplicate detection, organization suggestions).</summary>
    public bool CanUseIntelligenceFeatures => Tier >= LicenseTier.Professional;

    /// <summary>Professional+ have no document cap.</summary>
    public bool CanUseUnlimitedDocuments => Tier >= LicenseTier.Professional;

    /// <summary>Ultimate tier includes priority support.</summary>
    public bool CanUsePrioritySupport => Tier == LicenseTier.Ultimate;

    /// <summary>
    /// Checks whether the current tier grants access to a named feature.
    /// Feature names are case-insensitive.
    /// </summary>
    public bool HasFeature(string feature) => feature.ToUpperInvariant() switch
    {
        "ADVANCED_MODELS" => CanUseAdvancedModels,
        "INTELLIGENCE" => CanUseIntelligenceFeatures,
        "UNLIMITED_DOCUMENTS" => CanUseUnlimitedDocuments,
        "PRIORITY_SUPPORT" => CanUsePrioritySupport,
        _ => false
    };

    // ── Document Limits ──────────────────────────────────────────────

    /// <summary>
    /// Returns the maximum number of documents allowed for the given tier.
    /// Professional and Ultimate tiers are unlimited (int.MaxValue).
    /// </summary>
    public static int GetDocumentLimit(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 50,
        LicenseTier.Starter => 500,
        _ => int.MaxValue // Professional and Ultimate = unlimited
    };

    // ── Display Helpers ──────────────────────────────────────────────

    /// <summary>Human-readable tier name for UI display.</summary>
    public string TierDisplayName => Tier.ToString();

    /// <summary>Hex color code for the tier badge in the UI.</summary>
    public string TierBadgeColor => Tier switch
    {
        LicenseTier.Trial => "#666666",
        LicenseTier.Starter => "#3B82F6",
        LicenseTier.Professional => "#C41E3A",
        LicenseTier.Ultimate => "#F59E0B",
        _ => "#666666"
    };

    /// <summary>
    /// Returns a human-readable string for the document limit.
    /// Shows "Unlimited" for Professional and Ultimate tiers.
    /// </summary>
    public string DocumentLimitDisplay => MaxDocuments == int.MaxValue
        ? "Unlimited"
        : MaxDocuments.ToString("N0");
}
