using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
/// Messages are fetched in two phases: list (IDs only) then get (full content).
/// A full sync records the mailbox's history id from <c>users.getProfile</c> (the message
/// list carries none); later syncs read the History API from there.
/// </remarks>
public sealed class GmailProvider : IEmailProvider
{
    private const string ApiBase = "https://gmail.googleapis.com/gmail/v1/users/me";

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
        : this(oauthService, logger, new HttpClient())
    {
    }

    /// <summary>Test seam: routes every request through <paramref name="handler"/>.</summary>
    internal GmailProvider(IOAuthService oauthService, ILogger logger, HttpMessageHandler handler)
        : this(oauthService, logger, new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))))
    {
    }

    private GmailProvider(IOAuthService oauthService, ILogger logger, HttpClient http)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<GmailProvider>();
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);

        using var response = await SendAsync($"{ApiBase}/labels", token, cancellationToken).ConfigureAwait(false);
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
        DateTime? receivedAfterUtc = null, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync().ConfigureAwait(false);

        // If we have a delta token (Gmail historyId), use the History API.
        if (!string.IsNullOrEmpty(deltaToken))
        {
            return await GetMessagesViaHistoryAsync(token, deltaToken, folderId,
                maxResults, receivedAfterUtc, cancellationToken).ConfigureAwait(false);
        }

        // The history id is taken before listing, so nothing that arrives while the list is
        // read falls between the two; a message seen by both is only upserted twice.
        var historyId = await GetCurrentHistoryIdAsync(token, cancellationToken).ConfigureAwait(false);

        // Full sync: list message IDs then get details.
        var listUrl = $"{ApiBase}/messages" +
                      $"?labelIds={Uri.EscapeDataString(folderId)}" +
                      $"&maxResults={Math.Clamp(maxResults, 1, 500)}";

        if (receivedAfterUtc is { } after)
        {
            // Gmail search accepts epoch seconds for after:, which avoids any date-format or
            // time zone interpretation.
            var epochSeconds = new DateTimeOffset(DateTime.SpecifyKind(after, DateTimeKind.Utc)).ToUnixTimeSeconds();
            listUrl += $"&q={Uri.EscapeDataString(string.Create(CultureInfo.InvariantCulture, $"after:{epochSeconds}"))}";
        }

        using var listResponse = await SendAsync(listUrl, token, cancellationToken).ConfigureAwait(false);
        listResponse.EnsureSuccessStatusCode();

        var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var listResult = JsonSerializer.Deserialize<GmailMessageListResponse>(listJson, JsonOptions);

        var messages = new List<EmailMessage>();
        foreach (var id in (listResult?.Messages ?? []).Take(maxResults).Select(m => m.Id))
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

        return (messages, historyId);
    }

    /// <summary>
    /// The mailbox's current history id from <c>users.getProfile</c>, or null when it cannot be
    /// read (the next sync is then a full read again rather than no sync at all).
    /// </summary>
    private async Task<string?> GetCurrentHistoryIdAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync($"{ApiBase}/profile", token, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GmailProfileResponse>(json, JsonOptions)?.HistoryId;
        }
        catch (HttpRequestException ex)
        {
            _log.Warning(ex, "Could not read the Gmail history id; the next sync will read the folder in full again");
            return null;
        }
    }

    private async Task<(IReadOnlyList<EmailMessage>, string?)> GetMessagesViaHistoryAsync(
        string token, string startHistoryId, string folderId,
        int maxResults, DateTime? receivedAfterUtc, CancellationToken cancellationToken)
    {
        var messages = new List<EmailMessage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        string? mailboxHistoryId = null;
        string? lastRecordId = null;

        do
        {
            var url = $"{ApiBase}/history" +
                      $"?startHistoryId={Uri.EscapeDataString(startHistoryId)}" +
                      $"&historyTypes=messageAdded" +
                      $"&labelId={Uri.EscapeDataString(folderId)}" +
                      $"&maxResults={Math.Clamp(maxResults, 1, 500)}";
            if (pageToken is not null)
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await SendAsync(url, token, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // History expired — fall back to full sync.
                _log.Warning("Gmail historyId {HistoryId} expired — performing full sync", startHistoryId);
                return await GetMessagesAsync(folderId, maxResults, deltaToken: null, receivedAfterUtc, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Other failures propagate: the sync reports them and keeps the stored history id,
            // so the same changes are read again next cycle.
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GmailHistoryResponse>(json, JsonOptions);
            mailboxHistoryId = result?.HistoryId ?? mailboxHistoryId;

            var records = result?.History ?? [];
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];

                foreach (var added in record.MessagesAdded ?? [])
                {
                    var id = added.Message?.Id;
                    if (id is null || !seen.Add(id)) continue;
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var msg = await GetSingleMessageAsync(token, id, folderId, cancellationToken)
                            .ConfigureAwait(false);
                        if (msg is not null)
                            messages.Add(msg);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Error(ex, "Failed to get Gmail message {MessageId} from history", id);
                    }
                }

                lastRecordId = record.Id ?? lastRecordId;

                var moreToRead = i < records.Count - 1 || result?.NextPageToken is not null;
                if (messages.Count >= maxResults && moreToRead && lastRecordId is not null)
                {
                    // The per-sync cap is reached with changes left: resume after the last
                    // record read instead of jumping to the mailbox's current id, which would
                    // skip the rest.
                    return (messages, lastRecordId);
                }
            }

            pageToken = result?.NextPageToken;
        } while (pageToken is not null);

        return (messages, mailboxHistoryId ?? lastRecordId ?? startHistoryId);
    }

    private async Task<EmailMessage?> GetSingleMessageAsync(
        string token, string messageId, string folderId, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}/messages/{Uri.EscapeDataString(messageId)}?format=full";

        using var response = await SendAsync(url, token, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var gmailMsg = JsonSerializer.Deserialize<GmailMessage>(json, JsonOptions);

        return gmailMsg is null ? null : MapMessage(gmailMsg, messageId, folderId, ProviderId);
    }

    /// <summary>
    /// Maps a Gmail API message (format=full) to an <see cref="EmailMessage"/>.
    /// </summary>
    private static EmailMessage MapMessage(GmailMessage gmailMsg, string messageId, string folderId, string providerId)
    {
        // Header names are case-insensitive (RFC 5322); senders do not all use one casing.
        var headers = gmailMsg.Payload?.Headers ?? [];
        string? Header(string name) =>
            headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

        var subject = Header("Subject") ?? "(No Subject)";
        var from = Header("From") ?? "";
        var toRaw = Header("To") ?? "";
        var dateRaw = Header("Date") ?? "";

        // Extract body text.
        var bodyText = ExtractBodyText(gmailMsg.Payload);
        var bodyHtml = ExtractBodyHtml(gmailMsg.Payload);

        return new EmailMessage
        {
            Id = gmailMsg.Id ?? messageId,
            Subject = subject,
            BodyPreview = bodyText.Length > 300 ? bodyText[..300] : bodyText,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            From = ParseContact(from),
            To = ParseAddressList(toRaw),
            ReceivedAt = ParseMessageDate(gmailMsg.InternalDate, dateRaw),
            IsRead = !(gmailMsg.LabelIds?.Contains("UNREAD") ?? false),
            IsStarred = gmailMsg.LabelIds?.Contains("STARRED") ?? false,
            HasAttachments = gmailMsg.Payload?.Parts?.Any(p => p.Filename is { Length: > 0 }) ?? false,
            ThreadId = gmailMsg.ThreadId ?? "",
            SourceProvider = providerId,
            AttachmentNames = gmailMsg.Payload?.Parts?
                .Where(p => !string.IsNullOrEmpty(p.Filename))
                .Select(p => p.Filename!)
                .ToList() ?? [],
            FolderName = gmailMsg.LabelIds?.FirstOrDefault() ?? "",
            FolderId = folderId,
            WebLink = $"https://mail.google.com/mail/u/0/#inbox/{gmailMsg.Id}",
        };
    }

    /// <summary>
    /// When the message was received, in UTC. Gmail's <c>internalDate</c> (epoch milliseconds,
    /// set when Google accepted the message) is preferred; the Date header is the fallback.
    /// </summary>
    internal static DateTime ParseMessageDate(string? internalDate, string? dateHeader)
    {
        if (long.TryParse(internalDate, NumberStyles.None, CultureInfo.InvariantCulture, out var epochMs)
            && epochMs is > 0 and <= MaxEpochMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
        }

        if (!string.IsNullOrWhiteSpace(dateHeader))
        {
            // "Tue, 14 Apr 2026 09:30:00 +0000 (UTC)": the trailing comment is valid RFC 5322
            // but not something DateTimeOffset parses.
            var cleaned = TrailingCommentRx.Replace(dateHeader, string.Empty).Trim();
            if (DateTimeOffset.TryParse(
                    cleaned,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return parsed.UtcDateTime;
            }
        }

        return DateTime.MinValue;
    }

    /// <summary>9999-12-31T23:59:59.999Z, the largest value DateTimeOffset accepts.</summary>
    private const long MaxEpochMilliseconds = 253_402_300_799_999;

    private static readonly Regex TrailingCommentRx = new(@"\s*\([^()]*\)\s*$", RegexOptions.Compiled);

    private static string ExtractBodyText(GmailMessagePart? part) => ExtractBody(part, "text/plain");

    private static string ExtractBodyHtml(GmailMessagePart? part) => ExtractBody(part, "text/html");

    private static string ExtractBody(GmailMessagePart? part, string mimeType)
    {
        if (part is null) return "";
        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase) && part.Body?.Data is not null)
        {
            var contentType = part.Headers?
                .FirstOrDefault(h => string.Equals(h.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
            return DecodeBase64Url(part.Body.Data, ResolveCharset(contentType));
        }

        if (part.Parts is not null)
        {
            foreach (var sub in part.Parts)
            {
                var text = ExtractBody(sub, mimeType);
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }

        return "";
    }

    /// <summary>
    /// The encoding named by the charset parameter of a Content-Type header. Gmail returns
    /// each part's bytes as sent, so a windows-1252 or ISO-8859-x body read as UTF-8 turns
    /// every accented letter into a replacement character. Unknown or missing charsets fall
    /// back to UTF-8, as does us-ascii (a subset that mislabelled 8-bit mail often claims).
    /// </summary>
    internal static Encoding ResolveCharset(string? contentType)
    {
        var charset = ExtractCharset(contentType);
        if (charset is null
            || charset.Equals("us-ascii", StringComparison.OrdinalIgnoreCase)
            || charset.Equals("ascii", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8;
        }

        try
        {
            // Legacy code pages (windows-125x, ISO-8859-x, Shift_JIS, GB2312...) come from the
            // code pages provider, which is part of the shared framework but not registered by
            // default; UTF-8/16/32 and Latin-1 are built in.
            return CodePagesEncodingProvider.Instance.GetEncoding(charset) ?? Encoding.GetEncoding(charset);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    private static string? ExtractCharset(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;

        foreach (var parameter in contentType.Split(';').Skip(1))
        {
            var eq = parameter.IndexOf('=');
            if (eq < 0) continue;
            if (!parameter[..eq].Trim().Equals("charset", StringComparison.OrdinalIgnoreCase)) continue;

            var value = parameter[(eq + 1)..].Trim().Trim('"', '\'');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static string DecodeBase64Url(string base64Url, Encoding encoding)
    {
        try
        {
            var padded = base64Url.PadRight((base64Url.Length + 3) / 4 * 4, '=')
                .Replace('-', '+').Replace('_', '/');
            return encoding.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return base64Url;
        }
    }

    /// <summary>
    /// Splits an address-list header (To, Cc) into contacts. Commas (and the semicolons some
    /// clients use) separate addresses only outside a quoted display name or angle brackets,
    /// so <c>"Doe, Jane" &lt;jane@example.com&gt;</c> stays one contact.
    /// </summary>
    internal static List<EmailContact> ParseAddressList(string? raw)
    {
        var contacts = new List<EmailContact>();
        if (string.IsNullOrWhiteSpace(raw)) return contacts;

        var start = 0;
        var inQuotes = false;
        var angleDepth = 0;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inQuotes)
            {
                if (c == '\\') i++; // an escaped character inside quotes
                else if (c == '"') inQuotes = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case '<':
                    angleDepth++;
                    break;
                case '>' when angleDepth > 0:
                    angleDepth--;
                    break;
                case ',' or ';' when angleDepth == 0:
                    Add(raw[start..i]);
                    start = i + 1;
                    break;
            }
        }

        Add(raw[start..]);
        return contacts;

        void Add(string piece)
        {
            if (!string.IsNullOrWhiteSpace(piece))
                contacts.Add(ParseContact(piece));
        }
    }

    internal static EmailContact ParseContact(string raw)
    {
        // Parse "Display Name <email@example.com>" or just "email@example.com". The address
        // is the last bracketed part; a display name may itself contain '<'.
        var trimmed = raw.Trim();
        var ltIdx = trimmed.LastIndexOf('<');
        var gtIdx = trimmed.LastIndexOf('>');

        if (ltIdx >= 0 && gtIdx > ltIdx)
        {
            var name = trimmed[..ltIdx].Trim();
            if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
                name = name[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");

            return new EmailContact
            {
                DisplayName = name,
                EmailAddress = trimmed[(ltIdx + 1)..gtIdx].Trim(),
            };
        }

        return new EmailContact { EmailAddress = trimmed };
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    private sealed class GmailProfileResponse
    {
        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }
    }

    private sealed class GmailMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<GmailMessageId> Messages { get; init; } = [];
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; init; }
        [JsonPropertyName("resultSizeEstimate")]
        public int ResultSizeEstimate { get; init; }
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
        [JsonPropertyName("internalDate")]
        public string? InternalDate { get; init; }
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
        [JsonPropertyName("id")]
        public string? Id { get; init; }
        [JsonPropertyName("messagesAdded")]
        public List<GmailHistoryMessageAdded>? MessagesAdded { get; init; }
    }

    private sealed class GmailHistoryMessageAdded
    {
        [JsonPropertyName("message")]
        public GmailMessageId? Message { get; init; }
    }
}
