using AgentX.Core.Data.Entities;
using AgentX.Core.Documents.Models;

namespace AgentX.Core.Documents;

/// <summary>
/// Turns a file type filter into the <see cref="DocumentEntity.FileType"/> values it matches.
/// A document's file type is its extension without the dot ("cs", "png"), so a filter naming a
/// single type matches that type, and the vault's category chips match every extension their
/// processor reads: <see cref="Code"/> the extensions of the code processor
/// (<see cref="SupportedFileTypes.Code"/>), <see cref="Image"/> those of the image processor
/// (<see cref="SupportedFileTypes.Image"/>).
/// </summary>
public static class DocumentFileTypeFilter
{
    /// <summary>Filter value of the Code chip.</summary>
    public const string Code = "code";

    /// <summary>Filter value of the Images chip.</summary>
    public const string Image = "image";

    private static readonly IReadOnlyList<string> CodeFileTypes = ToFileTypes(SupportedFileTypes.Code);
    private static readonly IReadOnlyList<string> ImageFileTypes = ToFileTypes(SupportedFileTypes.Image);

    /// <summary>
    /// The lower-case file types <paramref name="filter"/> selects. A leading dot and letter case
    /// are ignored.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var normalized = filter.Trim().TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            Code => CodeFileTypes,
            Image => ImageFileTypes,
            _ => new[] { normalized },
        };
    }

    private static IReadOnlyList<string> ToFileTypes(IEnumerable<string> extensions) =>
        Array.AsReadOnly(extensions
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fileType => fileType, StringComparer.Ordinal)
            .ToArray());
}
