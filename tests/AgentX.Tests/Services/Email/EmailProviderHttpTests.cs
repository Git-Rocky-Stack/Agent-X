using System.Net;
using System.Text;
using System.Text.Json;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Email;

/// <summary>
/// Request shape and response handling of <see cref="GmailProvider"/> and
/// <see cref="OutlookEmailProvider"/> against a stub HTTP handler: where incremental sync
/// starts, how the per-sync cap resumes, the Outlook inbox mapping, deleted-message entries,
/// and message parsing (dates, charsets, address lists).
/// </summary>
public sealed class EmailProviderHttpTests : IDisposable
{
    private readonly Mock<IOAuthService> _oauth = new();
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public EmailProviderHttpTests()
    {
        _oauth.Setup(o => o.GetAccessTokenAsync(It.IsAny<string>())).ReturnsAsync("access-token");
    }

    public void Dispose() => (_logger as IDisposable)?.Dispose();

    private static string B64Url(string text, Encoding? encoding = null) =>
        Convert.ToBase64String((encoding ?? Encoding.UTF8).GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string GmailMessageJson(
        string id,
        string body = "Hello",
        string charsetHeader = "text/plain; charset=UTF-8",
        Encoding? bodyEncoding = null,
        string to = "bob@example.com",
        string? internalDate = "1776245400000",
        string date = "Wed, 15 Apr 2026 09:30:00 +0000") =>
        JsonSerializer.Serialize(new
        {
            id,
            threadId = "t-" + id,
            labelIds = new[] { "INBOX", "UNREAD" },
            internalDate,
            payload = new
            {
                mimeType = "text/plain",
                headers = new[]
                {
                    new { name = "Subject", value = "Subject " + id },
                    new { name = "From", value = "Alice <alice@example.com>" },
                    new { name = "To", value = to },
                    new { name = "Date", value = date },
                    new { name = "Content-Type", value = charsetHeader },
                },
                body = new { data = B64Url(body, bodyEncoding) },
            },
        });

    // -- Gmail: where sync starts and resumes ------------------------------------

    [Fact]
    public async Task Gmail_FullSync_TakesTheHistoryIdFromTheProfile_AndHonorsDaysBack()
    {
        // messages.list carries no historyId, so incremental sync used to never start.
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/gmail/v1/users/me/profile" => StubHttpMessageHandler.Json("""{ "emailAddress": "me@example.com", "historyId": "9001" }"""),
            "/gmail/v1/users/me/messages" => StubHttpMessageHandler.Json("""{ "messages": [ { "id": "m1", "threadId": "t1" } ], "resultSizeEstimate": 1 }"""),
            "/gmail/v1/users/me/messages/m1" => StubHttpMessageHandler.Json(GmailMessageJson("m1")),
            _ => StubHttpMessageHandler.Status(HttpStatusCode.NotFound),
        });
        var provider = new GmailProvider(_oauth.Object, _logger, handler);
        var after = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc);

        var (messages, token) = await provider.GetMessagesAsync("INBOX", 50, deltaToken: null, receivedAfterUtc: after);

        token.Should().Be("9001");
        messages.Should().ContainSingle().Which.Id.Should().Be("m1");

        var requests = handler.Requests;
        requests[0].Uri.AbsolutePath.Should().Be("/gmail/v1/users/me/profile", "the history id is read before listing");
        var list = requests.Single(r => r.Uri.AbsolutePath == "/gmail/v1/users/me/messages");
        list.Query["labelIds"].Should().Be("INBOX");
        list.Query["q"].Should().Be($"after:{new DateTimeOffset(after).ToUnixTimeSeconds()}");
    }

    [Fact]
    public async Task Gmail_FullSyncOfAnEmptyFolder_StillReturnsASyncPosition()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/gmail/v1/users/me/profile" => StubHttpMessageHandler.Json("""{ "historyId": "77" }"""),
            _ => StubHttpMessageHandler.Json("""{ "resultSizeEstimate": 0 }"""),
        });
        var provider = new GmailProvider(_oauth.Object, _logger, handler);

        var (messages, token) = await provider.GetMessagesAsync("INBOX");

        messages.Should().BeEmpty();
        token.Should().Be("77");
    }

    [Fact]
    public async Task Gmail_History_ReadsEveryPage_AndReturnsTheMailboxPosition()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/gmail/v1/users/me/history")
            {
                return request.RequestUri.Query.Contains("pageToken=p2")
                    ? StubHttpMessageHandler.Json("""{ "history": [ { "id": "120", "messagesAdded": [ { "message": { "id": "m2" } } ] } ], "historyId": "130" }""")
                    : StubHttpMessageHandler.Json("""{ "history": [ { "id": "110", "messagesAdded": [ { "message": { "id": "m1" } }, { "message": { "id": "m1" } } ] } ], "historyId": "130", "nextPageToken": "p2" }""");
            }

            var id = path[(path.LastIndexOf('/') + 1)..];
            return StubHttpMessageHandler.Json(GmailMessageJson(id));
        });
        var provider = new GmailProvider(_oauth.Object, _logger, handler);

        var (messages, token) = await provider.GetMessagesAsync("INBOX", 50, deltaToken: "100");

        messages.Select(m => m.Id).Should().Equal("m1", "m2");
        token.Should().Be("130");
        handler.Requests.First().Query["startHistoryId"].Should().Be("100");
    }

    [Fact]
    public async Task Gmail_History_CapReached_ResumesAfterTheLastRecordRead()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/gmail/v1/users/me/history")
            {
                return StubHttpMessageHandler.Json("""
                    { "history": [
                        { "id": "110", "messagesAdded": [ { "message": { "id": "m1" } } ] },
                        { "id": "120", "messagesAdded": [ { "message": { "id": "m2" } } ] } ],
                      "historyId": "130" }
                    """);
            }

            var id = path[(path.LastIndexOf('/') + 1)..];
            return StubHttpMessageHandler.Json(GmailMessageJson(id));
        });
        var provider = new GmailProvider(_oauth.Object, _logger, handler);

        var (messages, token) = await provider.GetMessagesAsync("INBOX", maxResults: 1, deltaToken: "100");

        messages.Should().ContainSingle().Which.Id.Should().Be("m1");
        token.Should().Be("110", "jumping to the mailbox position would skip m2 for good");
    }

    [Fact]
    public async Task Gmail_ExpiredHistory_FallsBackToAFullSync()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/gmail/v1/users/me/history" => StubHttpMessageHandler.Status(HttpStatusCode.NotFound),
            "/gmail/v1/users/me/profile" => StubHttpMessageHandler.Json("""{ "historyId": "500" }"""),
            _ => StubHttpMessageHandler.Json("""{ "resultSizeEstimate": 0 }"""),
        });
        var provider = new GmailProvider(_oauth.Object, _logger, handler);

        var (_, token) = await provider.GetMessagesAsync("INBOX", 50, deltaToken: "1");

        token.Should().Be("500");
    }

    // -- Gmail: message parsing ------------------------------------------------------

    [Fact]
    public async Task Gmail_Message_UsesInternalDate_DecodesTheCharset_AndSplitsQuotedNames()
    {
        var latin1Body = "Café crème brûlée";
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/gmail/v1/users/me/profile" => StubHttpMessageHandler.Json("""{ "historyId": "1" }"""),
            "/gmail/v1/users/me/messages" => StubHttpMessageHandler.Json("""{ "messages": [ { "id": "m1" } ] }"""),
            _ => StubHttpMessageHandler.Json(GmailMessageJson(
                "m1",
                body: latin1Body,
                charsetHeader: "text/plain; charset=\"windows-1252\"",
                bodyEncoding: CodePagesEncoding(1252),
                to: "\"Doe, Jane\" <jane@example.com>, bob@example.com",
                internalDate: "1776245400000",
                date: "Wed, 15 Apr 2026 09:30:00 +0000 (UTC)")),
        });
        var provider = new GmailProvider(_oauth.Object, _logger, handler);

        var message = (await provider.GetMessagesAsync("INBOX")).Messages.Single();

        message.ReceivedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1776245400000).UtcDateTime);
        message.BodyText.Should().Be(latin1Body);
        message.To.Select(c => c.EmailAddress).Should().Equal("jane@example.com", "bob@example.com");
        message.To[0].DisplayName.Should().Be("Doe, Jane");
    }

    private static Encoding CodePagesEncoding(int codePage) =>
        CodePagesEncodingProvider.Instance.GetEncoding(codePage)!;

    [Theory]
    [InlineData("Wed, 15 Apr 2026 09:30:00 +0000 (UTC)", 9)]
    [InlineData("Wed, 15 Apr 2026 09:30:00 -0700 (PDT)", 16)]
    [InlineData("Wed, 15 Apr 2026 09:30:00 +0200", 7)]
    public void Gmail_DateHeader_WithOrWithoutAComment_ParsesToUtc(string header, int expectedUtcHour)
    {
        var value = GmailProvider.ParseMessageDate(internalDate: null, header);

        value.Should().Be(new DateTime(2026, 4, 15, expectedUtcHour, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Gmail_InternalDate_IsPreferredOverTheDateHeader()
    {
        var value = GmailProvider.ParseMessageDate("1776245400000", "Mon, 1 Jan 2001 00:00:00 +0000");

        value.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1776245400000).UtcDateTime);
    }

    [Theory]
    [InlineData(null, "utf-8")]
    [InlineData("text/plain", "utf-8")]
    [InlineData("text/plain; charset=us-ascii", "utf-8")]
    [InlineData("text/plain; charset=\"ISO-8859-1\"", "iso-8859-1")]
    [InlineData("text/html; format=flowed; charset=windows-1252", "windows-1252")]
    [InlineData("text/plain; charset=x-unknown-charset", "utf-8")]
    public void Gmail_ResolveCharset_ReadsTheContentTypeParameter(string? contentType, string expectedWebName)
    {
        GmailProvider.ResolveCharset(contentType).WebName.Should().Be(expectedWebName);
    }

    [Fact]
    public void Gmail_ParseAddressList_KeepsQuotedCommasAndSemicolonsInsideNames()
    {
        var contacts = GmailProvider.ParseAddressList(
            "\"Doe, Jane\" <jane@example.com>; \"O'Brien; Pat\" <pat@example.com>, plain@example.com");

        contacts.Select(c => (c.DisplayName, c.EmailAddress)).Should().Equal(
            ("Doe, Jane", "jane@example.com"),
            ("O'Brien; Pat", "pat@example.com"),
            (string.Empty, "plain@example.com"));
    }

    // -- Outlook ---------------------------------------------------------------------

    [Fact]
    public async Task Outlook_ListFolders_ReportsTheInboxAsINBOX()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/mailFolders/inbox", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("""{ "id": "AAMkInbox=" }""")
                : StubHttpMessageHandler.Json("""
                    { "value": [
                        { "id": "AAMkInbox=", "displayName": "Posteingang", "totalItemCount": 10, "unreadItemCount": 2 },
                        { "id": "AAMkArchive=", "displayName": "Archiv" } ] }
                    """));
        var provider = new OutlookEmailProvider(_oauth.Object, _logger, handler);

        var folders = await provider.ListFoldersAsync();

        folders.Select(f => f.Id).Should().Equal(IEmailProvider.InboxFolderId, "AAMkArchive=");
        folders[0].Name.Should().Be("Posteingang", "the display name stays the mailbox's own");
    }

    [Fact]
    public async Task Outlook_InboxDeltaRound_UsesTheWellKnownName_AndTheDaysBackFilter()
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json("""{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=d1" }"""));
        var provider = new OutlookEmailProvider(_oauth.Object, _logger, handler);
        var after = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc);

        var (_, token) = await provider.GetMessagesAsync(IEmailProvider.InboxFolderId, 50, deltaToken: null, receivedAfterUtc: after);

        token.Should().Be("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=d1");
        var request = handler.Requests.Single();
        request.Uri.AbsolutePath.Should().Be("/v1.0/me/mailFolders/inbox/messages/delta");
        request.Query["$filter"].Should().Be("receivedDateTime ge 2026-03-16T00:00:00Z");
    }

    [Fact]
    public async Task Outlook_RemovedEntries_AreSkipped_NotAParseFailure()
    {
        // Graph reports a deleted or moved message as an object, which the old string-typed
        // property could not deserialize: the whole page failed on every sync.
        var handler = StubHttpMessageHandler.Sequence(() => StubHttpMessageHandler.Json("""
            { "value": [
                { "id": "gone", "@removed": { "reason": "deleted" } },
                { "id": "m1", "subject": "Kept", "receivedDateTime": "2026-04-15T09:30:00Z",
                  "body": { "contentType": "text", "content": "Body" } } ],
              "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=d2" }
            """));
        var provider = new OutlookEmailProvider(_oauth.Object, _logger, handler);

        var (messages, _) = await provider.GetMessagesAsync("INBOX", 50, deltaToken: "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=d1");

        var message = messages.Should().ContainSingle().Subject;
        message.Id.Should().Be("m1");
        message.ReceivedAt.Should().Be(new DateTime(2026, 4, 15, 9, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Outlook_CapReachedMidRound_KeepsTheNextLinkToResumeFrom()
    {
        const string nextLink = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$skiptoken=s1";
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Json($$"""
                { "value": [ { "id": "m1", "subject": "One" }, { "id": "m2", "subject": "Two" } ],
                  "@odata.nextLink": "{{nextLink}}" }
                """),
            () => StubHttpMessageHandler.Json("""
                { "value": [ { "id": "m3", "subject": "Three" } ],
                  "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=d9" }
                """));
        var provider = new OutlookEmailProvider(_oauth.Object, _logger, handler);

        var (first, resumeToken) = await provider.GetMessagesAsync("INBOX", maxResults: 2);
        var (second, deltaToken) = await provider.GetMessagesAsync("INBOX", maxResults: 2, deltaToken: resumeToken);

        first.Select(m => m.Id).Should().Equal("m1", "m2");
        resumeToken.Should().Be(nextLink, "discarding the next link meant a large folder never finished its round");
        second.Select(m => m.Id).Should().Equal("m3");
        deltaToken.Should().EndWith("$deltatoken=d9");
        handler.Requests[1].Uri.AbsoluteUri.Should().Be(nextLink);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Outlook_RejectedStoredLink_StartsANewRound(HttpStatusCode rejection)
    {
        var handler = StubHttpMessageHandler.Sequence(
            () => StubHttpMessageHandler.Status(rejection),
            () => StubHttpMessageHandler.Json("""{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=fresh" }"""));
        var provider = new OutlookEmailProvider(_oauth.Object, _logger, handler);

        var (_, token) = await provider.GetMessagesAsync("INBOX", 50, deltaToken: "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=stale");

        token.Should().EndWith("$deltatoken=fresh");
        handler.Requests[1].Uri.AbsolutePath.Should().Be("/v1.0/me/mailFolders/inbox/messages/delta");
    }
}
