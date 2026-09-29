namespace AgentX.Core.AI;

/// <summary>The provider and model that produce embeddings.</summary>
/// <param name="ProviderId">Provider id ("local", "ollama" or "openai"; never "anthropic").</param>
/// <param name="ModelName">Model passed to the provider's embedding call.</param>
public sealed record EmbeddingTarget(string ProviderId, string ModelName);

/// <summary>
/// Chooses the embedding provider independently of the chat provider, so switching the chat
/// model (or selecting a provider without an embedding API) never changes the embedding space
/// of the index or sends documents to a service the user did not choose for embeddings.
/// <list type="number">
/// <item>An OpenAI embedding model id in the Embedding Model setting (text-embedding-*) is the
/// only way document text is embedded in the cloud.</item>
/// <item>A GGUF file name selects the built-in provider explicitly.</item>
/// <item>Any other non-default model name is an explicit Ollama choice and is honored.</item>
/// <item>Otherwise (the default setting) the built-in model is used when its file is
/// installed, and Ollama with the default model when it is not.</item>
/// </list>
/// Anthropic has no embedding API and is never selected.
/// </summary>
public static class EmbeddingTargetResolver
{
    /// <summary>The Embedding Model setting a fresh install starts with (an Ollama model).</summary>
    public const string DefaultEmbeddingModelSetting = "all-minilm";

    /// <summary>Resolves the embedding target from the settings and the built-in model state.</summary>
    /// <param name="embeddingModelSetting">The saved Embedding Model setting.</param>
    /// <param name="localModelFileName">The built-in provider's configured model file.</param>
    /// <param name="localModelInstalled">Whether that file is present on disk.</param>
    public static EmbeddingTarget Resolve(
        string? embeddingModelSetting,
        string? localModelFileName,
        bool localModelInstalled)
    {
        var configured = embeddingModelSetting?.Trim() ?? string.Empty;

        if (IsOpenAiEmbeddingModel(configured))
            return new EmbeddingTarget("openai", configured);

        if (configured.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return new EmbeddingTarget("local", configured);

        if (configured.Length > 0 &&
            !string.Equals(configured, DefaultEmbeddingModelSetting, StringComparison.OrdinalIgnoreCase))
        {
            return new EmbeddingTarget("ollama", configured);
        }

        if (localModelInstalled && !string.IsNullOrWhiteSpace(localModelFileName))
            return new EmbeddingTarget("local", localModelFileName.Trim());

        return new EmbeddingTarget("ollama", configured.Length > 0 ? configured : DefaultEmbeddingModelSetting);
    }

    /// <summary>True for OpenAI embedding model ids (text-embedding-3-small, -3-large, -ada-002).</summary>
    public static bool IsOpenAiEmbeddingModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        model.Trim().StartsWith("text-embedding-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Output size of well-known embedding models, used until the first embedding reports the
    /// real size. Returns null for models whose size is not known in advance.
    /// </summary>
    public static int? KnownDimensions(EmbeddingTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var model = target.ModelName.Trim().ToLowerInvariant();
        foreach (var (prefix, dimensions) in KnownModelDimensions)
        {
            if (model.StartsWith(prefix, StringComparison.Ordinal))
                return dimensions;
        }

        return null;
    }

    private static readonly (string Prefix, int Dimensions)[] KnownModelDimensions =
    [
        ("all-minilm", 384),
        ("nomic-embed-text", 768),
        ("mxbai-embed-large", 1024),
        ("bge-m3", 1024),
        ("text-embedding-3-small", 1536),
        ("text-embedding-3-large", 3072),
        ("text-embedding-ada-002", 1536),
        ("llama-3.2-3b", 3072),
        ("llama-3.2-1b", 2048),
    ];
}
