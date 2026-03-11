using AgentX.Core.Services.Collaboration.Models;

namespace AgentX.Core.Services.Collaboration;

/// <summary>
/// Provides lightweight real-time collaboration infrastructure for the Agent-X desktop app.
///
/// <para>
/// Architecture overview:
/// <list type="bullet">
///   <item><description>
///     One process on the LAN acts as <b>host</b> by calling <see cref="StartHostingAsync"/>.
///     The host runs an <see cref="System.Net.HttpListener"/> on a fixed port and is the
///     authoritative store for active sessions.
///   </description></item>
///   <item><description>
///     All other processes act as <b>peers</b>. They call <see cref="StartSessionAsync"/>,
///     which registers the local session with the host and starts a periodic heartbeat timer.
///   </description></item>
///   <item><description>
///     Events are broadcast via HTTP POST to all known peer endpoints.
///     The lightweight approach avoids pulling in SignalR or any additional runtime dependency.
///   </description></item>
/// </list>
/// </para>
/// </summary>
public interface ICollaborationService
{
    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when this process is acting as the collaboration host,
    /// i.e., <see cref="StartHostingAsync"/> has been called and the listener is running.
    /// </summary>
    bool IsHosting { get; }

    /// <summary>
    /// Fires whenever a <see cref="CollaborationEvent"/> is received from another participant
    /// (or broadcast locally). Subscribers should marshal to the UI thread as needed.
    /// </summary>
    event EventHandler<CollaborationEvent>? EventReceived;

    // ── Hosting ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the HTTP listener that acts as the collaboration hub for all peers on the LAN.
    /// Calling this when already hosting is a no-op.
    /// </summary>
    /// <param name="port">
    /// TCP port for the listener. Defaults to <c>9847</c> — chosen to avoid common conflicts.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task StartHostingAsync(int port = 9847, CancellationToken ct = default);

    /// <summary>
    /// Stops the HTTP listener and broadcasts a <see cref="CollaborationEventType.UserLeft"/>
    /// event to all connected peers before shutting down.
    /// </summary>
    Task StopHostingAsync(CancellationToken ct = default);

    // ── Session lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// Creates and registers a new session for the local process.
    /// Automatically starts a heartbeat timer that fires every 10 seconds.
    /// </summary>
    /// <param name="userName">
    /// Display name shown to other participants.
    /// Falls back to <see cref="Environment.UserName"/> when <c>null</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly allocated session ID (a <see cref="Guid"/> formatted as a string).</returns>
    Task<string> StartSessionAsync(string? userName = null, CancellationToken ct = default);

    /// <summary>
    /// Ends the local session, stops the heartbeat timer, and broadcasts a
    /// <see cref="CollaborationEventType.UserLeft"/> event.
    /// </summary>
    Task EndSessionAsync(CancellationToken ct = default);

    // ── Presence ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the local session's presence information so other participants can see
    /// where the user currently is within the application.
    /// </summary>
    /// <param name="activePage">Name of the UI page being viewed (e.g., <c>"Chat"</c>).</param>
    /// <param name="activeDocumentId">Primary key of the document currently open, if any.</param>
    /// <param name="activeConversationId">Primary key of the conversation currently open, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdatePresenceAsync(
        string? activePage = null,
        long? activeDocumentId = null,
        long? activeConversationId = null,
        CancellationToken ct = default);

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcasts a <see cref="CollaborationEvent"/> to all known peers.
    /// The event is also raised locally via <see cref="EventReceived"/>.
    /// </summary>
    /// <param name="evt">The event to broadcast.</param>
    /// <param name="ct">Cancellation token.</param>
    Task BroadcastEventAsync(CollaborationEvent evt, CancellationToken ct = default);

    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current connection status and snapshot of all active sessions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<CollaborationStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all sessions that have sent a heartbeat within the last 30 seconds.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CollaborationSession>> GetActiveSessionsAsync(CancellationToken ct = default);
}
