using System.Text.RegularExpressions;

namespace AgentX.App.Helpers;

public enum StatusTone
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info
}

public static class StatusToneResolver
{
    private static readonly string[] DangerTokens =
    [
        "refresh error",
        "needs attention",
        "failed",
        "error",
        "offline",
        "disconnected",
        "critical",
        "unavailable"
    ];

    private static readonly string[] WarningTokens =
    [
        "stale",
        "pending",
        "loading",
        "processing",
        "syncing",
        "warning",
        "busy",
        "queued",
        "conflict",
        "cancelled",
        "canceled"
    ];

    private static readonly string[] SuccessTokens =
    [
        "current",
        "success",
        "completed",
        "healthy",
        "searchable",
        "connected",
        "online",
        "active",
        "ready",
        "enabled"
    ];

    private static readonly string[] InfoTokens =
    [
        "running",
        "indexing",
        "updating",
        "installing",
        "downloading",
        "info"
    ];

    private static readonly string[] NeutralTokens =
    [
        "idle",
        "paused",
        "disabled",
        "inactive",
        "off",
        "not configured",
        "not connected",
        "not installed",
        "none",
        "installed",
        "history",
        "analytics",
        "plugins",
        "unknown"
    ];

    public static StatusTone Resolve(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return StatusTone.Neutral;
        }

        if (ContainsAny(normalized, DangerTokens))
        {
            return StatusTone.Danger;
        }

        if (ContainsAny(normalized, WarningTokens))
        {
            return StatusTone.Warning;
        }

        if (ContainsAny(normalized, SuccessTokens))
        {
            return StatusTone.Success;
        }

        if (ContainsAny(normalized, InfoTokens))
        {
            return StatusTone.Info;
        }

        if (ContainsAny(normalized, NeutralTokens))
        {
            return StatusTone.Neutral;
        }

        return StatusTone.Neutral;
    }

    private static bool ContainsAny(string status, IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            // Word-boundary match: "inactive" must NOT match the "active" success
            // token, and "Collaborative sync is off" must NOT match the "offline"
            // danger token. Substring matching here inverted status colors
            // (negative states rendered green) — see design audit B2.
            if (Regex.IsMatch(status, $@"\b{Regex.Escape(token)}\b"))
            {
                return true;
            }
        }

        return false;
    }
}
