using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Gmail API v1 email provider. Fetches labels (folders) and messages
/// from the user's Gmail account via the REST API.
/// </summary>
/// <remarks>
/// Uses <see cref="IOAuthService"/> for OAuth2 access tokens.
/// Messages are fetched in two phases: list (IDs only) then batch-get (full content).
/// History API is used for delta sync when a historyId is available.
/// </remarks>
public sealed class GmailProvider : IEmailProvider
{
    public string ProviderId => "google";

    private readonly IOAuthService _oauthService;
    private readonly ILogger _log;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GmailProvider(IOAuthService oauthService, ILogger logger, string scopes)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<GmailProvider>();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://gmail.googleapis.com/gmail/v1/users/me/labels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<GmailLabelListResponse>(json, JsonOptions);

        return result?.Labels?.Select(l => new EmailFolderInfo
        {
            Id = l.Id ?? string.Empty,
            Name = l.Name ?? string.Empty,
            TotalCount = l.MessagesTotal ?? 0,
            UnreadCount = l.MessagesUnread ?? 0,
            SourceProvider = ProviderId,
        }).ToList() as IReadOnlyList<EmailFolderInfo> ?? [];
    }

    public async Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(
        string folderId, int maxResults = 50, string? deltaToken = null,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);
        var messages = new List<EmailMessage>();

        // If we have a delta token (Gmail historyId), use the History API.
        if (!string.IsNullOrEmpty(deltaToken))
        {
            return await GetMessagesViaHistoryAsync(token, deltaToken, folderId,
                maxResults, cancellationToken).ConfigureAwait(false);
        }

        // Full sync: list message IDs then batch-get details.
        var listUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages" +
                      $"?labelIds={Uri.EscapeDataString(folderId)}" +
                      $"&maxResults={Math.Min(maxResults, 500)}";

        var listRequest = new HttpRequestMessage(HttpMethod.Get, listUrl);
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var listResponse = await _http.SendAsync(listRequest, cancellationToken).ConfigureAwait(false);
        listResponse.EnsureSuccessStatusCode();

        var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var listResult = JsonSerializer.Deserialize<GmailMessageListResponse>(listJson, JsonOptions);

        if (listResult?.Messages is null || listResult.Messages.Count == 0)
            return ([], null);

        // Batch-get each message (format=full for body).
        var idsToFetch = listResult.Messages.Take(maxResults).Select(m => m.Id).ToList();

        foreach (var id in idsToFetch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var msg = await GetSingleMessageAsync(token, id, folderId, cancellationToken).ConfigureAwait(false);
                if (msg is not null)
                    messages.Add(msg);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Failed to get Gmail message {MessageId}", id);
            }
        }

        // The historyId from the list response serves as our delta token.
        var newDeltaToken = listResult.HistoryId;

        return (messages, newDeltaToken);
    }

    private async Task<(IReadOnlyList<EmailMessage>, string?)> GetMessagesViaHistoryAsync(
        string token, string startHistoryId, string folderId,
        int maxResults, CancellationToken cancellationToken)
    {
        var messages = new List<EmailMessage>();
        var url = $"https://gmail.googleapis.com/gmail/v1/users/me/history" +
                  $"?startHistoryId={Uri.EscapeDataString(startHistoryId)}" +
                  $"&historyTypes=messageAdded" +
                  $"&labelId={Uri.EscapeDataString(folderId)}" +
                  $"&maxResults={Math.Min(maxResults, 100)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // History expired — fall back to full sync.
                _log.Warning("Gmail historyId {HistoryId} expired — performing full sync", startHistoryId);
                return await GetMessagesAsync(folderId, maxResults, deltaToken: null, cancellationToken)
                    .ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GmailHistoryResponse>(json, JsonOptions);

            if (result?.History is null) return ([], result?.HistoryId);

            foreach (var record in result.History)
            {
                if (record.MessagesAdded is null) continue;

                foreach (var added in record.MessagesAdded)
                {
                    if (added.Message?.Id is null) continue;
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var msg = await GetSingleMessageAsync(token, added.Message.Id, folderId, cancellationToken)
                            .ConfigureAwait(false);
                        if (msg is not null)
                            messages.Add(msg);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Error(ex, "Failed to get Gmail message {MessageId} from history", added.Message.Id);
                    }
                }
            }

            return (messages, result.HistoryId);
        }
        catch (HttpRequestException ex)
        {
            _log.Error(ex, "Gmail History API request failed");
            return (messages, startHistoryId); // Keep the same token for retry
        }
    }

    private async Task<EmailMessage?> GetSingleMessageAsync(
        string token, string messageId, string folderId, CancellationToken cancellationToken)
    {
        var url = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{messageId}?format=full";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var gmailMsg = JsonSerializer.Deserialize<GmailMessage>(json, JsonOptions);

        if (gmailMsg is null) return null;

        var headers = gmailMsg.Payload?.Headers ?? [];
        var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(No Subject)";
        var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "";
        var toRaw = headers.FirstOrDefault(h => h.Name == "To")?.Value ?? "";
        var dateRaw = headers.FirstOrDefault(h => h.Name == "Date")?.Value ?? "";

        // Extract body text.
        var bodyText = ExtractBodyText(gmailMsg.Payload);
        var bodyHtml = ExtractBodyHtml(gmailMsg.Payload);

        // Parse From contact.
        var fromContact = ParseContact(from);

        // Parse To contacts.
        var toContacts = toRaw.Split(',', ';')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(ParseContact).ToList();

        // Parse date — use DateTimeOffset to correctly handle timezone offsets (RFC 2822).
        DateTime receivedAt = DateTime.MinValue;
        if (DateTimeOffset.TryParse(dateRaw, out var parsedOffset))
            receivedAt = parsedOffset.UtcDateTime;

        return new EmailMessage
        {
            Id = gmailMsg.Id ?? messageId,
            Subject = subject,
            BodyPreview = bodyText.Length > 300 ? bodyText[..300] : bodyText,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            From = fromContact,
            To = toContacts,
            ReceivedAt = receivedAt,
            IsRead = !(gmailMsg.LabelIds?.Contains("UNREAD") ?? false),
            IsStarred = gmailMsg.LabelIds?.Contains("STARRED") ?? false,
            HasAttachments = gmailMsg.Payload?.Parts?.Any(p => p.Filename is { Length: > 0 }) ?? false,
            ThreadId = gmailMsg.ThreadId ?? "",
            SourceProvider = ProviderId,
            AttachmentNames = gmailMsg.Payload?.Parts?
                .Where(p => !string.IsNullOrEmpty(p.Filename))
                .Select(p => p.Filename!)
                .ToList() ?? [],
            FolderName = gmailMsg.LabelIds?.FirstOrDefault() ?? "",
            FolderId = folderId,
            WebLink = $"https://mail.google.com/mail/u/0/#inbox/{gmailMsg.Id}",
        };
    }

    private static string ExtractBodyText(GmailMessagePart? part)
    {
        if (part is null) return "";
        if (part.MimeType == "text/plain" && part.Body?.Data is not null)
            return DecodeBase64Url(part.Body.Data);
        if (part.Parts is not null)
        {
            foreach (var sub in part.Parts)
            {
                var text = ExtractBodyText(sub);
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        return "";
    }

    private static string ExtractBodyHtml(GmailMessagePart? part)
    {
        if (part is null) return "";
        if (part.MimeType == "text/html" && part.Body?.Data is not null)
            return DecodeBase64Url(part.Body.Data);
        if (part.Parts is not null)
        {
            foreach (var sub in part.Parts)
            {
                var html = ExtractBodyHtml(sub);
                if (!string.IsNullOrEmpty(html)) return html;
            }
        }
        return "";
    }

    private static string DecodeBase64Url(string base64Url)
    {
        try
        {
            var padded = base64Url.PadRight((base64Url.Length + 3) / 4 * 4, '=')
                .Replace('-', '+').Replace('_', '/');
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch
        {
            return base64Url;
        }
    }

    private static EmailContact ParseContact(string raw)
    {
        // Parse "Display Name <email@example.com>" or just "email@example.com"
        var trimmed = raw.Trim();
        var ltIdx = trimmed.IndexOf('<');
        var gtIdx = trimmed.IndexOf('>');

        if (ltIdx > 0 && gtIdx > ltIdx)
        {
            return new EmailContact
            {
                DisplayName = trimmed[..ltIdx].Trim().TrimEnd('"'),
                EmailAddress = trimmed[(ltIdx + 1)..gtIdx].Trim(),
            };
        }

        return new EmailContact { EmailAddress = trimmed };
    }

    private Task<string> GetAccessTokenAsync()
        => _oauthService.GetAccessTokenAsync(ProviderId);

    // ── Internal JSON models ───────────────────────────────────────────────────

    private sealed class GmailLabelListResponse
    {
        [JsonPropertyName("labels")]
        public List<GmailLabel>? Labels { get; init; }
    }

    private sealed class GmailLabel
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("name")]
        public string? Name { get; init; }
        [JsonPropertyName("messagesTotal")]
        public int? MessagesTotal { get; init; }
        [JsonPropertyName("messagesUnread")]
        public int? MessagesUnread { get; init; }
    }

    private sealed class GmailMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<GmailMessageId> Messages { get; init; } = [];
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; init; }
        [JsonPropertyName("resultSizeEstimate")]
        public int ResultSizeEstimate { get; init; }
        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }
    }

    private sealed class GmailMessageId
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;
        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }
    }

    private sealed class GmailMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }
        [JsonPropertyName("labelIds")]
        public List<string>? LabelIds { get; init; }
        [JsonPropertyName("payload")]
        public GmailMessagePart? Payload { get; init; }
    }

    private sealed class GmailMessagePart
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }
        [JsonPropertyName("filename")]
        public string? Filename { get; init; }
        [JsonPropertyName("headers")]
        public List<GmailHeader>? Headers { get; init; }
        [JsonPropertyName("body")]
        public GmailMessageBody? Body { get; init; }
        [JsonPropertyName("parts")]
        public List<GmailMessagePart>? Parts { get; init; }
    }

    private sealed class GmailHeader
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    private sealed class GmailMessageBody
    {
        [JsonPropertyName("data")]
        public string? Data { get; init; }
        [JsonPropertyName("attachmentId")]
        public string? AttachmentId { get; init; }
        [JsonPropertyName("size")]
        public int? Size { get; init; }
    }

    private sealed class GmailHistoryResponse
    {
        [JsonPropertyName("history")]
        public List<GmailHistoryRecord>? History { get; init; }
        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; init; }
    }

    private sealed class GmailHistoryRecord
    {
        [JsonPropertyName("messagesAdded")]
        public List<GmailHistoryMessageAdded>? MessagesAdded { get; init; }
    }

    private sealed class GmailHistoryMessageAdded
    {
        [JsonPropertyName("message")]
        public GmailMessageId? Message { get; init; }
    }
}
