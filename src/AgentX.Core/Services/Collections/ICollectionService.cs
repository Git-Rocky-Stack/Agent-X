using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Collections;

/// <summary>
/// Service interface for managing document collections, including CRUD operations,
/// hierarchical organization, and document-collection associations.
/// </summary>
public interface ICollectionService
{
    /// <summary>
    /// Creates a new collection with the given name, optional description, and optional parent.
    /// </summary>
    /// <param name="name">The name of the collection (must not be empty).</param>
    /// <param name="description">Optional description of the collection.</param>
    /// <param name="parentId">Optional parent collection ID for nesting.</param>
    /// <returns>The newly created collection entity.</returns>
    Task<CollectionEntity> CreateCollectionAsync(string name, string? description = null, long? parentId = null);

    /// <summary>
    /// Retrieves all collections, ordered by sort order then name, with child collections included.
    /// </summary>
    Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync();

    /// <summary>
    /// Retrieves only root-level collections (those without a parent), with child collections included.
    /// </summary>
    Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync();

    /// <summary>
    /// Retrieves the immediate child collections of the specified parent collection.
    /// </summary>
    /// <param name="parentId">The ID of the parent collection.</param>
    Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId);

    /// <summary>
    /// Retrieves a single collection by ID, including its document associations and child collections.
    /// </summary>
    /// <param name="collectionId">The ID of the collection to retrieve.</param>
    /// <returns>The collection entity, or null if not found.</returns>
    Task<CollectionEntity?> GetCollectionAsync(long collectionId);

    /// <summary>
    /// Updates the name and description of an existing collection.
    /// </summary>
    /// <param name="collectionId">The ID of the collection to update.</param>
    /// <param name="name">The new name (must not be empty).</param>
    /// <param name="description">The new description (nullable).</param>
    Task UpdateCollectionAsync(long collectionId, string name, string? description = null);

    /// <summary>
    /// Deletes a collection. Children are re-parented to the deleted collection's parent.
    /// </summary>
    /// <param name="collectionId">The ID of the collection to delete.</param>
    /// <param name="deleteDocuments">
    /// If true, cascade-deletes all documents associated with this collection.
    /// If false, only the collection and its associations are removed; documents remain.
    /// </param>
    Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false);

    /// <summary>
    /// Associates a document with a collection.
    /// </summary>
    /// <param name="documentId">The ID of the document.</param>
    /// <param name="collectionId">The ID of the collection.</param>
    Task AddDocumentToCollectionAsync(long documentId, long collectionId);

    /// <summary>
    /// Removes the association between a document and a collection.
    /// </summary>
    /// <param name="documentId">The ID of the document.</param>
    /// <param name="collectionId">The ID of the collection.</param>
    Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId);

    /// <summary>
    /// Moves a collection to a new parent, or to root level if <paramref name="newParentId"/> is null.
    /// </summary>
    /// <param name="collectionId">The ID of the collection to move.</param>
    /// <param name="newParentId">The new parent collection ID, or null for root level.</param>
    Task MoveCollectionAsync(long collectionId, long? newParentId);

    /// <summary>
    /// Returns the total number of collections in the database.
    /// </summary>
    Task<int> GetCollectionCountAsync();

    /// <summary>
    /// Retrieves all documents belonging to a specific collection via the join table.
    /// </summary>
    /// <param name="collectionId">The ID of the collection.</param>
    Task<IReadOnlyList<DocumentEntity>> GetDocumentsInCollectionAsync(long collectionId);
}
