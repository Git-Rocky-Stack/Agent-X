using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Api.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.Services.Api;

/// <summary>
/// Embedded local REST API host built on <see cref="HttpListener"/>.
/// Exposes AgentX core data over HTTP for the mobile companion app and
/// external tool integrations. No ASP.NET Core dependency — the listener
/// runs entirely within the desktop process.
///
/// Endpoints:
///   GET  /api/health
///   GET  /api/documents
///   GET  /api/documents/{id}
///   GET  /api/conversations
///   GET  /api/conversations/{id}
///   GET  /api/collections
///   POST /api/search
///   POST /api/inbox/clip
///   GET  /api/auth/check
///   GET  /api/extension/health
/// </summary>
public sealed class ApiHostService : IApiHostService, IAsyncDisposable
{
    // ── Dependencies ─────────────────────────────────────────────────────────

    private readonly IConversationService _conversations;
    private readonly IDocumentService _documents;
    private readonly ICollectionService _collections;
    private readonly ISemanticSearchService _search;
    private readonly IInboxService _inboxService;
    private readonly ISettingsService _settings;
    private readonly IAppPathService _appPaths;
    private readonly ILogger _log = Log.ForContext<ApiHostService>();

    // ── State ─────────────────────────────────────────────────────────────────

    private HttpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private Task? _requestLoopTask;
    private DateTime _startedAt;

    /// <summary>
    /// Per-install bearer token required on every non-public route. Set at <see cref="StartAsync"/>
    /// and replaced by <see cref="SetAuthToken"/>; always accessed through <see cref="Volatile"/>
    /// because request threads read it while the settings page can swap it.
    /// </summary>
    private string? _authToken;

    /// <summary>Maximum number of requests processed concurrently.</summary>
    private readonly SemaphoreSlim _concurrencyGate = new(16, 16);

    // ── JSON options ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // ── IApiHostService ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <inheritdoc/>
    public int Port { get; private set; }

    /// <inheritdoc/>
    public string BaseUrl { get; private set; } = string.Empty;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ApiHostService(
        IConversationService conversations,
        IDocumentService documents,
        ICollectionService collections,
        ISemanticSearchService search,
        IInboxService inboxService,
        ISettingsService settings,
        IAppPathService appPaths)
    {
        _conversations = conversations;
        _documents = documents;
        _collections = collections;
        _search = search;
        _inboxService = inboxService;
        _settings = settings;
        _appPaths = appPaths;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(int port = 9846, string? authToken = null, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            _log.Debug("ApiHostService.StartAsync called while already running on port {Port}. No-op.", Port);
            return;
        }

        Volatile.Write(ref _authToken, authToken);
        if (string.IsNullOrEmpty(authToken))
        {
            // Defensive: starting without a token means every data route returns 401. Log loudly
            // so a misconfiguration is visible rather than silently exposing or locking out the API.
            _log.Warning("ApiHostService starting WITHOUT an auth token — all non-public routes will return 401.");
        }

        Port = port;
        BaseUrl = $"http://localhost:{port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            _log.Error(ex, "Failed to start HTTP listener on {BaseUrl}. Ensure no other process owns the port.", BaseUrl);
            throw;
        }

        IsRunning = true;
        _startedAt = DateTime.UtcNow;
        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _requestLoopTask = RunRequestLoopAsync(_listenerCts.Token);

        _log.Information("AgentX REST API listening on {BaseUrl}", BaseUrl);
        await Task.CompletedTask; // preserve async signature for interface contract
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning)
        {
            return;
        }

        _log.Information("Stopping AgentX REST API…");

        IsRunning = false;

        // Signal the accept loop to exit. FU-2: switched from sync Cancel() to
        // CancelAsync() — the latter properly awaits any registered callbacks
        // before returning, which matters when the accept loop has cleanup
        // hooks subscribed to the token's Register().
        if (_listenerCts is not null)
            await _listenerCts.CancelAsync().ConfigureAwait(false);

        // Stop the listener — this unblocks any pending GetContextAsync call
        try { _listener?.Stop(); } catch { /* intentional */ }

        // Wait for the loop to finish draining in-flight requests
        if (_requestLoopTask is not null)
        {
            try
            {
                await _requestLoopTask.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* shutdown timeout — acceptable */ }
            catch (TimeoutException) { /* shutdown timeout — acceptable */ }
        }

        _listener?.Close();
        _listener = null;
        _listenerCts?.Dispose();
        _listenerCts = null;

        _log.Information("AgentX REST API stopped.");
    }

    /// <inheritdoc/>
    public void SetAuthToken(string? authToken)
    {
        Volatile.Write(ref _authToken, authToken);

        if (string.IsNullOrEmpty(authToken))
            _log.Warning("Local REST API token cleared; all non-public routes now return 401.");
        else
            _log.Information("Local REST API token replaced; the previous token is no longer accepted.");
    }

    // ── Request Loop ──────────────────────────────────────────────────────────

    private async Task RunRequestLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested || !IsRunning)
            {
                // Listener was stopped — clean exit
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested || !IsRunning)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Unexpected error accepting HTTP request. Continuing loop.");
                continue;
            }

            // Dispatch each request on the thread pool; do not await here so the
            // accept loop remains free to pick up the next connection immediately.
            _ = HandleRequestAsync(ctx, ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        await _concurrencyGate.WaitAsync(ct).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var req = ctx.Request;
        var resp = ctx.Response;
        int statusCode = 200;

        try
        {
            var origin = req.Headers["Origin"];

            // CORS pre-flight — OPTIONS on any route
            if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                WriteCorsHeaders(resp, origin);
                resp.StatusCode = 204;
                resp.Close();
                return;
            }

            WriteCorsHeaders(resp, origin);

            var path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? string.Empty;

            // Authentication — every route except the public health probe requires the bearer token.
            if (!LocalApiSecurity.IsPublicPath(path)
                && !LocalApiSecurity.IsAuthorized(req.Headers["Authorization"], Volatile.Read(ref _authToken)))
            {
                statusCode = 401;
                resp.AddHeader("WWW-Authenticate", "Bearer");
                await WriteErrorResponseAsync(resp, 401,
                    "Unauthorized. A valid API token is required. Pair the client with the token from AgentX Settings.",
                    ct).ConfigureAwait(false);
                return;
            }

            var method = req.HttpMethod.ToUpperInvariant();

            statusCode = await RouteAsync(ctx, method, path, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unhandled exception processing {Method} {Path}", req.HttpMethod, req.Url?.AbsolutePath);

            statusCode = 500;
            try
            {
                await WriteErrorResponseAsync(resp, 500, "An internal server error occurred.", ct).ConfigureAwait(false);
            }
            catch
            {
                // If we can't write the error response the connection is already gone
            }
        }
        finally
        {
            sw.Stop();
            _log.Information("{Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                req.HttpMethod,
                req.Url?.AbsolutePath,
                statusCode,
                sw.ElapsedMilliseconds);

            _concurrencyGate.Release();
        }
    }

    // ── Router ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches the request to the appropriate handler and returns the HTTP status code.
    /// </summary>
    private async Task<int> RouteAsync(
        HttpListenerContext ctx,
        string method,
        string path,
        CancellationToken ct)
    {
        var resp = ctx.Response;

        // GET /api/health
        if (method == "GET" && path == "/api/health")
            return await HandleGetHealthAsync(resp, ct).ConfigureAwait(false);

        // GET /api/documents
        if (method == "GET" && path == "/api/documents")
            return await HandleGetDocumentsAsync(resp, ct).ConfigureAwait(false);

        // GET /api/documents/{id}
        if (method == "GET" && path.StartsWith("/api/documents/", StringComparison.Ordinal))
        {
            var segment = path["/api/documents/".Length..];
            if (long.TryParse(segment, out var docId))
                return await HandleGetDocumentByIdAsync(resp, docId, ct).ConfigureAwait(false);
        }

        // GET /api/conversations
        if (method == "GET" && path == "/api/conversations")
            return await HandleGetConversationsAsync(resp, ct).ConfigureAwait(false);

        // GET /api/conversations/{id}
        if (method == "GET" && path.StartsWith("/api/conversations/", StringComparison.Ordinal))
        {
            var segment = path["/api/conversations/".Length..];
            if (long.TryParse(segment, out var convId))
                return await HandleGetConversationByIdAsync(resp, convId, ct).ConfigureAwait(false);
        }

        // GET /api/collections
        if (method == "GET" && path == "/api/collections")
            return await HandleGetCollectionsAsync(resp, ct).ConfigureAwait(false);

        // POST /api/search
        if (method == "POST" && path == "/api/search")
            return await HandlePostSearchAsync(ctx, ct).ConfigureAwait(false);

        // POST /api/inbox/clip
        if (method == "POST" && path == "/api/inbox/clip")
            return await HandlePostClipAsync(ctx, ct).ConfigureAwait(false);

        // GET /api/auth/check
        if (method == "GET" && path == "/api/auth/check")
            return await HandleGetAuthCheckAsync(resp, ct).ConfigureAwait(false);

        // GET /api/extension/health
        if (method == "GET" && path == "/api/extension/health")
            return await HandleGetExtensionHealthAsync(resp, ct).ConfigureAwait(false);

        // 404 fallback
        await WriteErrorResponseAsync(resp, 404, $"Route not found: {method} {path}", ct).ConfigureAwait(false);
        return 404;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task<int> HandleGetHealthAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var uptime = DateTime.UtcNow - _startedAt;
        var uptimeStr = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";

        // Sequential on purpose. Both counts run on the one shared AgentXDbContext, and EF Core
        // rejects a second operation while the first is in flight ("A second operation was started
        // on this context"), so running them in parallel made this probe, which the mobile app
        // uses as its connectivity check, fail intermittently with a 500.
        var docCount = await _documents.GetTotalDocumentCountAsync().ConfigureAwait(false);
        var convCount = await _conversations.GetConversationCountAsync().ConfigureAwait(false);

        var payload = new ApiHealthDto
        {
            Status = "ok",
            Version = AppVersionInfo.Display,
            Uptime = uptimeStr,
            DocumentCount = docCount,
            ConversationCount = convCount
        };

        await WriteJsonResponseAsync(resp, 200, ApiResponse<ApiHealthDto>.Ok(payload), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleGetDocumentsAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var entities = await Task.Run(() => _documents.GetAllDocumentsAsync(ct: ct), ct).ConfigureAwait(false);

        var dtos = entities.Select(d => new ApiDocumentDto(
            d.Id,
            d.FileName,
            d.FileType,
            d.FileSizeBytes,
            d.ImportedAt,
            d.IndexingStatus)).ToList();

        await WriteJsonResponseAsync(resp, 200, ApiResponse<List<ApiDocumentDto>>.Ok(dtos), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleGetDocumentByIdAsync(
        HttpListenerResponse resp, long documentId, CancellationToken ct)
    {
        var entity = await Task.Run(() => _documents.GetDocumentAsync(documentId), ct).ConfigureAwait(false);

        if (entity is null)
        {
            await WriteErrorResponseAsync(resp, 404, $"Document {documentId} not found.", ct).ConfigureAwait(false);
            return 404;
        }

        var dto = new ApiDocumentDto(
            entity.Id,
            entity.FileName,
            entity.FileType,
            entity.FileSizeBytes,
            entity.ImportedAt,
            entity.IndexingStatus);

        await WriteJsonResponseAsync(resp, 200, ApiResponse<ApiDocumentDto>.Ok(dto), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleGetConversationsAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var entities = await Task.Run(
            () => _conversations.GetAllConversationsAsync(includeArchived: false), ct)
            .ConfigureAwait(false);

        var dtos = entities.Select(c => new ApiConversationDto(
            c.Id,
            c.Title,
            c.ModelId,
            c.CreatedAt,
            c.UpdatedAt,
            c.MessageCount,
            c.TokensUsed)).ToList();

        await WriteJsonResponseAsync(resp, 200, ApiResponse<List<ApiConversationDto>>.Ok(dtos), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleGetConversationByIdAsync(
        HttpListenerResponse resp, long conversationId, CancellationToken ct)
    {
        var entity = await Task.Run(
            () => _conversations.GetConversationAsync(conversationId), ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            await WriteErrorResponseAsync(resp, 404, $"Conversation {conversationId} not found.", ct).ConfigureAwait(false);
            return 404;
        }

        var dto = new ApiConversationDto(
            entity.Id,
            entity.Title,
            entity.ModelId,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.MessageCount,
            entity.TokensUsed);

        await WriteJsonResponseAsync(resp, 200, ApiResponse<ApiConversationDto>.Ok(dto), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleGetCollectionsAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var entities = await Task.Run(() => _collections.GetAllCollectionsAsync(), ct).ConfigureAwait(false);

        var dtos = entities.Select(c => new ApiCollectionDto(
            c.Id,
            c.Name,
            c.Description,
            c.DocumentCount,
            c.CreatedAt)).ToList();

        await WriteJsonResponseAsync(resp, 200, ApiResponse<List<ApiCollectionDto>>.Ok(dtos), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandlePostSearchAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var resp = ctx.Response;
        var req = ctx.Request;

        // Deserialize request body
        ApiSearchRequest? searchReq;
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            searchReq = JsonSerializer.Deserialize<ApiSearchRequest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Malformed JSON in POST /api/search body.");
            await WriteErrorResponseAsync(resp, 400, "Invalid JSON in request body.", ct).ConfigureAwait(false);
            return 400;
        }

        if (searchReq is null || string.IsNullOrWhiteSpace(searchReq.Query))
        {
            await WriteErrorResponseAsync(resp, 400, "Request body must include a non-empty 'query' field.", ct).ConfigureAwait(false);
            return 400;
        }

        var query = new SearchQuery
        {
            QueryText = searchReq.Query,
            TopK = Math.Clamp(searchReq.TopK, 1, 50),
            MinScore = Math.Clamp(searchReq.MinScore, 0f, 1f),
            Mode = SearchMode.Semantic
        };

        var results = await Task.Run(() => _search.SearchAsync(query, ct), ct).ConfigureAwait(false);

        var dtos = results.Select(r => new ApiSearchResultDto(
            r.DocumentId,
            r.FileName,
            r.MatchedText,
            r.Score)).ToList();

        await WriteJsonResponseAsync(resp, 200, ApiResponse<List<ApiSearchResultDto>>.Ok(dtos), ct).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandlePostClipAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var resp = ctx.Response;
        var req = ctx.Request;

        // Deserialize request body
        ApiClipRequest? clipReq;
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            clipReq = JsonSerializer.Deserialize<ApiClipRequest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Malformed JSON in POST /api/inbox/clip body.");
            await WriteErrorResponseAsync(resp, 400, "Invalid JSON in request body.", ct).ConfigureAwait(false);
            return 400;
        }

        if (clipReq is null || string.IsNullOrWhiteSpace(clipReq.Content))
        {
            await WriteErrorResponseAsync(resp, 400, "Request body must include a non-empty 'content' field.", ct).ConfigureAwait(false);
            return 400;
        }

        // Markdown with YAML front matter. Every client-supplied value is escaped, so a title or
        // URL cannot break out of the front matter or inject keys of its own.
        var clippedAtUtc = DateTime.UtcNow;
        var markdown = BuildClipMarkdown(clipReq, clippedAtUtc);

        // A clip is a pending inbox item that must survive until the user triages it, so it lives
        // under the app data root. %TEMP% is swept by Storage Sense and disk cleanup, which
        // silently orphaned inbox items whose file had been deleted underneath them.
        var clipsDir = Path.Combine(_appPaths.GetAppDataPath(), ClipsFolderName);
        Directory.CreateDirectory(clipsDir);

        var clipFilePath = await WriteNewClipFileAsync(clipsDir, clipReq.Title, clippedAtUtc, markdown, ct)
            .ConfigureAwait(false);

        _log.Information(
            "API: Clipped content saved to {FilePath} (source: {SourceUrl}, mode: {ClipMode}, words: {WordCount})",
            clipFilePath, clipReq.SourceUrl, clipReq.ClipMode, clipReq.WordCount);

        // Add to Smart Inbox
        InboxItemEntity inboxItem;
        try
        {
            inboxItem = await _inboxService.AddToInboxAsync(
                clipFilePath,
                watchFolderId: null,
                sourceType: "browser-extension",
                sourceUrl: clipReq.SourceUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "API: Failed to add clipped content to inbox from {SourceUrl}", clipReq.SourceUrl);

            // Clean up the clip file since inbox ingestion failed
            try { File.Delete(clipFilePath); } catch { /* best effort */ }

            await WriteErrorResponseAsync(resp, 500, "Failed to add clip to inbox.", ct).ConfigureAwait(false);
            return 500;
        }

        var clipResponse = new ApiClipResponse
        {
            InboxItemId = inboxItem.Id,
            Status = "clipped",
            Message = $"Content clipped to inbox as item #{inboxItem.Id}."
        };

        await WriteJsonResponseAsync(resp, 201, ApiResponse<ApiClipResponse>.Ok(clipResponse), ct).ConfigureAwait(false);
        return 201;
    }

    private async Task<int> HandleGetExtensionHealthAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var settings = await _settings.GetSettingsAsync().ConfigureAwait(false);

        var payload = new ApiExtensionHealthDto
        {
            Connected = true,
            Version = AppVersionInfo.Display,
            // The Smart Inbox has no off switch in AppSettings: the clip route is served whenever
            // the API itself is running, which it is if this probe answers.
            InboxEnabled = true,
            Provider = settings.ActiveProviderId
        };

        await WriteJsonResponseAsync(resp, 200, ApiResponse<ApiExtensionHealthDto>.Ok(payload), ct).ConfigureAwait(false);
        return 200;
    }

    /// <summary>
    /// GET /api/auth/check. The auth gate in <see cref="HandleRequestAsync"/> has already rejected
    /// a missing or wrong token with 401, so answering at all confirms the token. Clients call this
    /// during pairing instead of trusting the public health probe, which accepts any token.
    /// </summary>
    private static async Task<int> HandleGetAuthCheckAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var payload = new ApiAuthCheckDto
        {
            Authenticated = true,
            Version = AppVersionInfo.Display
        };

        await WriteJsonResponseAsync(resp, 200, ApiResponse<ApiAuthCheckDto>.Ok(payload), ct).ConfigureAwait(false);
        return 200;
    }

    // ── Clip Helpers ──────────────────────────────────────────────────────────

    /// <summary>Folder under the app data root that holds clipped pages awaiting triage.</summary>
    internal const string ClipsFolderName = "Clips";

    /// <summary>
    /// Longest title fragment used in a clip file name. Long page titles (300+ characters are
    /// common) otherwise pushed the full path past MAX_PATH and failed the clip with a 500.
    /// </summary>
    internal const int MaxClipFileStemLength = 80;

    /// <summary>A raw published-date value longer than this is not a date; it is not parsed.</summary>
    private const int MaxPublishedDateLength = 64;

    /// <summary>Front matter keys the host writes itself; client metadata may not reuse them.</summary>
    private static readonly string[] ReservedFrontMatterKeys =
        { "title", "source_url", "author", "published_date", "clip_mode", "word_count", "clipped_at" };

    /// <summary>Compact date shapes the general parser does not accept, e.g. <c>20240305</c>.</summary>
    private static readonly string[] CompactDateFormats = { "yyyyMMdd" };

    /// <summary>
    /// A date worth parsing names a four-digit year. Without this, a bare time such as
    /// <c>10:00</c> or a partial <c>5 March</c> would parse as today's date or this year's.
    /// </summary>
    private static readonly Regex HasFourDigitYear = new(@"\d{4}", RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses a published date leniently. Accepts ISO 8601 with or without a colon in the offset,
    /// space-separated date and time, year-month, RFC 1123 and similar invariant forms, plus the
    /// compact <c>yyyyMMdd</c> form. Returns the calendar date as the page states it (in the
    /// page's own offset), or null when the value is missing or cannot be read. A bad date never
    /// fails the clip.
    /// </summary>
    internal static DateTime? ParsePublishedDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxPublishedDateLength)
            return null;

        var value = raw.Trim();
        if (!HasFourDigitYear.IsMatch(value))
            return null;

        if (DateTime.TryParseExact(value, CompactDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var compact))
        {
            return compact.Date;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.DateTime.Date;
        }

        return null;
    }

    /// <summary>
    /// Builds the clip file: YAML front matter followed by the clipped content. All dates and
    /// numbers are formatted with the invariant culture, so the file reads the same whatever the
    /// user's regional settings (a th-TH or ar-SA calendar would otherwise write Buddhist or
    /// Hijri years into machine-readable fields).
    /// </summary>
    internal static string BuildClipMarkdown(ApiClipRequest clip, DateTime clippedAtUtc)
    {
        var title = string.IsNullOrWhiteSpace(clip.Title) ? "Untitled" : clip.Title.Trim();
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"title: {YamlQuote(title)}");
        sb.AppendLine($"source_url: {YamlQuote(clip.SourceUrl ?? string.Empty)}");

        if (!string.IsNullOrWhiteSpace(clip.Author))
            sb.AppendLine($"author: {YamlQuote(clip.Author.Trim())}");

        if (ParsePublishedDate(clip.PublishedDate) is { } published)
            sb.AppendLine($"published_date: {YamlQuote(published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");

        sb.AppendLine($"clip_mode: {FormatClipMode(clip.ClipMode)}");
        sb.AppendLine($"word_count: {Math.Max(0, clip.WordCount).ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"clipped_at: {YamlQuote(clippedAtUtc.ToString("O", CultureInfo.InvariantCulture))}");

        if (clip.Metadata is not null)
        {
            var written = new HashSet<string>(ReservedFrontMatterKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var (rawKey, value) in clip.Metadata)
            {
                var key = rawKey.Trim();
                if (key.Length == 0 || value is null || !written.Add(key))
                    continue;

                // Keys are quoted too: a bare key could carry a colon, a newline, or a YAML
                // keyword such as "null" or "true".
                sb.AppendLine($"{YamlQuote(key)}: {YamlQuote(value)}");
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(clip.Content);

        return sb.ToString();
    }

    /// <summary>
    /// Builds a unique clip file name: a sanitized, length-capped title, the UTC clip time to the
    /// second, and a random suffix. The suffix matters: "Clip All Tabs" sends several same-title
    /// pages within one second, and a title-plus-timestamp name made each clip overwrite the
    /// previous one (and the inbox, which de-duplicates by path, kept only the first item).
    /// </summary>
    internal static string BuildClipFileName(string? title, DateTime clippedAtUtc)
    {
        var stem = SanitizeFileName(title);
        if (stem.Length > MaxClipFileStemLength)
        {
            var cut = MaxClipFileStemLength;
            if (char.IsHighSurrogate(stem[cut - 1]))
                cut--;
            stem = stem[..cut].TrimEnd(' ', '.', '_');
            if (stem.Length == 0)
                stem = "untitled";
        }

        var timestamp = clippedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var unique = Guid.NewGuid().ToString("N")[..8];
        return $"{stem}-{timestamp}-{unique}.md";
    }

    /// <summary>
    /// Writes a clip to a new file. <see cref="FileMode.CreateNew"/> never replaces an existing
    /// file, so even an improbable name collision fails loudly instead of losing an earlier clip.
    /// </summary>
    private static async Task<string> WriteNewClipFileAsync(
        string directory, string? title, DateTime clippedAtUtc, string markdown, CancellationToken ct)
    {
        var path = Path.Combine(directory, BuildClipFileName(title, clippedAtUtc));

        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(markdown.AsMemory(), ct).ConfigureAwait(false);

        return path;
    }

    /// <summary>
    /// Formats a value as a YAML double-quoted scalar. Backslashes and quotes are escaped, and so
    /// is every line break or control character: a raw newline in a title used to end the front
    /// matter early ("\n---") and let the rest of the title inject arbitrary keys.
    /// </summary>
    internal static string YamlQuote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    // C0/C1 controls, DEL, the Unicode line and paragraph separators, NEL and the
                    // BOM are all line breaks or invisible in some YAML parser; spell them out.
                    if (char.IsControl(c) || c is (char)0x2028 or (char)0x2029 or (char)0xFEFF)
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Known clip modes are written bare; anything else is quoted verbatim.</summary>
    private static string FormatClipMode(string? mode) =>
        mode?.Trim().ToLowerInvariant() switch
        {
            "full" => "full",
            "selection" => "selection",
            "reader" => "reader",
            _ => YamlQuote(mode ?? string.Empty)
        };

    /// <summary>
    /// Sanitizes a title string for use as a file name by removing or replacing
    /// characters that are invalid in Windows file paths.
    /// </summary>
    private static string SanitizeFileName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "untitled";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(title.Length);

        foreach (var c in title)
        {
            // Control characters are invalid in Windows names; test them explicitly so the result
            // does not depend on the platform's GetInvalidFileNameChars list.
            if (invalidChars.Contains(c) || char.IsControl(c)
                || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                sanitized.Append('_');
            }
            else
            {
                sanitized.Append(c);
            }
        }

        // Collapse consecutive underscores and trim edges
        var result = string.Join("_", sanitized.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(result) ? "untitled" : result;
    }

    // ── Response Helpers ──────────────────────────────────────────────────────

    private static async Task WriteJsonResponseAsync<T>(
        HttpListenerResponse resp,
        int statusCode,
        ApiResponse<T> payload,
        CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = json.Length;

        try
        {
            await resp.OutputStream.WriteAsync(json, ct).ConfigureAwait(false);
        }
        finally
        {
            resp.OutputStream.Close();
        }
    }

    private static async Task WriteErrorResponseAsync(
        HttpListenerResponse resp,
        int statusCode,
        string error,
        CancellationToken ct)
    {
        await WriteJsonResponseAsync(resp, statusCode, ApiResponse<object>.Fail(error), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Emits CORS headers scoped to the request origin. Only browser-extension origins receive an
    /// <c>Access-Control-Allow-Origin</c> grant; ordinary web pages get none, so a malicious site
    /// cannot read API responses even though the listener is on localhost. (Combined with the
    /// bearer-token requirement, a web page would also lack the token.)
    /// </summary>
    private static void WriteCorsHeaders(HttpListenerResponse resp, string? requestOrigin)
    {
        var allowedOrigin = LocalApiSecurity.ResolveAllowedOrigin(requestOrigin);
        if (allowedOrigin is null)
            return;

        resp.AddHeader("Access-Control-Allow-Origin", allowedOrigin);
        resp.AddHeader("Vary", "Origin");
        resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, X-Requested-With");
        resp.AddHeader("Access-Control-Max-Age", "86400");
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _concurrencyGate.Dispose();
    }
}
