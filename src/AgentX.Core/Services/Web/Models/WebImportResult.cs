using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Web.Models;

/// <summary>
/// Outcome of importing one URL of a batch. A batch returns exactly one result per requested
/// URL, in request order, so a document never has to be matched to its URL by position.
/// </summary>
public sealed record WebImportResult
{
    /// <summary>The URL this result belongs to, exactly as it was requested.</summary>
    public required string Url { get; init; }

    /// <summary>The created document, or null when the import failed.</summary>
    public DocumentEntity? Document { get; init; }

    /// <summary>Why the import failed; null when it succeeded.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when a document was created for <see cref="Url"/>.</summary>
    public bool Success => Document is not null;
}
