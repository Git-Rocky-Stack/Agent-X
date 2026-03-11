using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AgentX.Core.Services.Collaboration.Models;
using Serilog;

namespace AgentX.Core.Services.Collaboration;

/// <summary>
/// Lightweight real-time collaboration service using <see cref="HttpListener"/> as the hub
/// and <see cref="HttpClient"/> for peer-to-peer event delivery.
///
/// <para>
/// No external runtime libraries are required. The protocol is a minimal JSON-over-HTTP
/// REST API exposed on a user-configurable local port (default 9847).
/// </para>
///
/// <para>
/// <b>Endpoints exposed when hosting:</b>
/// <list type="table">
///   <item>
///     <term>POST /api/session</term>
///     <description>Register or refresh a peer session. Body: <see cref="CollaborationSession"/> JSON.</description>
///   </item>
///   <item>
///     <term>POST /api/heartbeat</term>
///     <description>Keep a session alive. Body: <c>{ "sessionId": "..." }</c>.</description>
///   </item>
///   <item>
///     <term>POST /api/events</term>
///     <description>Receive an event broadcast from a peer. Body: <see cref="CollaborationEvent"/> JSON.</description>
///   </item>
///   <item>
///     <term>GET  /api/sessions</term>
///     <description>Return the list of currently active sessions as a JSON array.</description>
///   </item>
/// </list>
/// </para>
/// </summary>
public sealed class CollaborationService : ICollaborationService, IDisposable
{
    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>Sessions that have not sent a heartbeat in this window are considered gone.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often the local process sends its heartbeat to the host.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    /// <summary>Timeout applied to outbound HTTP calls to peers.</summary>
    private static readonly TimeSpan PeerRequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // ── Infrastructure ───────────────────────────────────────────────────────

    private readonly ILogger _log;
    private readonly HttpClient _http;

    // Active session registry (shared between host and peer paths).
    private readonly ConcurrentDictionary<string, CollaborationSession> _sessions = new();

    // Known peer base URLs for event fan-out (e.g., "http://192.168.1.5:9847").
    private readonly ConcurrentDictionary<string, byte> _peerBaseUrls = new(StringComparer.OrdinalIgnoreCase);

    // ── Host-side state ──────────────────────────────────────────────────────

    private HttpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private Timer? _pruneTimer; // Removes stale sessions on the host.

    // ── Local-session state ──────────────────────────────────────────────────

    private string? _localSessionId;
    private string? _hostBaseUrl;   // Base URL of the host this peer is connected to.
    private Timer? _heartbeatTimer;

    // ── Public surface ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool IsHosting => _listener?.IsListening == true;

    /// <inheritdoc />
    public event EventHandler<CollaborationEvent>? EventReceived;

    /// <summary>
    /// Initialises the service. The <paramref name="logger"/> is enriched with the
    /// service type context automatically.
    /// </summary>
    public CollaborationService(ILogger logger)
    {
        _log = logger?.ForContext<CollaborationService>()
               ?? throw new ArgumentNullException(nameof(logger));

        _http = new HttpClient { Timeout = PeerRequestTimeout };
    }

    // ── ICollaborationService — Hosting ──────────────────────────────────────

    /// <inheritdoc />
    public Task StartHostingAsync(int port = 9847, CancellationToken ct = default)
    {
        if (IsHosting)
        {
            _log.Debug("StartHostingAsync: already hosting — no-op");
            return Task.CompletedTask;
        }

        try
        {
            _listenerCts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Start();

            _log.Information("Collaboration host started on port {Port}", port);

            // Prune stale sessions every 10 seconds.
            _pruneTimer = new Timer(
                PruneExpiredSessions,
                state: null,
                dueTime: HeartbeatInterval,
                period: HeartbeatInterval);

            // Process incoming requests on a background task.
            _listenerTask = Task.Run(
                () => RunListenerLoopAsync(_listenerCts.Token),
                _listenerCts.Token);

            // Register the local process as a session on its own host.
            _hostBaseUrl = $"http://localhost:{port}";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to start collaboration host on port {Port}", port);

            // Clean up partially initialised state.
            _listener?.Stop();
            _listener = null;
            _listenerCts?.Dispose();
            _listenerCts = null;

            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopHostingAsync(CancellationToken ct = default)
    {
        if (!IsHosting)
        {
            _log.Debug("StopHostingAsync: not currently hosting — no-op");
            return;
        }

        _log.Information("Stopping collaboration host");

        // Broadcast departure before tearing down.
        if (_localSessionId is not null)
        {
            await BroadcastEventAsync(new CollaborationEvent
            {
                EventType = CollaborationEventType.UserLeft,
                UserId = _localSessionId,
                Timestamp = DateTime.UtcNow,
            }, ct).ConfigureAwait(false);
        }

        try
        {
            _listenerCts?.Cancel();
            _listener?.Stop();

            if (_listenerTask is not null)
            {
                await _listenerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Exception while stopping collaboration host listener");
        }
        finally
        {
            _pruneTimer?.Dispose();
            _pruneTimer = null;
            _listenerCts?.Dispose();
            _listenerCts = null;
            _listener = null;
            _listenerTask = null;
        }

        _log.Information("Collaboration host stopped");
    }

    // ── ICollaborationService — Session lifecycle ─────────────────────────────

    /// <inheritdoc />
    public async Task<string> StartSessionAsync(string? userName = null, CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;

        var session = new CollaborationSession
        {
            SessionId = sessionId,
            UserName = userName ?? Environment.UserName,
            MachineName = Environment.MachineName,
            StartedAt = now,
            LastHeartbeat = now,
        };

        // Register locally.
        _sessions[sessionId] = session;
        _localSessionId = sessionId;

        _log.Information(
            "Started collaboration session {SessionId} for user '{UserName}' on {MachineName}",
            sessionId, session.UserName, session.MachineName);

        // If connected to a host, register there too.
        if (_hostBaseUrl is not null)
        {
            await PostJsonAsync(
                $"{_hostBaseUrl}/api/session",
                session,
                ct).ConfigureAwait(false);
        }

        // Start the heartbeat timer.
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(
            SendHeartbeat,
            state: null,
            dueTime: HeartbeatInterval,
            period: HeartbeatInterval);

        // Announce arrival.
        await BroadcastEventAsync(new CollaborationEvent
        {
            EventType = CollaborationEventType.UserJoined,
            UserId = sessionId,
            Timestamp = now,
        }, ct).ConfigureAwait(false);

        return sessionId;
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(CancellationToken ct = default)
    {
        if (_localSessionId is null)
        {
            _log.Debug("EndSessionAsync: no active session — no-op");
            return;
        }

        var sessionId = _localSessionId;

        // Stop heartbeat first to prevent a race with disposal.
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        // Announce departure.
        try
        {
            await BroadcastEventAsync(new CollaborationEvent
            {
                EventType = CollaborationEventType.UserLeft,
                UserId = sessionId,
                Timestamp = DateTime.UtcNow,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to broadcast UserLeft event for session {SessionId}", sessionId);
        }

        // Remove from local and remote registries.
        _sessions.TryRemove(sessionId, out _);
        _localSessionId = null;

        _log.Information("Ended collaboration session {SessionId}", sessionId);
    }

    // ── ICollaborationService — Presence ──────────────────────────────────────

    /// <inheritdoc />
    public async Task UpdatePresenceAsync(
        string? activePage = null,
        long? activeDocumentId = null,
        long? activeConversationId = null,
        CancellationToken ct = default)
    {
        if (_localSessionId is null)
        {
            _log.Debug("UpdatePresenceAsync: no active session — skipping");
            return;
        }

        if (!_sessions.TryGetValue(_localSessionId, out var session))
        {
            _log.Warning("UpdatePresenceAsync: local session {SessionId} not found in registry", _localSessionId);
            return;
        }

        session.ActivePage = activePage;
        session.ActiveDocumentId = activeDocumentId;
        session.ActiveConversationId = activeConversationId;
        session.LastHeartbeat = DateTime.UtcNow;

        _log.Debug(
            "Updated presence for session {SessionId}: page={Page}, doc={DocId}, conv={ConvId}",
            _localSessionId, activePage, activeDocumentId, activeConversationId);

        // Propagate the updated session record to the host.
        if (_hostBaseUrl is not null)
        {
            await PostJsonAsync(
                $"{_hostBaseUrl}/api/session",
                session,
                ct).ConfigureAwait(false);
        }
    }

    // ── ICollaborationService — Events ────────────────────────────────────────

    /// <inheritdoc />
    public async Task BroadcastEventAsync(CollaborationEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // Raise locally first so the UI responds even if peers are unreachable.
        RaiseEventReceived(evt);

        // Fan out to all known peers in parallel; failures are logged and swallowed so one
        // unreachable peer does not prevent delivery to others.
        if (_peerBaseUrls.IsEmpty)
            return;

        var tasks = _peerBaseUrls.Keys
            .Select(peerUrl => PostEventToPeerAsync(peerUrl, evt, ct))
            .ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // ── ICollaborationService — Queries ───────────────────────────────────────

    /// <inheritdoc />
    public Task<CollaborationStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var sessions = GetLiveSessions();

        var status = new CollaborationStatus
        {
            IsConnected = _localSessionId is not null,
            ActiveUsers = sessions,
            CurrentSessionId = _localSessionId ?? string.Empty,
        };

        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CollaborationSession>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<CollaborationSession> sessions = GetLiveSessions();
        return Task.FromResult(sessions);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _pruneTimer?.Dispose();
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();

        try { _listener?.Stop(); } catch { /* best effort */ }

        _http.Dispose();
    }

    // ── Private — HTTP listener loop ──────────────────────────────────────────

    /// <summary>
    /// Processes incoming HTTP requests until <paramref name="ct"/> is cancelled
    /// or the listener is stopped.
    /// </summary>
    private async Task RunListenerLoopAsync(CancellationToken ct)
    {
        _log.Debug("Collaboration listener loop started");

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener!.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException ex) when (ct.IsCancellationRequested || !IsHosting)
            {
                _log.Debug("Listener closed: {Message}", ex.Message);
                break;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Unexpected error accepting HTTP connection — continuing");
                continue;
            }

            // Handle each request on a separate task to keep the loop responsive.
            _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
        }

        _log.Debug("Collaboration listener loop exited");
    }

    /// <summary>
    /// Dispatches a single incoming HTTP request to the appropriate handler.
    /// </summary>
    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var req = context.Request;
        var res = context.Response;

        try
        {
            var path = req.Url?.AbsolutePath?.TrimEnd('/').ToLowerInvariant() ?? string.Empty;
            var method = req.HttpMethod.ToUpperInvariant();

            _log.Debug("Collaboration request: {Method} {Path}", method, path);

            switch ((method, path))
            {
                case ("POST", "/api/session"):
                    await HandleRegisterSessionAsync(req, res, ct).ConfigureAwait(false);
                    break;

                case ("POST", "/api/heartbeat"):
                    await HandleHeartbeatAsync(req, res, ct).ConfigureAwait(false);
                    break;

                case ("POST", "/api/events"):
                    await HandleEventAsync(req, res, ct).ConfigureAwait(false);
                    break;

                case ("GET", "/api/sessions"):
                    await HandleGetSessionsAsync(res, ct).ConfigureAwait(false);
                    break;

                default:
                    res.StatusCode = (int)HttpStatusCode.NotFound;
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error handling collaboration request {Method} {Path}",
                req.HttpMethod, req.Url?.AbsolutePath);
            try { res.StatusCode = (int)HttpStatusCode.InternalServerError; } catch { /* ignore */ }
        }
        finally
        {
            try { res.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// POST /api/session — Registers or refreshes a peer session on the host.
    /// Also records the caller's IP so we can fan events back to it.
    /// </summary>
    private async Task HandleRegisterSessionAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        var session = await DeserializeBodyAsync<CollaborationSession>(req, ct).ConfigureAwait(false);
        if (session is null)
        {
            res.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        session.LastHeartbeat = DateTime.UtcNow;
        _sessions[session.SessionId] = session;

        // Record the peer's origin URL for event fan-out.
        // We derive the port from the request — peers POST their own port in the payload
        // when they are also hosting; otherwise we just use the remote address.
        var remoteIp = req.RemoteEndPoint?.Address?.ToString();
        if (!string.IsNullOrEmpty(remoteIp) && remoteIp != "127.0.0.1" && remoteIp != "::1")
        {
            // Default peer port — a proper implementation would include it in the payload.
            var peerUrl = $"http://{remoteIp}:9847";
            _peerBaseUrls.TryAdd(peerUrl, 0);
        }

        _log.Information(
            "Registered session {SessionId} for '{UserName}' on {MachineName}",
            session.SessionId, session.UserName, session.MachineName);

        res.StatusCode = (int)HttpStatusCode.OK;
        await WriteJsonResponseAsync(res, new { ok = true }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST /api/heartbeat — Refreshes the <see cref="CollaborationSession.LastHeartbeat"/>
    /// timestamp for an existing session.
    /// </summary>
    private async Task HandleHeartbeatAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        var body = await DeserializeBodyAsync<HeartbeatPayload>(req, ct).ConfigureAwait(false);
        if (body is null || string.IsNullOrWhiteSpace(body.SessionId))
        {
            res.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        if (_sessions.TryGetValue(body.SessionId, out var session))
        {
            session.LastHeartbeat = DateTime.UtcNow;
            _log.Debug("Heartbeat received for session {SessionId}", body.SessionId);
            res.StatusCode = (int)HttpStatusCode.OK;
            await WriteJsonResponseAsync(res, new { ok = true }, ct).ConfigureAwait(false);
        }
        else
        {
            _log.Warning("Heartbeat for unknown session {SessionId}", body.SessionId);
            res.StatusCode = (int)HttpStatusCode.NotFound;
        }
    }

    /// <summary>
    /// POST /api/events — Receives a <see cref="CollaborationEvent"/> from a peer and
    /// raises it locally via <see cref="EventReceived"/>.
    /// </summary>
    private async Task HandleEventAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        var evt = await DeserializeBodyAsync<CollaborationEvent>(req, ct).ConfigureAwait(false);
        if (evt is null)
        {
            res.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        _log.Debug(
            "Received collaboration event {EventType} from {UserId}",
            evt.EventType, evt.UserId);

        // Surface the event to all local subscribers.
        RaiseEventReceived(evt);

        res.StatusCode = (int)HttpStatusCode.OK;
        await WriteJsonResponseAsync(res, new { ok = true }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GET /api/sessions — Returns the list of all currently known sessions as JSON.
    /// </summary>
    private async Task HandleGetSessionsAsync(HttpListenerResponse res, CancellationToken ct)
    {
        var sessions = GetLiveSessions();

        res.StatusCode = (int)HttpStatusCode.OK;
        await WriteJsonResponseAsync(res, sessions, ct).ConfigureAwait(false);
    }

    // ── Private — Timers ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="_heartbeatTimer"/> every <see cref="HeartbeatInterval"/>.
    /// Posts a heartbeat to the host so the local session is not pruned.
    /// </summary>
    private void SendHeartbeat(object? state)
    {
        if (_localSessionId is null || _hostBaseUrl is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await PostJsonAsync(
                    $"{_hostBaseUrl}/api/heartbeat",
                    new HeartbeatPayload { SessionId = _localSessionId },
                    CancellationToken.None).ConfigureAwait(false);

                _log.Debug("Heartbeat sent for session {SessionId}", _localSessionId);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Heartbeat failed for session {SessionId}", _localSessionId);
            }
        });
    }

    /// <summary>
    /// Called by <see cref="_pruneTimer"/> every <see cref="HeartbeatInterval"/>.
    /// Removes sessions whose <see cref="CollaborationSession.LastHeartbeat"/> is older than
    /// <see cref="SessionTimeout"/> and broadcasts a <see cref="CollaborationEventType.UserLeft"/>
    /// event for each evicted session.
    /// </summary>
    private void PruneExpiredSessions(object? state)
    {
        var cutoff = DateTime.UtcNow - SessionTimeout;
        var expired = _sessions.Values
            .Where(s => s.LastHeartbeat < cutoff)
            .ToList();

        foreach (var session in expired)
        {
            if (_sessions.TryRemove(session.SessionId, out _))
            {
                _log.Information(
                    "Pruned expired session {SessionId} ('{UserName}', last heartbeat {LastHeartbeat:u})",
                    session.SessionId, session.UserName, session.LastHeartbeat);

                // Fire-and-forget: broadcast the departure event asynchronously.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await BroadcastEventAsync(new CollaborationEvent
                        {
                            EventType = CollaborationEventType.UserLeft,
                            UserId = session.SessionId,
                            Timestamp = DateTime.UtcNow,
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex,
                            "Failed to broadcast UserLeft for pruned session {SessionId}",
                            session.SessionId);
                    }
                });
            }
        }
    }

    // ── Private — Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns all sessions that have sent a heartbeat within <see cref="SessionTimeout"/>.
    /// </summary>
    private List<CollaborationSession> GetLiveSessions()
    {
        var cutoff = DateTime.UtcNow - SessionTimeout;
        return _sessions.Values
            .Where(s => s.LastHeartbeat >= cutoff)
            .OrderBy(s => s.StartedAt)
            .ToList();
    }

    /// <summary>
    /// Raises <see cref="EventReceived"/> on the current synchronisation context.
    /// All exceptions thrown by subscribers are caught and logged.
    /// </summary>
    private void RaiseEventReceived(CollaborationEvent evt)
    {
        try
        {
            EventReceived?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Exception in EventReceived subscriber for event {EventType}", evt.EventType);
        }
    }

    /// <summary>
    /// Posts a <see cref="CollaborationEvent"/> to a specific peer's /api/events endpoint.
    /// All network errors are caught and logged without propagating.
    /// </summary>
    private async Task PostEventToPeerAsync(string peerBaseUrl, CollaborationEvent evt, CancellationToken ct)
    {
        try
        {
            await PostJsonAsync($"{peerBaseUrl}/api/events", evt, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to deliver event {EventType} to peer {PeerUrl}", evt.EventType, peerBaseUrl);
        }
    }

    /// <summary>
    /// Serialises <paramref name="value"/> to JSON and POSTs it to <paramref name="url"/>.
    /// Uses <see cref="PeerRequestTimeout"/> as the HTTP timeout.
    /// </summary>
    private async Task PostJsonAsync<T>(string url, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PeerRequestTimeout);

        try
        {
            var response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out — log at debug level; this is expected on an unreachable host.
            _log.Debug("HTTP POST to {Url} timed out", url);
        }
    }

    /// <summary>
    /// Deserialises the request body as <typeparamref name="T"/>. Returns <c>null</c> on failure.
    /// </summary>
    private static async Task<T?> DeserializeBodyAsync<T>(HttpListenerRequest req, CancellationToken ct)
    {
        try
        {
            if (!req.HasEntityBody)
                return default;

            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body))
                return default;

            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Serialises <paramref name="value"/> to JSON and writes it to the HTTP response.
    /// Sets the <c>Content-Type</c> header to <c>application/json; charset=utf-8</c>.
    /// </summary>
    private static async Task WriteJsonResponseAsync<T>(
        HttpListenerResponse res,
        T value,
        CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;

            await res.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // If the response stream is already closed, swallow silently.
        }
    }

    // ── Private — DTO used for heartbeat endpoint ─────────────────────────────

    /// <summary>Body payload for POST /api/heartbeat.</summary>
    private sealed class HeartbeatPayload
    {
        public string SessionId { get; init; } = string.Empty;
    }
}
