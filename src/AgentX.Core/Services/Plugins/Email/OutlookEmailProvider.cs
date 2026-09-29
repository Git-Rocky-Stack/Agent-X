using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Microsoft Graph API v1.0 email provider. Fetches mail folders and messages
/// from the user's Outlook account via the Microsoft Graph REST API.
/// Uses delta queries for incremental sync.
/// </summary>
/// <remarks>
/// The inbox is reported with the id <see cref="IEmailProvider.InboxFolderId"/> ("INBOX")
/// and read through Graph's well-known folder name "inbox", so the default folder selection
/// (which is Gmail's inbox label id) selects the Outlook inbox too.
/// </remarks>
public sealed class OutlookEmailProvider : IEmailProvider
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    public string ProviderId => "microsoft";

    private readonly IOAuthService _oauthService;
    private readonly ILogger _log;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OutlookEmailProvider(IOAuthService oauthService, ILogger logger, string scopes)
        : this(oauthService, logger, new HttpClient())
    {
    }

    /// <summary>Test seam: routes every request through <paramref name="handler"/>.</summary>
    internal OutlookEmailProvider(IOAuthService oauthService, ILogger logger, HttpMessageHandler handler)
        : this(oauthService, logger, new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))))
    {
    }

    private OutlookEmailProvider(IOAuthService oauthService, ILogger logger, HttpClient http)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<OutlookEmailProvider>();
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        var inboxId = await GetInboxIdAsync(token, cancellationToken).ConfigureAwait(false);
        var url = $"{GraphBase}/me/mailFolders?$select=id,displayName,unreadItemCount,totalItemCount,isHidden";
        var folders = new List<EmailFolderInfo>();

        while (!string.IsNullOrEmpty(url))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GraphMailFolderListResponse>(json, JsonOptions);

            if (result?.Value is not null)
            {
                folders.AddRange(result.Value.Select(f => new EmailFolderInfo
                {
                    // Graph ids are opaque; the inbox gets the id the settings select by default.
                    Id = f.Id is not null && string.Equals(f.Id, inboxId, StringComparison.Ordinal)
                        ? IEmailProvider.InboxFolderId
                        : f.Id ?? string.Empty,
                    Name = f.DisplayName ?? string.Empty,
                    TotalCount = f.TotalItemCount ?? 0,
                    UnreadCount = f.UnreadItemCount ?? 0,
                    SourceProvider = ProviderId,
                }));
            }

            url = result?.ODataNextLink;
        }

        return folders;
    }

    /// <summary>
    /// The opaque id of the mailbox's inbox, read through Graph's well-known folder name,
    /// or null when it cannot be read (the folders are then listed without the mapping).
    /// </summary>
    private async Task<string?> GetInboxIdAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/me/mailFolders/inbox?$select=id");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GraphMailFolder>(json, JsonOptions)?.Id;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _log.Warning(ex, "Could not identify the Outlook inbox folder");
            return null;
        }
    }

    public async Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(
        string folderId, int maxResults = 50, string? deltaToken = null,
        DateTime? receivedAfterUtc = null, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        var messages = new List<EmailMessage>();

        // Build URL: continue from the stored link if we have one, otherwise start a delta round.
        var incremental = !string.IsNullOrEmpty(deltaToken);
        var url = incremental
            ? deltaToken! // the full @odata.deltaLink (or @odata.nextLink) URL from the last sync
            : BuildInitialDeltaUrl(folderId, maxResults, receivedAfterUtc);

        string? newDeltaToken = null;

        while (!string.IsNullOrEmpty(url))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Request plain text body in addition to HTML
            request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // A stored link Graph no longer accepts (410 Gone when the sync state expired, 400
            // when it is malformed) is discarded for a new round from the start.
            if (incremental && response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest)
            {
                _log.Warning(
                    "Outlook rejected the stored delta link ({Status}); starting a full sync for folder {FolderId}",
                    (int)response.StatusCode, folderId);
                return await GetMessagesAsync(folderId, maxResults, deltaToken: null, receivedAfterUtc, cancellationToken)
                    .ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GraphMessageListResponse>(json, JsonOptions);

            foreach (var msg in result?.Value ?? [])
            {
                // A deleted or moved message arrives as {"id": ..., "@removed": {"reason": ...}}.
                if (msg.Removed is not null) continue;

                messages.Add(ConvertMessage(msg, folderId));
            }

            // The delta link ends the round.
            if (!string.IsNullOrEmpty(result?.ODataDeltaLink))
            {
                newDeltaToken = result.ODataDeltaLink;
                break;
            }

            url = result?.ODataNextLink;

            if (url is not null && messages.Count >= maxResults)
            {
                // The per-sync cap is reached mid-round. The next-page link is itself a valid
                // continuation, so it is kept and the next sync resumes there; discarding it
                // meant a large folder never reached its delta link.
                newDeltaToken = url;
                break;
            }
        }

        return (messages, newDeltaToken);
    }

    /// <summary>
    /// The first request of a delta round on <paramref name="folderId"/>. The inbox is
    /// addressed by Graph's well-known name. <paramref name="receivedAfterUtc"/> becomes the
    /// one filter message delta supports, on receivedDateTime.
    /// </summary>
    internal static string BuildInitialDeltaUrl(string folderId, int maxResults, DateTime? receivedAfterUtc)
    {
        var folderSegment = string.Equals(folderId, IEmailProvider.InboxFolderId, StringComparison.OrdinalIgnoreCase)
            ? "inbox"
            : Uri.EscapeDataString(folderId);

        var url = $"{GraphBase}/me/mailFolders/{folderSegment}/messages/delta" +
                  $"?$top={Math.Clamp(maxResults, 1, 200)}" +
                  "&$select=id,subject,bodyPreview,body,from,toRecipients,ccRecipients,receivedDateTime,isRead,flag,conversationId,hasAttachments,webLink";

        if (receivedAfterUtc is { } after)
        {
            var since = DateTime.SpecifyKind(after, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            url += "&$filter=" + Uri.EscapeDataString($"receivedDateTime ge {since}");
        }

        return url;
    }

    private static EmailMessage ConvertMessage(GraphMessage msg, string folderId)
    {
        var bodyText = msg.Body?.ContentType == "text"
            ? msg.Body.Content ?? ""
            : StripHtmlTags(msg.Body?.Content ?? "");

        var bodyHtml = msg.Body?.ContentType == "html"
            ? msg.Body.Content ?? ""
            : "";

        return new EmailMessage
        {
            Id = msg.Id ?? string.Empty,
            Subject = msg.Subject ?? "(No Subject)",
            BodyPreview = msg.BodyPreview ?? "",
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            From = ConvertContact(msg.From?.EmailAddress),
            To = msg.ToRecipients?.Select(r => ConvertContact(r.EmailAddress)).ToList() ?? [],
            Cc = msg.CcRecipients?.Select(r => ConvertContact(r.EmailAddress)).ToList() ?? [],
            ReceivedAt = msg.ReceivedDateTime ?? DateTime.MinValue,
            IsRead = msg.IsRead ?? false,
            IsStarred = msg.Flag?.FlagStatus == "flagged",
            HasAttachments = msg.HasAttachments ?? false,
            FolderId = folderId,
            FolderName = "",
            ThreadId = msg.ConversationId ?? "",
            SourceProvider = "microsoft",
            WebLink = msg.WebLink,
        };
    }

    private static EmailContact ConvertContact(GraphEmailAddress? addr)
    {
        if (addr is null) return new EmailContact();
        return new EmailContact
        {
            DisplayName = addr.Name ?? "",
            EmailAddress = addr.Address ?? "",
        };
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var result = new System.Text.StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) result.Append(c);
        }
        return result.ToString()
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ");
    }

    private Task<string> GetAccessTokenAsync()
        => _oauthService.GetAccessTokenAsync(ProviderId);

    // -- Internal JSON models ---------------------------------------------------

    private sealed class GraphMailFolderListResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMailFolder>? Value { get; init; }
        [JsonPropertyName("@odata.nextLink")]
        public string? ODataNextLink { get; init; }
    }

    private sealed class GraphMailFolder
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }
        [JsonPropertyName("unreadItemCount")]
        public int? UnreadItemCount { get; init; }
        [JsonPropertyName("totalItemCount")]
        public int? TotalItemCount { get; init; }
        [JsonPropertyName("isHidden")]
        public bool? IsHidden { get; init; }
    }

    private sealed class GraphMessageListResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessage>? Value { get; init; }
        [JsonPropertyName("@odata.nextLink")]
        public string? ODataNextLink { get; init; }
        [JsonPropertyName("@odata.deltaLink")]
        public string? ODataDeltaLink { get; init; }
    }

    private sealed class GraphMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("subject")]
        public string? Subject { get; init; }
        [JsonPropertyName("bodyPreview")]
        public string? BodyPreview { get; init; }
        [JsonPropertyName("body")]
        public GraphMessageBody? Body { get; init; }
        [JsonPropertyName("from")]
        public GraphMessageFrom? From { get; init; }
        [JsonPropertyName("toRecipients")]
        public List<GraphRecipient>? ToRecipients { get; init; }
        [JsonPropertyName("ccRecipients")]
        public List<GraphRecipient>? CcRecipients { get; init; }
        [JsonPropertyName("receivedDateTime")]
        public DateTime? ReceivedDateTime { get; init; }
        [JsonPropertyName("isRead")]
        public bool? IsRead { get; init; }
        [JsonPropertyName("flag")]
        public GraphMessageFlag? Flag { get; init; }
        [JsonPropertyName("hasAttachments")]
        public bool? HasAttachments { get; init; }
        [JsonPropertyName("conversationId")]
        public string? ConversationId { get; init; }
        [JsonPropertyName("webLink")]
        public string? WebLink { get; init; }
        /// <summary>
        /// Present (an object such as <c>{"reason": "deleted"}</c>) when the message was
        /// deleted or moved out of the folder. Typed as a string before, which made every
        /// delta page with a removal fail to parse.
        /// </summary>
        [JsonPropertyName("@removed")]
        public GraphRemoved? Removed { get; init; }
    }

    private sealed class GraphRemoved
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }


    private sealed class GraphMessageBody
    {
        [JsonPropertyName("contentType")]
        public string? ContentType { get; init; }
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class GraphMessageFrom
    {
        [JsonPropertyName("emailAddress")]
        public GraphEmailAddress? EmailAddress { get; init; }
    }

    private sealed class GraphRecipient
    {
        [JsonPropertyName("emailAddress")]
        public GraphEmailAddress? EmailAddress { get; init; }
    }

    private sealed class GraphEmailAddress
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
        [JsonPropertyName("address")]
        public string? Address { get; init; }
    }

    private sealed class GraphMessageFlag
    {
        [JsonPropertyName("flagStatus")]
        public string? FlagStatus { get; init; }
    }
}
