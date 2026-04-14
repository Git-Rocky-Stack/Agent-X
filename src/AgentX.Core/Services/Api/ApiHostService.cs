using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Api.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
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
    private readonly ILogger _log = Log.ForContext<ApiHostService>();

    // ── State ─────────────────────────────────────────────────────────────────

    private HttpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private Task? _requestLoopTask;
    private DateTime _startedAt;

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
        IInboxService inboxService)
    {
        _conversations = conversations;
        _documents = documents;
        _collections = collections;
        _search = search;
        _inboxService = inboxService;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(int port = 9846, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            _log.Debug("ApiHostService.StartAsync called while already running on port {Port}. No-op.", Port);
            return;
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

        // Signal the accept loop to exit
        _listenerCts?.Cancel();

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
            // CORS pre-flight — OPTIONS on any route
            if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                WriteCorsHeaders(resp);
                resp.StatusCode = 204;
                resp.Close();
                return;
            }

            WriteCorsHeaders(resp);

            var path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? string.Empty;
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

        // Run DB reads in parallel
        var docCountTask = Task.Run(() => _documents.GetTotalDocumentCountAsync(), ct);
        var convCountTask = Task.Run(() => _conversations.GetConversationCountAsync(), ct);

        await Task.WhenAll(docCountTask, convCountTask).ConfigureAwait(false);

        var payload = new ApiHealthDto
        {
            Status = "ok",
            Version = "1.0.0",
            Uptime = uptimeStr,
            DocumentCount = docCountTask.Result,
            ConversationCount = convCountTask.Result
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

        // Build markdown content with YAML frontmatter
        var fileName = SanitizeFileName(clipReq.Title);
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"title: \"{clipReq.Title.Replace("\"", "\\\"")}\"");
        sb.AppendLine($"source_url: \"{clipReq.SourceUrl}\"");

        if (!string.IsNullOrWhiteSpace(clipReq.Author))
            sb.AppendLine($"author: \"{clipReq.Author.Replace("\"", "\\\"")}\"");

        if (clipReq.PublishedDate.HasValue)
            sb.AppendLine($"published_date: \"{clipReq.PublishedDate.Value:yyyy-MM-dd}\"");

        sb.AppendLine($"clip_mode: {clipReq.ClipMode}");
        sb.AppendLine($"word_count: {clipReq.WordCount}");
        sb.AppendLine($"clipped_at: \"{DateTime.UtcNow:O}\"");

        if (clipReq.Metadata is not null)
        {
            foreach (var (key, value) in clipReq.Metadata)
            {
                var escapedValue = value.Replace("\"", "\\\"");
                sb.AppendLine($"{key}: \"{escapedValue}\"");
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(clipReq.Content);

        // Write to temp directory so InboxService can pick it up
        var tempDir = Path.Combine(Path.GetTempPath(), "AgentX", "clips");
        Directory.CreateDirectory(tempDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var tempFilePath = Path.Combine(tempDir, $"{fileName}-{timestamp}.md");

        await File.WriteAllTextAsync(tempFilePath, sb.ToString(), ct).ConfigureAwait(false);

        _log.Information(
            "API: Clipped content saved to {FilePath} (source: {SourceUrl}, mode: {ClipMode}, words: {WordCount})",
            tempFilePath, clipReq.SourceUrl, clipReq.ClipMode, clipReq.WordCount);

        // Add to Smart Inbox
        InboxItemEntity inboxItem;
        try
        {
            inboxItem = await _inboxService.AddToInboxAsync(
                tempFilePath,
                watchFolderId: null,
                sourceType: "browser-extension",
                sourceUrl: clipReq.SourceUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "API: Failed to add clipped content to inbox from {SourceUrl}", clipReq.SourceUrl);

            // Clean up temp file since inbox ingestion failed
            try { File.Delete(tempFilePath); } catch { /* best effort */ }

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
        var payload = new ApiExtensionHealthDto
        {
            Connected = true,
            Version = "1.4.0",
            InboxEnabled = true,
            Provider = "local"
        };

        await WriteJsonResponseAsync(resp, 200, ApiResponse<ApiExtensionHealthDto>.Ok(payload), ct).ConfigureAwait(false);
        return 200;
    }

    // ── Clip Helpers ──────────────────────────────────────────────────────────

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
            if (invalidChars.Contains(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
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

    private static void WriteCorsHeaders(HttpListenerResponse resp)
    {
        resp.AddHeader("Access-Control-Allow-Origin", "*");
        resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
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
