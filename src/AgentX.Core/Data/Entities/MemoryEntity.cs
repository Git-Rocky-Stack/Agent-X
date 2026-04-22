namespace AgentX.Core.Data.Entities;

/// <summary>
/// Stores an extracted memory/fact from a conversation with semantic search capabilities.
/// Memories persist across conversations and are injected into system prompts
/// to provide personalized context.
/// </summary>
public class MemoryEntity
{
    public long Id { get; set; }

    /// <summary>The extracted fact or preference (e.g., "User prefers Python over JavaScript")</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Expanded category for better memory organization.
    /// User-focused: user_preference, user_fact, user_topic, user_instruction
    /// Style-focused: interaction_style, communication_preference
    /// Domain-focused: domain_expertise, project_context
    /// Relationship-focused: relationship, goal, constraint
    /// Temporal-focused: episodic_event, learning, correction, affirmation
    /// </summary>
    public string Category { get; set; } = "fact";

    /// <summary>Source conversation ID where this was extracted</summary>
    public long? SourceConversationId { get; set; }

    /// <summary>Importance score (0.0-1.0). Base importance before temporal decay.</summary>
    public double Importance { get; set; } = 0.5;

    /// <summary>
    /// Temporal decay rate (0.0-1.0). Controls how fast the memory fades.
    /// 0.0 = no decay, 0.01 = slow decay, 0.1 = fast decay.
    /// Effective importance = Importance * exp(-DecayRate * DaysSinceLastAccess)
    /// </summary>
    public double DecayRate { get; set; } = 0.01;

    /// <summary>How many times this memory has been used in prompts</summary>
    public int UsageCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft delete - user can dismiss memories</summary>
    public bool IsActive { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════════
    //  Semantic Memory 2.0 additions
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Embedding vector for semantic similarity search.
    /// Stored as comma-separated float values (e.g., "0.1,-0.2,0.3,...").
    /// 384 dimensions for all-MiniLM-L6-v2.
    /// </summary>
    public string? Embedding { get; set; }

    /// <summary>
    /// Links this memory to a related memory for associative retrieval.
    /// Enables transitive memory access (memory → linked memory → its links).
    /// Created when two memories have semantic similarity > 0.85.
    /// </summary>
    public long? LinkedMemoryId { get; set; }

    /// <summary>Navigation property to the linked memory (if any).</summary>
    public MemoryEntity? LinkedMemory { get; set; }

    /// <summary>
    /// Confidence score from extraction (0.0-1.0).
    /// Higher values indicate more reliable/confident extractions.
    /// Used to weight importance during retrieval.
    /// </summary>
    public double Confidence { get; set; } = 0.8;

    /// <summary>
    /// Optional free-form tags for additional organization.
    /// Stored as comma-separated values (e.g., "technical,preference,csharp").
    /// </summary>
    public string? Tags { get; set; }
}
