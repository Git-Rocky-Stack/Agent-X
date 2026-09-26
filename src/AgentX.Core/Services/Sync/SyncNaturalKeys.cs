using System.Globalization;

namespace AgentX.Core.Services.Sync;

/// <summary>
/// Builds and parses the installation-independent identities Collaborative Sync uses to
/// match an incoming change to a local row. Auto-increment ids are local to one database,
/// so two independent installations can both have an unrelated "document 5"; matching on
/// these keys instead keeps one machine's export from overwriting the other's records.
/// <para>
/// The schema has no stable sync id column, so every key is derived from existing data:
/// </para>
/// <list type="bullet">
///   <item>Document: <c>sha:</c> plus the content hash, or <c>path:</c> plus file path and name when no hash exists.</item>
///   <item>Collection: the names from the root collection down to this one.</item>
///   <item>Tag and system prompt: the name.</item>
///   <item>Conversation: the creation time (ticks) plus the title.</item>
///   <item>Annotation: the document key plus start offset, end offset and highlighted text.</item>
/// </list>
/// Components are joined with the ASCII unit separator, which never occurs in names or text
/// entered through the UI.
/// </summary>
internal static class SyncNaturalKeys
{
    /// <summary>Separator between key components (ASCII 0x1F, unit separator).</summary>
    internal const char Separator = '\u001F';

    private const string HashPrefix = "sha:";
    private const string PathPrefix = "path:";

    // ---- Documents ----

    public static string ForDocument(string? contentHash, string? filePath, string? fileName) =>
        !string.IsNullOrWhiteSpace(contentHash)
            ? HashPrefix + contentHash.Trim()
            : PathPrefix + (filePath ?? string.Empty) + Separator + (fileName ?? string.Empty);

    /// <summary>Splits a document key into its content hash, or its path and file name.</summary>
    public static bool TryParseDocument(string? key, out string? contentHash, out string? filePath, out string? fileName)
    {
        contentHash = null;
        filePath = null;
        fileName = null;

        if (string.IsNullOrEmpty(key))
            return false;

        if (key.StartsWith(HashPrefix, StringComparison.Ordinal))
        {
            contentHash = key[HashPrefix.Length..];
            return contentHash.Length > 0;
        }

        if (key.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            var rest = key[PathPrefix.Length..];
            var split = rest.LastIndexOf(Separator);
            if (split < 0)
                return false;

            filePath = rest[..split];
            fileName = rest[(split + 1)..];
            return true;
        }

        return false;
    }

    // ---- Collections ----

    /// <summary>Builds a collection key from the names on the path from the root collection.</summary>
    public static string ForCollectionPath(IEnumerable<string> namesFromRoot) =>
        string.Join(Separator, namesFromRoot);

    /// <summary>Returns the key of the parent collection, or null for a root collection.</summary>
    public static string? ParentCollectionKey(string collectionKey)
    {
        var split = collectionKey.LastIndexOf(Separator);
        return split < 0 ? null : collectionKey[..split];
    }

    /// <summary>Number of ancestors encoded in a collection key (0 for a root collection).</summary>
    public static int CollectionDepth(string? collectionKey) =>
        string.IsNullOrEmpty(collectionKey) ? 0 : collectionKey.Count(c => c == Separator);

    /// <summary>
    /// Computes the key of every collection in <paramref name="collections"/> by walking the
    /// parent chain. A parent cycle or a dangling parent id stops the walk at that point.
    /// </summary>
    public static Dictionary<long, string> CollectionKeysById(
        IReadOnlyCollection<(long Id, string Name, long? ParentId)> collections)
    {
        var byId = collections.ToDictionary(c => c.Id);
        var keys = new Dictionary<long, string>(collections.Count);

        foreach (var collection in collections)
        {
            var names = new List<string>();
            var visited = new HashSet<long>();
            (long Id, string Name, long? ParentId)? current = collection;

            while (current is { } node && visited.Add(node.Id))
            {
                names.Add(node.Name);
                current = node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
                    ? parent
                    : null;
            }

            names.Reverse();
            keys[collection.Id] = ForCollectionPath(names);
        }

        return keys;
    }

    // ---- Conversations ----

    public static string ForConversation(DateTime createdAt, string? title) =>
        createdAt.Ticks.ToString(CultureInfo.InvariantCulture) + Separator + (title ?? string.Empty);

    public static bool TryParseConversation(string? key, out DateTime createdAt, out string title)
    {
        createdAt = default;
        title = string.Empty;

        if (string.IsNullOrEmpty(key))
            return false;

        var split = key.IndexOf(Separator);
        var ticksPart = split < 0 ? key : key[..split];
        if (!long.TryParse(ticksPart, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        createdAt = new DateTime(ticks, DateTimeKind.Unspecified);
        title = split < 0 ? string.Empty : key[(split + 1)..];
        return true;
    }

    // ---- Annotations ----

    public static string ForAnnotation(string documentKey, int startOffset, int endOffset, string? highlightedText) =>
        string.Join(
            Separator,
            documentKey,
            startOffset.ToString(CultureInfo.InvariantCulture),
            endOffset.ToString(CultureInfo.InvariantCulture),
            highlightedText ?? string.Empty);

    public static bool TryParseAnnotation(
        string? key,
        out string documentKey,
        out int startOffset,
        out int endOffset,
        out string highlightedText)
    {
        documentKey = string.Empty;
        startOffset = 0;
        endOffset = 0;
        highlightedText = string.Empty;

        if (string.IsNullOrEmpty(key))
            return false;

        // The document key may itself contain a separator (path keys), so the text and the two
        // offsets are taken from the right and everything before them is the document key.
        var parts = key.Split(Separator);
        if (parts.Length < 4)
            return false;

        highlightedText = parts[^1];
        if (!int.TryParse(parts[^3], NumberStyles.Integer, CultureInfo.InvariantCulture, out startOffset)
            || !int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out endOffset))
        {
            return false;
        }

        documentKey = string.Join(Separator, parts[..^3]);
        return documentKey.Length > 0;
    }
}
