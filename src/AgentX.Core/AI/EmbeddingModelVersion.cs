namespace AgentX.Core.AI;

/// <summary>
/// Represents the version of an embedding model with semantic versioning.
/// Format: "{ModelName}:{Version}" (e.g., "all-minilm:1.0", "nomic-embed-text:1.5").
/// </summary>
public sealed class EmbeddingModelVersion
{
    /// <summary>The model name (e.g., "all-minilm", "nomic-embed-text").</summary>
    public string ModelName { get; }

    /// <summary>The version string (e.g., "1.0", "1.5").</summary>
    public string Version { get; }

    /// <summary>The expected embedding dimensions for this model version.</summary>
    public int Dimensions { get; }

    /// <summary>
    /// The full version string in format "{ModelName}:{Version}".
    /// This is what gets stored in the database.
    /// </summary>
    public string FullVersion => $"{ModelName}:{Version}";

    private EmbeddingModelVersion(string modelName, string version, int dimensions)
    {
        ModelName = modelName;
        Version = version;
        Dimensions = dimensions;
    }

    /// <summary>
    /// Parses a full version string into an EmbeddingModelVersion instance.
    /// </summary>
    /// <param name="fullVersion">The full version string (e.g., "all-minilm:1.0").</param>
    /// <param name="defaultDimensions">Default dimensions if not specified in the version string.</param>
    /// <returns>Parsed EmbeddingModelVersion, or null if parsing fails.</returns>
    public static EmbeddingModelVersion? Parse(string? fullVersion, int defaultDimensions = 384)
    {
        if (string.IsNullOrWhiteSpace(fullVersion))
            return null;

        var parts = fullVersion.Split(':');
        if (parts.Length == 0)
            return null;

        var modelName = parts[0];
        var version = parts.Length > 1 ? parts[1] : "1.0";

        // Try to extract dimensions from version (e.g., "384d" or "dim384")
        var dimensions = defaultDimensions;
        if (parts.Length > 2 && int.TryParse(parts[2], out var parsedDims))
        {
            dimensions = parsedDims;
        }

        return new EmbeddingModelVersion(modelName, version, dimensions);
    }

    /// <summary>
    /// Creates an EmbeddingModelVersion from model name and dimensions.
    /// Uses default version "1.0".
    /// </summary>
    public static EmbeddingModelVersion FromModel(string modelName, int dimensions)
    {
        return new EmbeddingModelVersion(modelName, "1.0", dimensions);
    }

    /// <summary>
    /// Legacy marker for embeddings created before versioning was introduced.
    /// </summary>
    public static EmbeddingModelVersion Legacy(int dimensions)
    {
        return new EmbeddingModelVersion("legacy", "0.0", dimensions);
    }

    /// <summary>
    /// Checks if this version is legacy (before versioning was introduced).
    /// </summary>
    public bool IsLegacy => ModelName == "legacy";

    /// <summary>
    /// Determines if this model version is compatible with another.
    /// Models are compatible if they have the same name and dimensions.
    /// </summary>
    public bool IsCompatibleWith(EmbeddingModelVersion? other)
    {
        if (other is null)
            return false;

        return ModelName == other.ModelName && Dimensions == other.Dimensions;
    }

    public override string ToString() => FullVersion;

    public override bool Equals(object? obj)
    {
        return obj is EmbeddingModelVersion other && FullVersion == other.FullVersion;
    }

    public override int GetHashCode()
    {
        return FullVersion.GetHashCode();
    }
}
