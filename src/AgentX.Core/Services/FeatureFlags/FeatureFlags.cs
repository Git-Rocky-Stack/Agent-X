namespace AgentX.Core.Services.FeatureFlags;

/// <summary>
/// Registry of all known feature flags with their default states.
/// Add new features here before referencing them in code.
/// </summary>
public static class FeatureFlags
{
    // ── AI Features ─────────────────────────────────────────────────
    public static readonly FeatureFlag ConversationMemory = new("ai.conversation_memory", true, "Conversation memory and context retention");
    public static readonly FeatureFlag StreamingResponses = new("ai.streaming_responses", true, "Stream AI responses token by token");
    public static readonly FeatureFlag AutoTagging = new("ai.auto_tagging", true, "Automatically tag documents during indexing");

    // ── Search ──────────────────────────────────────────────────────
    public static readonly FeatureFlag HybridSearch = new("search.hybrid_mode", true, "Enable hybrid semantic+keyword search");
    public static readonly FeatureFlag SearchCaching = new("search.caching", true, "Cache search results for faster repeated queries");

    // ── Intelligence ────────────────────────────────────────────────
    public static readonly FeatureFlag KnowledgeGraph = new("intelligence.knowledge_graph", true, "Knowledge graph visualization");
    public static readonly FeatureFlag DuplicateDetection = new("intelligence.duplicate_detection", true, "Detect duplicate/near-duplicate documents");
    public static readonly FeatureFlag DigestReports = new("intelligence.digest_reports", true, "Generate periodic digest reports");

    // ── Sync ────────────────────────────────────────────────────────
    public static readonly FeatureFlag CollaborativeSync = new("sync.collaborative", true, "Collaborative sync via shared folder");
    public static readonly FeatureFlag AutoSync = new("sync.auto", true, "Automatic background sync");

    // ── Plugins ─────────────────────────────────────────────────────
    public static readonly FeatureFlag PluginSystem = new("plugins.enabled", true, "Plugin installation and management");

    // ── Experimental ────────────────────────────────────────────────
    public static readonly FeatureFlag AudioTranscription = new("experimental.audio_transcription", false, "Audio file transcription (experimental)");
    public static readonly FeatureFlag WebImport = new("experimental.web_import", true, "Import content from web URLs");
    public static readonly FeatureFlag WorkflowAutomation = new("experimental.workflow_automation", true, "Multi-step workflow execution");
    public static readonly FeatureFlag BatchOperations = new("experimental.batch_operations", true, "Batch operations on plugins and documents");

    /// <summary>Returns all registered feature flags.</summary>
    public static IReadOnlyList<FeatureFlag> All { get; } = new[]
    {
        ConversationMemory, StreamingResponses, AutoTagging,
        HybridSearch, SearchCaching,
        KnowledgeGraph, DuplicateDetection, DigestReports,
        CollaborativeSync, AutoSync,
        PluginSystem,
        AudioTranscription, WebImport, WorkflowAutomation, BatchOperations
    };
}

/// <summary>
/// Represents a single feature flag with its name, default value, and description.
/// </summary>
public sealed record FeatureFlag(string Name, bool DefaultValue, string Description);
