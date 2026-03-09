namespace AgentX.Core.Exceptions;

/// <summary>
/// Thrown when a database entity cannot be found by its identifier.
/// Carries the entity type name and the ID that was requested so callers
/// can produce precise diagnostics or user-facing messages.
/// </summary>
public class EntityNotFoundException : AgentXException
{
    private const string Code = "ENTITY_NOT_FOUND";

    /// <summary>
    /// The type name of the entity that was not found (e.g., "Document", "Collection").
    /// </summary>
    public string EntityType { get; }

    /// <summary>
    /// The primary-key identifier that was used to look up the entity.
    /// </summary>
    public long EntityId { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="EntityNotFoundException"/>.
    /// </summary>
    /// <param name="entityType">The type name of the missing entity.</param>
    /// <param name="entityId">The primary-key identifier that was requested.</param>
    public EntityNotFoundException(string entityType, long entityId)
        : base(
            message: $"Entity '{entityType}' with ID {entityId} was not found.",
            errorCode: Code,
            userFriendlyMessage: $"The requested {entityType} could not be found. It may have been deleted.",
            inner: null)
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
