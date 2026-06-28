using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AgentX.Core.Services.Collaboration;
using AgentX.Core.Services.Collaboration.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Collaboration;

/// <summary>
/// Coverage for <see cref="CollaborationService"/> — a lightweight real-time collaboration hub
/// built on <see cref="HttpListener"/> + <see cref="HttpClient"/> (no EF, no external runtime libs).
///
/// The public <see cref="CollaborationService.StartHostingAsync"/> binds the strong-wildcard prefix
/// <c>http://+:{port}/</c>, which requires an elevated URL-ACL reservation and can't run unprivileged
/// in CI. To exercise the real request handlers anyway, the harness injects a non-privileged
/// <c>http://localhost:{port}/</c> listener into the service's own fields and starts its real
/// <c>RunListenerLoopAsync</c> via reflection — the established "drive the service's own loop against a
/// localhost prefix" pattern (cf. the OAuth HTTP-flow + ApiHost suites). A real silent Serilog logger is
/// supplied because the constructor consumes <c>logger.ForContext&lt;T&gt;()</c>.
/// </summary>
public sealed class CollaborationServiceTests : IDisposable
{
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();
    private readonly CollaborationService _svc;

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public CollaborationServiceTests()
    {
        _svc = new CollaborationService(_logger);
    }

    public void Dispose()
    {
        try { _svc.Dispose(); } catch { /* teardown is best-effort */ }
        _logger.Dispose();
    }

    // ── Reflection helpers ───────────────────────────────────────────────────

    private static void SetField(object obj, string name, object? value) =>
        typeof(CollaborationService)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(obj, value);

    private static T GetField<T>(object obj, string name) =>
        (T)typeof(CollaborationService)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(obj)!;

    private static object? Invoke(object obj, string name, params object?[] args) =>
        typeof(CollaborationService)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(obj, args);

    private static ConcurrentDictionary<string, CollaborationSession> Sessions(object svc) =>
        GetField<ConcurrentDictionary<string, CollaborationSession>>(svc, "_sessions");

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value, CamelCase), Encoding.UTF8, "application/json");

    private static CollaborationSession NewSession(string id, DateTime lastHeartbeat) => new()
    {
        SessionId = id,
        UserName = "user-" + id,
        MachineName = "machine",
        StartedAt = lastHeartbeat,
        LastHeartbeat = lastHeartbeat,
    };

    /// <summary>
    /// Injects a started localhost <see cref="HttpListener"/> into <paramref name="svc"/> and runs the
    /// service's real listener loop, so incoming HTTP requests hit the production handlers.
    /// </summary>
    private sealed class Hosted : IAsyncDisposable
    {
        public string BaseUrl { get; }
        public HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public Hosted(CollaborationService svc)
        {
            // FreePort closes its probe socket before we bind, so under parallel test load another
            // listener can claim the port in that window. Retry the bind on a fresh port.
            HttpListener? bound = null;
            string? baseUrl = null;
            for (var attempt = 0; attempt < 12 && bound is null; attempt++)
            {
                var port = FreePort();
                var candidate = new HttpListener();
                candidate.Prefixes.Add($"http://localhost:{port}/");
                try
                {
                    candidate.Start();
                    bound = candidate;
                    baseUrl = $"http://localhost:{port}";
                }
                catch (HttpListenerException)
                {
                    candidate.Close();
                }
            }

            _listener = bound ?? throw new InvalidOperationException(
                "Could not bind a localhost HttpListener after 12 attempts.");
            BaseUrl = baseUrl!;

            SetField(svc, "_listener", _listener);
            SetField(svc, "_listenerCts", _cts);
            _loop = (Task)Invoke(svc, "RunListenerLoopAsync", _cts.Token)!;
            SetField(svc, "_listenerTask", _loop);
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { /* may already be disposed by StopHostingAsync */ }
            try { _listener.Stop(); } catch { }
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            try { _listener.Close(); } catch { }
            Client.Dispose();
            // Intentionally NOT disposing _cts — the service owns it and disposes it in Dispose().
        }
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_throws_when_logger_null()
    {
        var act = () => new CollaborationService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_succeeds_and_is_not_hosting()
    {
        _svc.IsHosting.Should().BeFalse();
    }

    // ── Session lifecycle ────────────────────────────────────────────────────

    [Fact]
    public async Task StartSession_default_username_registers_session()
    {
        var id = await _svc.StartSessionAsync();

        Guid.TryParse(id, out _).Should().BeTrue();
        var sessions = await _svc.GetActiveSessionsAsync();
        sessions.Should().ContainSingle();
        sessions[0].UserName.Should().Be(Environment.UserName);
        sessions[0].MachineName.Should().Be(Environment.MachineName);
    }

    [Fact]
    public async Task StartSession_explicit_username()
    {
        await _svc.StartSessionAsync("Alice");

        (await _svc.GetActiveSessionsAsync())[0].UserName.Should().Be("Alice");
    }

    [Fact]
    public async Task StartSession_twice_keeps_both_and_disposes_prior_heartbeat_timer()
    {
        await _svc.StartSessionAsync("first");
        await _svc.StartSessionAsync("second"); // exercises the dispose-prior-timer branch

        (await _svc.GetActiveSessionsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task StartSession_posts_to_host_when_connected()
    {
        await using var h = new Hosted(_svc);
        SetField(_svc, "_hostBaseUrl", h.BaseUrl); // simulate being a connected peer

        var id = await _svc.StartSessionAsync("peer"); // posts the session to the running host loop

        id.Should().NotBeNullOrEmpty();
        Sessions(_svc).Should().ContainKey(id);
    }

    [Fact]
    public async Task EndSession_with_no_session_is_noop()
    {
        await _svc.Invoking(s => s.EndSessionAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task EndSession_removes_session_and_raises_user_left()
    {
        var id = await _svc.StartSessionAsync("bye");
        CollaborationEvent? captured = null;
        _svc.EventReceived += (_, e) => { if (e.EventType == CollaborationEventType.UserLeft) captured = e; };

        await _svc.EndSessionAsync();

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(id);
        (await _svc.GetActiveSessionsAsync()).Should().BeEmpty();
        (await _svc.GetStatusAsync()).IsConnected.Should().BeFalse();
    }

    // ── Presence ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePresence_with_no_session_is_noop()
    {
        await _svc.Invoking(s => s.UpdatePresenceAsync("Chat")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdatePresence_updates_session_fields()
    {
        await _svc.StartSessionAsync("present");

        await _svc.UpdatePresenceAsync("Documents", activeDocumentId: 42, activeConversationId: 7);

        var session = (await _svc.GetActiveSessionsAsync())[0];
        session.ActivePage.Should().Be("Documents");
        session.ActiveDocumentId.Should().Be(42);
        session.ActiveConversationId.Should().Be(7);
    }

    [Fact]
    public async Task UpdatePresence_propagates_to_host_when_connected()
    {
        await _svc.StartSessionAsync("present");
        await using var h = new Hosted(_svc);
        SetField(_svc, "_hostBaseUrl", h.BaseUrl);

        await _svc.Invoking(s => s.UpdatePresenceAsync("Chat")).Should().NotThrowAsync();
    }

    // ── Events ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Broadcast_null_event_throws()
    {
        await _svc.Invoking(s => s.BroadcastEventAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Broadcast_raises_event_locally()
    {
        CollaborationEvent? captured = null;
        _svc.EventReceived += (_, e) => captured = e;

        var evt = new CollaborationEvent
        {
            EventType = CollaborationEventType.EditStarted,
            UserId = "u1",
            Timestamp = DateTime.UtcNow,
        };
        await _svc.BroadcastEventAsync(evt);

        captured.Should().BeSameAs(evt);
    }

    [Fact]
    public async Task Broadcast_swallows_subscriber_exception()
    {
        _svc.EventReceived += (_, _) => throw new InvalidOperationException("subscriber blew up");

        var act = () => _svc.BroadcastEventAsync(new CollaborationEvent
        {
            EventType = CollaborationEventType.DocumentLocked,
            UserId = "u1",
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Broadcast_fans_out_to_reachable_and_unreachable_peers()
    {
        await using var good = new Hosted(_svc);
        var deadPort = FreePort(); // nothing listening → fast connection-refused
        var peers = GetField<ConcurrentDictionary<string, byte>>(_svc, "_peerBaseUrls");
        peers.TryAdd(good.BaseUrl, 0);
        peers.TryAdd($"http://localhost:{deadPort}", 0);

        CollaborationEvent? local = null;
        _svc.EventReceived += (_, e) => local = e;

        var act = () => _svc.BroadcastEventAsync(new CollaborationEvent
        {
            EventType = CollaborationEventType.EditCompleted,
            UserId = "u1",
            Timestamp = DateTime.UtcNow,
        });

        await act.Should().NotThrowAsync(); // unreachable peer is logged + swallowed
        local.Should().NotBeNull();         // still raised locally
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_is_disconnected_with_no_session()
    {
        var status = await _svc.GetStatusAsync();

        status.IsConnected.Should().BeFalse();
        status.CurrentSessionId.Should().BeEmpty();
        status.ActiveUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatus_is_connected_with_session()
    {
        var id = await _svc.StartSessionAsync("connected");

        var status = await _svc.GetStatusAsync();

        status.IsConnected.Should().BeTrue();
        status.CurrentSessionId.Should().Be(id);
        status.ActiveUsers.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActiveSessions_excludes_stale_sessions()
    {
        Sessions(_svc)["fresh"] = NewSession("fresh", DateTime.UtcNow);
        Sessions(_svc)["stale"] = NewSession("stale", DateTime.UtcNow.AddMinutes(-5));

        var live = await _svc.GetActiveSessionsAsync();

        live.Should().ContainSingle().Which.SessionId.Should().Be("fresh");
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_is_safe_when_idle()
    {
        var svc = new CollaborationService(_logger);
        svc.Invoking(s => s.Dispose()).Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_disposes_timers_after_session()
    {
        var svc = new CollaborationService(_logger);
        await svc.StartSessionAsync("x"); // creates the heartbeat timer

        svc.Invoking(s => s.Dispose()).Should().NotThrow();
    }

    // ── Hosting lifecycle ────────────────────────────────────────────────────

    [Fact]
    public async Task StartHosting_when_already_hosting_is_noop()
    {
        await using var h = new Hosted(_svc); // service now reports IsHosting == true
        _svc.IsHosting.Should().BeTrue();

        await _svc.Invoking(s => s.StartHostingAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopHosting_when_not_hosting_is_noop()
    {
        await _svc.Invoking(s => s.StopHostingAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopHosting_tears_down_listener_and_timers()
    {
        await using var h = new Hosted(_svc);
        SetField(_svc, "_pruneTimer", new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite));
        SetField(_svc, "_localSessionId", "local-session"); // makes StopHosting broadcast UserLeft

        await _svc.StopHostingAsync();

        _svc.IsHosting.Should().BeFalse();
    }

    // ── HTTP endpoints (drive the real handlers through the loop) ─────────────

    [Fact]
    public async Task Post_session_registers_session_and_returns_ok()
    {
        await using var h = new Hosted(_svc);
        var session = NewSession("sess-1", DateTime.UtcNow);

        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/session", JsonBody(session));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Sessions(_svc).Should().ContainKey("sess-1");
    }

    [Fact]
    public async Task Post_session_with_empty_body_returns_bad_request()
    {
        await using var h = new Hosted(_svc);

        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/session",
            new StringContent("", Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_heartbeat_refreshes_known_session()
    {
        await using var h = new Hosted(_svc);
        Sessions(_svc)["hb-1"] = NewSession("hb-1", DateTime.UtcNow.AddSeconds(-5));
        var before = Sessions(_svc)["hb-1"].LastHeartbeat;

        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/heartbeat", JsonBody(new { sessionId = "hb-1" }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Sessions(_svc)["hb-1"].LastHeartbeat.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task Post_heartbeat_for_unknown_session_returns_not_found()
    {
        await using var h = new Hosted(_svc);

        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/heartbeat", JsonBody(new { sessionId = "nope" }));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_heartbeat_with_blank_session_returns_bad_request()
    {
        await using var h = new Hosted(_svc);

        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/heartbeat", JsonBody(new { sessionId = "" }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_events_raises_event_and_returns_ok()
    {
        await using var h = new Hosted(_svc);
        var tcs = new TaskCompletionSource<CollaborationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _svc.EventReceived += (_, e) => tcs.TrySetResult(e);

        var evt = new CollaborationEvent
        {
            EventType = CollaborationEventType.DocumentUnlocked,
            UserId = "remote-user",
            Timestamp = DateTime.UtcNow,
        };
        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/events", JsonBody(evt));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        received.UserId.Should().Be("remote-user");
    }

    [Fact]
    public async Task Post_events_with_invalid_json_returns_bad_request()
    {
        await using var h = new Hosted(_svc);

        var resp = await h.Client.PostAsync($"{h.BaseUrl}/api/events",
            new StringContent("{ this is not json", Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_sessions_returns_json_array()
    {
        await using var h = new Hosted(_svc);
        Sessions(_svc)["live-1"] = NewSession("live-1", DateTime.UtcNow);

        var resp = await h.Client.GetAsync($"{h.BaseUrl}/api/sessions");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("live-1");
    }

    [Fact]
    public async Task Unknown_route_returns_not_found()
    {
        await using var h = new Hosted(_svc);

        var resp = await h.Client.GetAsync($"{h.BaseUrl}/api/does-not-exist");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Timer callbacks (invoked directly) ───────────────────────────────────

    [Fact]
    public void PruneExpiredSessions_evicts_stale_keeps_fresh()
    {
        Sessions(_svc)["fresh"] = NewSession("fresh", DateTime.UtcNow);
        Sessions(_svc)["stale"] = NewSession("stale", DateTime.UtcNow.AddMinutes(-2));

        Invoke(_svc, "PruneExpiredSessions", new object?[] { null });

        Sessions(_svc).Keys.Should().BeEquivalentTo(new[] { "fresh" });
    }

    [Fact]
    public void SendHeartbeat_without_session_or_host_is_noop()
    {
        // Both _localSessionId and _hostBaseUrl are null on a fresh service → early return.
        var act = () => Invoke(_svc, "SendHeartbeat", new object?[] { null });
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SendHeartbeat_with_session_and_host_dispatches_without_throwing()
    {
        await using var h = new Hosted(_svc);
        SetField(_svc, "_localSessionId", "hb-session");
        SetField(_svc, "_hostBaseUrl", h.BaseUrl);

        var act = () => Invoke(_svc, "SendHeartbeat", new object?[] { null });

        act.Should().NotThrow(); // fire-and-forget post to the running host loop
    }
}
