namespace AgentX.Core.AI.Routing;

/// <summary>
/// Describes a category of AI task with routing preferences.
/// Used by the <see cref="ITaskTypeDetector"/> and <see cref="IModelRouterService"/>
/// to select the best provider/model for a given prompt.
/// </summary>
public sealed class TaskType
{
    /// <summary>
    /// The canonical name of this task type (e.g. "extraction", "analysis").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether this task type prefers a local (on-device) provider to minimize cost and latency.
    /// </summary>
    public bool PreferLocal { get; }

    /// <summary>
    /// Whether this task type prioritizes speed over quality (e.g. simple extraction or chat).
    /// </summary>
    public bool PreferSpeed { get; }

    /// <summary>
    /// Whether this task type prioritizes quality over speed (e.g. analysis, code generation).
    /// </summary>
    public bool PreferQuality { get; }

    private TaskType(string name, bool preferLocal, bool preferSpeed, bool preferQuality)
    {
        Name = name;
        PreferLocal = preferLocal;
        PreferSpeed = preferSpeed;
        PreferQuality = preferQuality;
    }

    // ── Predefined Task Types ──────────────────────────────────────

    /// <summary>Extraction tasks: structured data extraction, entity recognition. Local + fast.</summary>
    public static TaskType Extraction { get; } = new("extraction", true, true, false);

    /// <summary>Summarization tasks: condensing content. Local + fast.</summary>
    public static TaskType Summarization { get; } = new("summarization", true, true, false);

    /// <summary>Analysis tasks: deep reasoning, comparison, evaluation. Quality-first.</summary>
    public static TaskType Analysis { get; } = new("analysis", false, false, true);

    /// <summary>Generation tasks: long-form content creation. Quality-first.</summary>
    public static TaskType Generation { get; } = new("generation", false, false, true);

    /// <summary>Code tasks: code generation, debugging, refactoring. Quality-first.</summary>
    public static TaskType Code { get; } = new("code", false, false, true);

    /// <summary>Creative tasks: creative writing, brainstorming. Quality-first.</summary>
    public static TaskType Creative { get; } = new("creative", false, false, true);

    /// <summary>Chat tasks: general conversation. Local + fast.</summary>
    public static TaskType Chat { get; } = new("chat", true, true, false);

    /// <summary>Embedding tasks: vector embedding generation. Local + fast.</summary>
    public static TaskType Embedding { get; } = new("embedding", true, true, false);

    // ── Factory Method ─────────────────────────────────────────────

    private static readonly Dictionary<string, TaskType> _predefined = new(StringComparer.OrdinalIgnoreCase)
    {
        [Extraction.Name] = Extraction,
        [Summarization.Name] = Summarization,
        [Analysis.Name] = Analysis,
        [Generation.Name] = Generation,
        [Code.Name] = Code,
        [Creative.Name] = Creative,
        [Chat.Name] = Chat,
        [Embedding.Name] = Embedding,
    };

    /// <summary>
    /// Resolves a task type by its canonical name. Returns <see cref="Chat"/> for unknown names.
    /// </summary>
    /// <param name="name">The task type name (case-insensitive).</param>
    /// <returns>The matching <see cref="TaskType"/>, or <see cref="Chat"/> as fallback.</returns>
    public static TaskType FromString(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Chat;

        return _predefined.TryGetValue(name, out var taskType) ? taskType : Chat;
    }

    /// <summary>
    /// All predefined task type names, useful for UI population.
    /// </summary>
    public static IReadOnlyList<string> AllNames => _predefined.Keys.ToList().AsReadOnly();

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TaskType other && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override int GetHashCode() => Name.ToLowerInvariant().GetHashCode();
}

/// <summary>
/// Represents the outcome of a routing decision: which provider and model
/// to use for a given prompt, along with the reasoning and metadata.
/// </summary>
public sealed class RoutingDecision
{
    /// <summary>
    /// The provider identifier selected by the router (e.g. "ollama", "openai").
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// The model identifier selected by the router (e.g. "llama3.2", "gpt-4o-mini").
    /// </summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// The detected (or overridden) task type that influenced this routing decision.
    /// </summary>
    public TaskType TaskType { get; init; } = TaskType.Chat;

    /// <summary>
    /// The routing profile used to make this decision.
    /// </summary>
    public RoutingProfile Profile { get; init; } = RoutingProfile.Balanced;

    /// <summary>
    /// Human-readable explanation of why this routing decision was made.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when this routing decision was made.
    /// </summary>
    public DateTimeOffset DecidedAt { get; init; } = DateTimeOffset.UtcNow;
}