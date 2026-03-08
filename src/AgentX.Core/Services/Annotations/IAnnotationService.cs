using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Annotations;

/// <summary>
/// Manages document annotations and highlights. Provides CRUD operations,
/// colour filtering, full-text search, aggregate statistics, and Markdown export
/// via EF Core against the AgentX SQLite database.
/// </summary>
public interface IAnnotationService
{
    /// <summary>
    /// Creates a new annotation (highlight) on the specified document.
    /// </summary>
    /// <param name="documentId">The document being annotated.</param>
    /// <param name="chunkId">
    /// Optional chunk the highlight falls within. Pass null when referencing
    /// the full document text rather than an indexed chunk.
    /// </param>
    /// <param name="startOffset">Zero-based character offset of the highlight start.</param>
    /// <param name="endOffset">Exclusive character offset of the highlight end.</param>
    /// <param name="highlightedText">The verbatim text that was selected.</param>
    /// <param name="color">
    /// Colour label: "yellow", "green", "blue", "red", or "purple".
    /// </param>
    /// <param name="noteText">Optional user note attached to the highlight.</param>
    /// <returns>The newly created annotation entity with its generated ID.</returns>
    Task<AnnotationEntity> CreateAnnotationAsync(
        long documentId,
        long? chunkId,
        int startOffset,
        int endOffset,
        string highlightedText,
        string color,
        string? noteText = null);

    /// <summary>
    /// Retrieves a single annotation by ID, including the parent document.
    /// Returns null if not found.
    /// </summary>
    /// <param name="annotationId">The ID of the annotation to retrieve.</param>
    Task<AnnotationEntity?> GetAnnotationAsync(long annotationId);

    /// <summary>
    /// Returns all annotations for the specified document, ordered by
    /// <see cref="AnnotationEntity.StartOffset"/> ascending.
    /// </summary>
    /// <param name="documentId">The document whose annotations to retrieve.</param>
    Task<IReadOnlyList<AnnotationEntity>> GetAnnotationsForDocumentAsync(long documentId);

    /// <summary>
    /// Returns all annotations that use the specified colour label, ordered by
    /// <see cref="AnnotationEntity.CreatedAt"/> descending.
    /// </summary>
    /// <param name="color">The colour label to filter by (e.g., "yellow").</param>
    Task<IReadOnlyList<AnnotationEntity>> GetAnnotationsByColorAsync(string color);

    /// <summary>
    /// Searches annotations whose <see cref="AnnotationEntity.HighlightedText"/>
    /// or <see cref="AnnotationEntity.NoteText"/> contains the query string.
    /// Results are ordered by <see cref="AnnotationEntity.CreatedAt"/> descending.
    /// </summary>
    /// <param name="query">The search term (case-insensitive LIKE match).</param>
    Task<IReadOnlyList<AnnotationEntity>> SearchAnnotationsAsync(string query);

    /// <summary>
    /// Returns a paged list of all annotations ordered by
    /// <see cref="AnnotationEntity.CreatedAt"/> descending.
    /// </summary>
    /// <param name="skip">Number of records to skip (for pagination).</param>
    /// <param name="take">Maximum number of records to return.</param>
    Task<IReadOnlyList<AnnotationEntity>> GetAllAnnotationsAsync(int skip = 0, int take = 50);

    /// <summary>
    /// Returns the total number of annotations, optionally scoped to a single document.
    /// </summary>
    /// <param name="documentId">
    /// When provided, counts only annotations for that document.
    /// When null, counts all annotations in the database.
    /// </param>
    Task<int> GetAnnotationCountAsync(long? documentId = null);

    /// <summary>
    /// Updates the note text and/or colour of an existing annotation.
    /// Only non-null arguments are applied; omitted arguments leave the field unchanged.
    /// </summary>
    /// <param name="annotationId">The annotation to update.</param>
    /// <param name="noteText">New note text, or null to leave unchanged.</param>
    /// <param name="color">New colour label, or null to leave unchanged.</param>
    /// <returns>The updated annotation entity.</returns>
    Task<AnnotationEntity> UpdateAnnotationAsync(
        long annotationId,
        string? noteText = null,
        string? color = null);

    /// <summary>
    /// Permanently deletes a single annotation by ID.
    /// No-ops silently if the annotation does not exist.
    /// </summary>
    /// <param name="annotationId">The ID of the annotation to delete.</param>
    Task DeleteAnnotationAsync(long annotationId);

    /// <summary>
    /// Permanently deletes all annotations belonging to the specified document.
    /// Typically called when a document is removed from the vault.
    /// </summary>
    /// <param name="documentId">The document whose annotations to delete.</param>
    Task DeleteAnnotationsForDocumentAsync(long documentId);

    /// <summary>
    /// Returns the most recently created annotations across all documents,
    /// ordered by <see cref="AnnotationEntity.CreatedAt"/> descending.
    /// </summary>
    /// <param name="count">Maximum number of annotations to return.</param>
    Task<IReadOnlyList<AnnotationEntity>> GetRecentAnnotationsAsync(int count = 20);

    /// <summary>
    /// Returns a dictionary mapping each colour label to the number of annotations
    /// that use it. Only colours with at least one annotation are included.
    /// </summary>
    Task<Dictionary<string, int>> GetColorDistributionAsync();

    /// <summary>
    /// Exports annotations as a formatted Markdown document. When
    /// <paramref name="documentId"/> is provided, only that document's annotations
    /// are exported; otherwise all annotations are exported grouped by document.
    /// </summary>
    /// <param name="documentId">
    /// Optional document scope. Null exports annotations for all documents.
    /// </param>
    /// <returns>A Markdown string ready for saving or copying to clipboard.</returns>
    Task<string> ExportAnnotationsAsMarkdownAsync(long? documentId = null);
}
