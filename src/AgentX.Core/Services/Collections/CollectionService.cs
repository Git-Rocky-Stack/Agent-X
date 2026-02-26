using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Collections;

/// <summary>
/// EF Core-backed implementation of <see cref="ICollectionService"/>.
/// Manages all collection CRUD operations, hierarchical organization,
/// and document-collection associations.
/// </summary>
public class CollectionService : ICollectionService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _log;

    public CollectionService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<CollectionService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CollectionEntity> CreateCollectionAsync(
        string name,
        string? description = null,
        long? parentId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Collection name must not be empty.", nameof(name));
            }

            // Validate parent exists if specified
            if (parentId.HasValue)
            {
                var parentExists = await _db.Collections.AnyAsync(c => c.Id == parentId.Value);
                if (!parentExists)
                {
                    throw new InvalidOperationException(
                        $"Parent collection {parentId.Value} not found.");
                }
            }

            // Determine sort order: place new collection at the end of its sibling group
            var maxSortOrder = await _db.Collections
                .Where(c => c.ParentCollectionId == parentId)
                .MaxAsync(c => (int?)c.SortOrder) ?? -1;

            var now = DateTime.UtcNow;

            var collection = new CollectionEntity
            {
                Name = name.Trim(),
                Description = description?.Trim(),
                ParentCollectionId = parentId,
                CreatedAt = now,
                UpdatedAt = now,
                DocumentCount = 0,
                SortOrder = maxSortOrder + 1,
            };

            _db.Collections.Add(collection);
            await _db.SaveChangesAsync();

            _log.Information(
                "Created collection {CollectionId} '{Name}' under parent {ParentId}",
                collection.Id, collection.Name, parentId?.ToString() ?? "root");

            return collection;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to create collection '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync()
    {
        try
        {
            var collections = await _db.Collections
                .Include(c => c.ChildCollections)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            _log.Debug("Retrieved {Count} collections", collections.Count);

            return collections;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get all collections");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync()
    {
        try
        {
            var collections = await _db.Collections
                .Where(c => c.ParentCollectionId == null)
                .Include(c => c.ChildCollections)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            _log.Debug("Retrieved {Count} root collections", collections.Count);

            return collections;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get root collections");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId)
    {
        try
        {
            var collections = await _db.Collections
                .Where(c => c.ParentCollectionId == parentId)
                .Include(c => c.ChildCollections)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} child collections for parent {ParentId}",
                collections.Count, parentId);

            return collections;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get child collections for parent {ParentId}", parentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<CollectionEntity?> GetCollectionAsync(long collectionId)
    {
        try
        {
            var collection = await _db.Collections
                .Include(c => c.DocumentCollections)
                .Include(c => c.ChildCollections)
                .FirstOrDefaultAsync(c => c.Id == collectionId);

            if (collection is null)
            {
                _log.Warning("Collection {CollectionId} not found", collectionId);
            }

            return collection;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get collection {CollectionId}", collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateCollectionAsync(
        long collectionId,
        string name,
        string? description = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Collection name must not be empty.", nameof(name));
            }

            var collection = await _db.Collections.FindAsync(collectionId);
            if (collection is null)
            {
                _log.Warning(
                    "Cannot update: collection {CollectionId} not found",
                    collectionId);
                throw new InvalidOperationException(
                    $"Collection {collectionId} not found.");
            }

            collection.Name = name.Trim();
            collection.Description = description?.Trim();
            collection.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.Information(
                "Updated collection {CollectionId} name to '{Name}'",
                collectionId, name);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update collection {CollectionId}", collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false)
    {
        try
        {
            var collection = await _db.Collections
                .Include(c => c.ChildCollections)
                .Include(c => c.DocumentCollections)
                .FirstOrDefaultAsync(c => c.Id == collectionId);

            if (collection is null)
            {
                _log.Warning(
                    "Cannot delete: collection {CollectionId} not found",
                    collectionId);
                return;
            }

            // Re-parent children to the deleted collection's parent (or root if null)
            var newParentId = collection.ParentCollectionId;
            foreach (var child in collection.ChildCollections)
            {
                child.ParentCollectionId = newParentId;
                child.UpdatedAt = DateTime.UtcNow;
            }

            if (deleteDocuments)
            {
                // Cascade delete all documents associated with this collection
                var documentIds = collection.DocumentCollections
                    .Select(dc => dc.DocumentId)
                    .ToList();

                if (documentIds.Count > 0)
                {
                    var documents = await _db.Documents
                        .Where(d => documentIds.Contains(d.Id))
                        .ToListAsync();

                    _db.Documents.RemoveRange(documents);

                    _log.Information(
                        "Cascade deleting {DocumentCount} documents from collection {CollectionId}",
                        documents.Count, collectionId);
                }
            }

            // Remove the collection itself (DocumentCollectionEntity entries are
            // cascade-deleted by the DB relationship configured in OnModelCreating)
            _db.Collections.Remove(collection);
            await _db.SaveChangesAsync();

            _log.Information(
                "Deleted collection {CollectionId} '{Name}' (deleteDocuments={DeleteDocuments}, re-parented {ChildCount} children)",
                collectionId, collection.Name, deleteDocuments, collection.ChildCollections.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete collection {CollectionId}", collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AddDocumentToCollectionAsync(long documentId, long collectionId)
    {
        try
        {
            // Validate both entities exist
            var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId);
            if (!documentExists)
            {
                throw new InvalidOperationException($"Document {documentId} not found.");
            }

            var collection = await _db.Collections.FindAsync(collectionId);
            if (collection is null)
            {
                throw new InvalidOperationException($"Collection {collectionId} not found.");
            }

            // Check for duplicate association
            var alreadyExists = await _db.DocumentCollections
                .AnyAsync(dc => dc.DocumentId == documentId && dc.CollectionId == collectionId);

            if (alreadyExists)
            {
                _log.Debug(
                    "Document {DocumentId} is already in collection {CollectionId}, skipping",
                    documentId, collectionId);
                return;
            }

            var association = new DocumentCollectionEntity
            {
                DocumentId = documentId,
                CollectionId = collectionId,
                AddedAt = DateTime.UtcNow,
            };

            _db.DocumentCollections.Add(association);

            // Update the denormalized document count
            collection.DocumentCount += 1;
            collection.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.Information(
                "Added document {DocumentId} to collection {CollectionId} (new count: {DocumentCount})",
                documentId, collectionId, collection.DocumentCount);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to add document {DocumentId} to collection {CollectionId}",
                documentId, collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId)
    {
        try
        {
            var association = await _db.DocumentCollections
                .FirstOrDefaultAsync(dc => dc.DocumentId == documentId && dc.CollectionId == collectionId);

            if (association is null)
            {
                _log.Warning(
                    "No association found between document {DocumentId} and collection {CollectionId}",
                    documentId, collectionId);
                return;
            }

            _db.DocumentCollections.Remove(association);

            // Update the denormalized document count
            var collection = await _db.Collections.FindAsync(collectionId);
            if (collection is not null)
            {
                collection.DocumentCount = Math.Max(0, collection.DocumentCount - 1);
                collection.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            _log.Information(
                "Removed document {DocumentId} from collection {CollectionId} (new count: {DocumentCount})",
                documentId, collectionId, collection?.DocumentCount ?? 0);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to remove document {DocumentId} from collection {CollectionId}",
                documentId, collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task MoveCollectionAsync(long collectionId, long? newParentId)
    {
        try
        {
            var collection = await _db.Collections.FindAsync(collectionId);
            if (collection is null)
            {
                _log.Warning(
                    "Cannot move: collection {CollectionId} not found",
                    collectionId);
                throw new InvalidOperationException(
                    $"Collection {collectionId} not found.");
            }

            // Prevent moving a collection into itself
            if (newParentId.HasValue && newParentId.Value == collectionId)
            {
                throw new InvalidOperationException(
                    "A collection cannot be moved into itself.");
            }

            // Prevent circular references by walking up the ancestor chain
            if (newParentId.HasValue)
            {
                var newParent = await _db.Collections.FindAsync(newParentId.Value);
                if (newParent is null)
                {
                    throw new InvalidOperationException(
                        $"Target parent collection {newParentId.Value} not found.");
                }

                // Walk ancestors to ensure collectionId is not an ancestor of newParentId
                var ancestorId = newParent.ParentCollectionId;
                while (ancestorId.HasValue)
                {
                    if (ancestorId.Value == collectionId)
                    {
                        throw new InvalidOperationException(
                            "Cannot move a collection into one of its own descendants (circular reference).");
                    }

                    var ancestor = await _db.Collections.FindAsync(ancestorId.Value);
                    ancestorId = ancestor?.ParentCollectionId;
                }
            }

            var previousParentId = collection.ParentCollectionId;
            collection.ParentCollectionId = newParentId;
            collection.UpdatedAt = DateTime.UtcNow;

            // Place at the end of the new sibling group
            var maxSortOrder = await _db.Collections
                .Where(c => c.ParentCollectionId == newParentId && c.Id != collectionId)
                .MaxAsync(c => (int?)c.SortOrder) ?? -1;

            collection.SortOrder = maxSortOrder + 1;

            await _db.SaveChangesAsync();

            _log.Information(
                "Moved collection {CollectionId} from parent {PreviousParent} to {NewParent}",
                collectionId,
                previousParentId?.ToString() ?? "root",
                newParentId?.ToString() ?? "root");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to move collection {CollectionId}", collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCollectionCountAsync()
    {
        try
        {
            return await _db.Collections.CountAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get collection count");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentEntity>> GetDocumentsInCollectionAsync(long collectionId)
    {
        try
        {
            var collectionExists = await _db.Collections.AnyAsync(c => c.Id == collectionId);
            if (!collectionExists)
            {
                _log.Warning(
                    "Cannot get documents: collection {CollectionId} not found",
                    collectionId);
                return Array.Empty<DocumentEntity>();
            }

            var documents = await _db.DocumentCollections
                .Where(dc => dc.CollectionId == collectionId)
                .Include(dc => dc.Document)
                .Select(dc => dc.Document)
                .OrderBy(d => d.FileName)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} documents from collection {CollectionId}",
                documents.Count, collectionId);

            return documents;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to get documents in collection {CollectionId}",
                collectionId);
            throw;
        }
    }
}
