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
public sealed class OutlookEmailProvider : IEmailProvider
{
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
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<OutlookEmailProvider>();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        var url = "https://graph.microsoft.com/v1.0/me/mailFolders?$select=id,displayName,unreadItemCount,totalItemCount,isHidden";
        var folders = new List<EmailFolderInfo>();

        while (!string.IsNullOrEmpty(url))
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GraphMailFolderListResponse>(json, JsonOptions);

            if (result?.Value is not null)
            {
                folders.AddRange(result.Value.Select(f => new EmailFolderInfo
                {
                    Id = f.Id ?? string.Empty,
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

    public async Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(
        string folderId, int maxResults = 50, string? deltaToken = null,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        var messages = new List<EmailMessage>();

        // Build URL: use delta endpoint if we have a token, otherwise initial delta query.
        string? url;
        if (!string.IsNullOrEmpty(deltaToken))
        {
            // deltaToken is the full URL from @odata.deltaLink
            url = deltaToken;
        }
        else
        {
            url = $"https://graph.microsoft.com/v1.0/me/mailFolders/{Uri.EscapeDataString(folderId)}/messages/delta" +
                  $"?$top={Math.Min(maxResults, 200)}" +
                  $"&$select=id,subject,bodyPreview,body,from,toRecipients,ccRecipients,receivedDateTime,isRead,flag,conversationId,hasAttachments,webLink";
        }

        var newDeltaToken = (string?)null;
        var fetched = 0;

        while (!string.IsNullOrEmpty(url) && fetched < maxResults)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Request plain text body in addition to HTML
            request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");

            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                // Delta token expired — fall back to full sync.
                _log.Warning("Outlook delta token expired — performing full sync for folder {FolderId}", folderId);
                return await GetMessagesAsync(folderId, maxResults, deltaToken: null, cancellationToken)
                    .ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GraphMessageListResponse>(json, JsonOptions);

            if (result?.Value is not null)
            {
                foreach (var msg in result.Value)
                {
                    if (msg.ODataRemoved is not null) continue; // deleted message

                    var email = ConvertMessage(msg, folderId);
                    messages.Add(email);
                    fetched++;

                    if (fetched >= maxResults) break;
                }
            }

            // Check for delta link (end of current round).
            if (!string.IsNullOrEmpty(result?.ODataDeltaLink))
            {
                newDeltaToken = result.ODataDeltaLink;
            }

            // Continue with next page if available.
            url = result?.ODataNextLink;
            if (url is not null && fetched >= maxResults) break;
        }

        return (messages, newDeltaToken);
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

    // ── Internal JSON models ───────────────────────────────────────────────────

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
        [JsonPropertyName("@removed")]
        public string? ODataRemoved { get; init; }
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