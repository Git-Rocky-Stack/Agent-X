using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Tagging;

/// <summary>
/// Service interface for AI-powered automatic tagging and manual tag management.
/// Provides both AI-generated tag suggestions and CRUD operations for the tag system.
/// </summary>
public interface IAutoTagService
{
    /// <summary>
    /// Uses the AI service to generate descriptive tags for the given document content.
    /// </summary>
    /// <param name="documentContent">The text content to analyze for tag generation.</param>
    /// <param name="maxTags">Maximum number of tags to generate (default 5).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of tag names paired with confidence scores (0.0 to 1.0).</returns>
    Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(
        string documentContent,
        int maxTags = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Generates tags for a document and persists them as TagEntity/DocumentTagEntity records.
    /// Existing tags are matched by name (case-insensitive); new tags are created as auto-generated.
    /// Duplicate document-tag associations are skipped.
    /// </summary>
    /// <param name="documentId">The ID of the document to auto-tag.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all tags in the system, ordered by name.
    /// </summary>
    Task<IReadOnlyList<TagEntity>> GetAllTagsAsync();

    /// <summary>
    /// Creates a new tag with the given name and optional color.
    /// </summary>
    /// <param name="name">The tag name (must not be empty, must be unique).</param>
    /// <param name="colorHex">Optional hex color string for display (e.g. "#FF5733").</param>
    /// <returns>The newly created tag entity.</returns>
    Task<TagEntity> CreateTagAsync(string name, string? colorHex = null);

    /// <summary>
    /// Deletes a tag by ID. Cascade removes all document-tag associations.
    /// </summary>
    /// <param name="tagId">The ID of the tag to delete.</param>
    Task DeleteTagAsync(long tagId);

    /// <summary>
    /// Manually assigns a tag to a document with full confidence (1.0).
    /// </summary>
    /// <param name="documentId">The ID of the document.</param>
    /// <param name="tagId">The ID of the tag to assign.</param>
    Task AssignTagAsync(long documentId, long tagId);

    /// <summary>
    /// Removes a tag assignment from a document.
    /// </summary>
    /// <param name="documentId">The ID of the document.</param>
    /// <param name="tagId">The ID of the tag to remove.</param>
    Task RemoveTagAsync(long documentId, long tagId);

    /// <summary>
    /// Retrieves all tags currently assigned to a specific document.
    /// </summary>
    /// <param name="documentId">The ID of the document.</param>
    Task<IReadOnlyList<TagEntity>> GetTagsForDocumentAsync(long documentId);

    /// <summary>
    /// Retrieves assigned tags for multiple documents in one call, keyed by document ID.
    /// Used by list surfaces to avoid N+1 tag loading.
    /// </summary>
    Task<IReadOnlyDictionary<long, IReadOnlyList<TagEntity>>> GetTagsForDocumentsAsync(
        IReadOnlyList<long> documentIds);
}
