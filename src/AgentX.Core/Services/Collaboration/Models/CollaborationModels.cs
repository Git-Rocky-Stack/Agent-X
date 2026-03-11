namespace AgentX.Core.Services.Collaboration.Models;

/// <summary>
/// Represents a single active user session in the collaborative workspace.
/// One session exists per running Agent-X process that has called
/// <see cref="ICollaborationService.StartSessionAsync"/>.
/// </summary>
public sealed class CollaborationSession
{
    /// <summary>Unique identifier for this session, generated as a <see cref="Guid"/> string.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable display name for the user.
    /// Defaults to the OS user name when not explicitly provided.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>NetBIOS machine name used to help disambiguate sessions from the same user.</summary>
    public string MachineName { get; init; } = string.Empty;

    /// <summary>UTC time when the session was created.</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// UTC time of the most recent heartbeat received from this session.
    /// Sessions whose heartbeat is older than 30 seconds are pruned automatically.
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// Identifier of the UI page the user is currently viewing
    /// (e.g., <c>"Chat"</c>, <c>"Documents"</c>, <c>"Settings"</c>).
    /// <c>null</c> when the page is not known.
    /// </summary>
    public string? ActivePage { get; set; }

    /// <summary>
    /// Primary key of the <c>DocumentEntity</c> the user currently has open.
    /// <c>null</c> when no document is active.
    /// </summary>
    public long? ActiveDocumentId { get; set; }

    /// <summary>
    /// Primary key of the <c>ConversationEntity</c> the user is currently in.
    /// <c>null</c> when no conversation is active.
    /// </summary>
    public long? ActiveConversationId { get; set; }
}

/// <summary>
/// Discrete event published by one collaboration participant and broadcast to all peers.
/// </summary>
public sealed class CollaborationEvent
{
    /// <summary>The kind of activity this event represents.</summary>
    public CollaborationEventType EventType { get; init; }

    /// <summary>Session ID of the participant that raised the event.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>UTC time the event was raised.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Optional JSON payload carrying event-specific data.
    /// For example, <see cref="CollaborationEventType.EditStarted"/> may include
    /// <c>{ "documentId": 42, "fieldName": "Title" }</c>.
    /// </summary>
    public string? Payload { get; init; }
}

/// <summary>
/// Categorises the kinds of activities that can be broadcast between collaborators.
/// </summary>
public enum CollaborationEventType
{
    /// <summary>A new participant has joined the session.</summary>
    UserJoined = 0,

    /// <summary>A participant has left or their session has expired.</summary>
    UserLeft = 1,

    /// <summary>A participant has begun editing a shared resource.</summary>
    EditStarted = 2,

    /// <summary>A participant has finished editing a shared resource.</summary>
    EditCompleted = 3,

    /// <summary>A resource has been locked for exclusive editing by one participant.</summary>
    DocumentLocked = 4,

    /// <summary>A previously locked resource has been released.</summary>
    DocumentUnlocked = 5,
}

/// <summary>
/// Point-in-time snapshot of the collaboration layer's connectivity and active membership.
/// Returned by <see cref="ICollaborationService.GetStatusAsync"/>.
/// </summary>
public sealed class CollaborationStatus
{
    /// <summary>
    /// <c>true</c> when the local process has an active session registered
    /// (either as host or as a connected peer).
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>All sessions that have sent a heartbeat within the last 30 seconds.</summary>
    public List<CollaborationSession> ActiveUsers { get; init; } = [];

    /// <summary>Session ID of the local process's own session.</summary>
    public string CurrentSessionId { get; init; } = string.Empty;
}
