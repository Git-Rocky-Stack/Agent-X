using System.Text.Json;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Kind-safe readers for schema.org JSON-LD blocks. Real pages put almost anything in these
/// fields ("@type" as an array, "author" as null, an empty array, or an object whose "name" is
/// not a string), so every access checks the JSON value kind first. A value of an unexpected
/// kind is treated as absent instead of throwing and aborting the whole extraction.
/// </summary>
internal static class JsonLdReader
{
    /// <summary>
    /// Returns the value of <paramref name="propertyName"/> when <paramref name="element"/> is an
    /// object and the property is a string; otherwise null.
    /// </summary>
    public static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Returns the schema.org type of an object: the "@type" string, or the first string of a
    /// "@type" array (for example <c>["Article", "NewsArticle"]</c>).
    /// </summary>
    public static string? GetSchemaType(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("@type", out var type))
        {
            return null;
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return type.GetString();
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the author of a JSON-LD object, looking through a "@graph" array first (a common
    /// schema.org layout) and then at the object's own "author" property.
    /// </summary>
    public static string? FindAuthor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var graphItem in graph.EnumerateArray())
            {
                if (graphItem.ValueKind == JsonValueKind.Object
                    && graphItem.TryGetProperty("author", out var graphAuthor))
                {
                    var name = ResolveAuthorName(graphAuthor);
                    if (name is not null)
                    {
                        return name;
                    }
                }
            }
        }

        return element.TryGetProperty("author", out var author) ? ResolveAuthorName(author) : null;
    }

    /// <summary>
    /// Resolves an author value: a plain string, an object with a string "name", or an array of
    /// those (the first usable entry wins). Null, numbers, empty arrays, blank strings and
    /// nameless objects resolve to null so callers fall back to other author sources.
    /// </summary>
    public static string? ResolveAuthorName(JsonElement author)
    {
        switch (author.ValueKind)
        {
            case JsonValueKind.String:
                return NullIfBlank(author.GetString());

            case JsonValueKind.Object:
                return NullIfBlank(GetString(author, "name"));

            case JsonValueKind.Array:
                foreach (var item in author.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.String or JsonValueKind.Object)
                    {
                        var name = ResolveAuthorName(item);
                        if (name is not null)
                        {
                            return name;
                        }
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
