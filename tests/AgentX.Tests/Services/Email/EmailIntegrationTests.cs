using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Calendar.Models;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Email;

/// <summary>
/// Integration tests for the Email sync pipeline:
/// EmailSyncService → EmailTriageProcessor → IInboxService.TriageExternalAsync
/// and EmailPlugin lifecycle.
/// </summary>
public sealed class EmailIntegrationTests : IDisposable
{
    private readonly Mock<IInboxService> _inboxService;
    private readonly Mock<IOAuthService> _oauthService;
    private readonly Mock<IEmailProvider> _gmailProvider;
    private readonly Mock<IEmailProvider> _outlookProvider;
    private readonly EmailTriageProcessor _processor;
    private readonly EmailSyncService _syncService;
    private readonly string _tempDir;
    private readonly ILogger _logger;

    public EmailIntegrationTests()
    {
        _inboxService = new Mock<IInboxService>(MockBehavior.Strict);
        _oauthService = new Mock<IOAuthService>(MockBehavior.Loose);
        _gmailProvider = new Mock<IEmailProvider>(MockBehavior.Strict);
        _outlookProvider = new Mock<IEmailProvider>(MockBehavior.Strict);
        _logger = new LoggerConfiguration().CreateLogger();

        _processor = new EmailTriageProcessor(_logger);
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentx-email-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _syncService = new EmailSyncService(
            _inboxService.Object, _processor, _logger, _tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static EmailMessage CreateMessage(
        string id = "msg-1",
        string subject = "Sprint Review",
        string folderId = "INBOX",
        string sourceProvider = "google",
        bool isStarred = true,
        bool hasAttachments = true)
    {
        return new EmailMessage
        {
            Id = id,
            Subject = subject,
            BodyPreview = "Please review the sprint deliverables.",
            BodyText = "Please review the sprint deliverables before EOD.",
            From = new EmailContact { DisplayName = "Alice", EmailAddress = "alice@example.com" },
            To = [new() { DisplayName = "Bob", EmailAddress = "bob@example.com" }],
            Cc = [new() { DisplayName = "Charlie", EmailAddress = "charlie@example.com" }],
            ReceivedAt = DateTime.UtcNow.AddHours(-2),
            IsRead = false,
            IsStarred = isStarred,
            HasAttachments = hasAttachments,
            FolderId = folderId,
            FolderName = folderId,
            ThreadId = "thread-1",
            SourceProvider = sourceProvider,
            AttachmentNames = hasAttachments ? ["report.pdf"] : [],
            WebLink = $"https://mail.google.com/mail/u/0/#inbox/{id}",
        };
    }

    private static EmailSyncSettings DefaultSettings(params string[] enabledFolderIds)
    {
        var settings = new EmailSyncSettings();
        foreach (var id in enabledFolderIds)
            settings.EnabledFolders[id] = true;
        return settings;
    }

    private InboxItemEntity CreateInboxItem(long id = 1, DateTime? addedAt = null, DateTime? processedAt = null)
    {
        return new InboxItemEntity
        {
            Id = id,
            FilePath = $@"C:\Temp\AgentX\ExternalItems\com.agentx.email\email-{id}.txt",
            Status = "accepted",
            AddedAt = addedAt ?? DateTime.UtcNow,
            ProcessedAt = processedAt ?? DateTime.UtcNow,
            SourcePluginId = "com.agentx.email",
            SourceCategory = "email_message",
            ExternalId = $"google:INBOX:msg-{id}",
        };
    }

    private void SetupGmailProvider(params EmailMessage[] messages)
    {
        _gmailProvider.SetupGet(p => p.ProviderId).Returns("google");
        _gmailProvider
            .Setup(p => p.ListFoldersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailFolderInfo>
            {
                new() { Id = "INBOX", Name = "Inbox", TotalCount = 42, UnreadCount = 10, SourceProvider = "google" },
            });
        _gmailProvider
            .Setup(p => p.GetMessagesAsync("INBOX", It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((messages.ToList() as IReadOnlyList<EmailMessage>, (string?)"gmail-delta-1"));
    }

    private void SetupOutlookProvider(params EmailMessage[] messages)
    {
        _outlookProvider.SetupGet(p => p.ProviderId).Returns("microsoft");
        _outlookProvider
            .Setup(p => p.ListFoldersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailFolderInfo>
            {
                new() { Id = "AAMkAGI2AAA=", Name = "Inbox", TotalCount = 100, UnreadCount = 20, SourceProvider = "microsoft" },
            });
        _outlookProvider
            .Setup(p => p.GetMessagesAsync("AAMkAGI2AAA=", It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((messages.ToList() as IReadOnlyList<EmailMessage>, (string?)"ms-delta-1"));
    }

    // ── EmailSyncService integration tests ────────────────────────────────────

    [Fact]
    public async Task SyncAsync_SingleProvider_ProcessesAllEmailsThroughInbox()
    {
        var msg1 = CreateMessage("msg-1", "Sprint Review");
        var msg2 = CreateMessage("msg-2", "Action Item");
        SetupGmailProvider(msg1, msg2);

        _inboxService
            .Setup(i => i.TriageExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(CreateInboxItem());

        var settings = DefaultSettings("INBOX");

        var result = await _syncService.SyncAsync([_gmailProvider.Object], settings);

        result.Should().NotBeNull();
        result.ItemsFailed.Should().Be(0);
        result.IsSuccess.Should().BeTrue();

        _inboxService.Verify(i => i.TriageExternalAsync(
            It.IsAny<string>(), "EmailMessage", "email-connector",
            It.IsAny<string?>(), "com.agentx.email", "email_message",
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAsync_MultiProvider_AggregatesResults()
    {
        var gmailMsg = CreateMessage("g-1", "Gmail Thread", "INBOX", "google");
        var outlookMsg = CreateMessage("o-1", "Outlook Thread", "AAMkAGI2AAA=", "microsoft");
        SetupGmailProvider(gmailMsg);
        SetupOutlookProvider(outlookMsg);

        _inboxService
            .Setup(i => i.TriageExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(CreateInboxItem());

        var settings = DefaultSettings("INBOX", "AAMkAGI2AAA=");

        var result = await _syncService.SyncAsync(
            [_gmailProvider.Object, _outlookProvider.Object], settings);

        result.ItemsFailed.Should().Be(0);
        _inboxService.Verify(i => i.TriageExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAsync_NoEnabledFolders_SkipsProvider()
    {
        SetupGmailProvider(CreateMessage());
        var settings = new EmailSyncSettings();
        settings.EnabledFolders.Clear(); // no enabled folders

        var result = await _syncService.SyncAsync([_gmailProvider.Object], settings);

        result.ItemsAdded.Should().Be(0);
        result.ItemsFailed.Should().Be(0);
        _inboxService.Verify(i => i.TriageExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_DeltaTokens_PersistedAcrossSyncs()
    {
        SetupGmailProvider(CreateMessage());
        _inboxService
            .Setup(i => i.TriageExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(CreateInboxItem());

        var settings = DefaultSettings("INBOX");
        await _syncService.SyncAsync([_gmailProvider.Object], settings);

        var deltaPath = Path.Combine(_tempDir, "email-delta-tokens.json");
        File.Exists(deltaPath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(deltaPath);
        content.Should().Contain("google:INBOX");
        content.Should().Contain("gmail-delta-1");
    }

    [Fact]
    public async Task SyncAsync_ProviderError_ContinuesToNextProvider()
    {
        _gmailProvider.SetupGet(p => p.ProviderId).Returns("google");
        _gmailProvider
            .Setup(p => p.ListFoldersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API error"));

        SetupOutlookProvider(CreateMessage("o-1", "Outlook Email", "AAMkAGI2AAA=", "microsoft"));

        _inboxService
            .Setup(i => i.TriageExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(CreateInboxItem());

        var settings = DefaultSettings("INBOX", "AAMkAGI2AAA=");

        var result = await _syncService.SyncAsync(
            [_gmailProvider.Object, _outlookProvider.Object], settings);

        result.ItemsFailed.Should().Be(1); // Gmail provider failure
        _inboxService.Verify(i => i.TriageExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Once); // Outlook event
    }

    [Fact]
    public async Task SyncAsync_CancellationRequested_StopsProcessing()
    {
        SetupGmailProvider(CreateMessage());
        _inboxService
            .Setup(i => i.TriageExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(CreateInboxItem());

        var settings = DefaultSettings("INBOX");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _syncService.SyncAsync([_gmailProvider.Object], settings, cts.Token));
    }

    [Fact]
    public async Task SyncAsync_EmailProcessingError_ContinuesToNextEmail()
    {
        var msg1 = CreateMessage("msg-1", "First Email");
        var msg2 = CreateMessage("msg-2", "Second Email");
        SetupGmailProvider(msg1, msg2);

        var callCount = 0;
        _inboxService
            .Setup(i => i.TriageExternalAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) throw new InvalidOperationException("DB error");
                return CreateInboxItem(2);
            });

        var settings = DefaultSettings("INBOX");

        var result = await _syncService.SyncAsync([_gmailProvider.Object], settings);

        result.ItemsFailed.Should().Be(1);
        _inboxService.Verify(i => i.TriageExternalAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    // ── EmailTriageProcessor pipeline tests ────────────────────────────────────

    [Fact]
    public void Processor_ProducesCorrectExternalId_ForGmail()
    {
        var msg = CreateMessage("abc123", "Meeting", "INBOX", "google");
        var (fileName, fileType, sourceType, sourceUrl, sourcePluginId, sourceCategory,
             externalId, contentPreview, contentText) = _processor.ConvertToInboxParameters(msg);

        externalId.Should().Be("google:INBOX:abc123");
        sourcePluginId.Should().Be("com.agentx.email");
        sourceCategory.Should().Be("email_message");
        sourceType.Should().Be("email-connector");
        fileType.Should().Be("EmailMessage");
    }

    [Fact]
    public void Processor_ProducesCorrectExternalId_ForOutlook()
    {
        var msg = CreateMessage("xyz789", "Meeting", "folder-A", "microsoft");
        var (_, _, _, _, _, _, externalId, _, _) = _processor.ConvertToInboxParameters(msg);

        externalId.Should().Be("microsoft:folder-A:xyz789");
    }

    [Fact]
    public void Processor_ContentIncludesFromAndTo()
    {
        var msg = CreateMessage();
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("From: Alice <alice@example.com>");
        content.Should().Contain("To: Bob <bob@example.com>");
        content.Should().Contain("Cc: Charlie <charlie@example.com>");
    }

    [Fact]
    public void Processor_ContentIncludesFlags()
    {
        var msg = CreateMessage(isStarred: true, hasAttachments: true);
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("Starred");
        content.Should().Contain("HasAttachments");
    }

    // ── EmailPlugin integration tests ──────────────────────────────────────────

    [Fact]
    public async Task EmailPlugin_WithInboxService_ActivatesWithoutError()
    {
        var plugin = new EmailPlugin();
        var mockInbox = new Mock<IInboxService>(MockBehavior.Loose);

        var services = new ServiceCollection();
        services.AddSingleton(_oauthService.Object);
        services.AddSingleton(mockInbox.Object);

        var context = new Mock<IPluginContext>();
        context.SetupGet(c => c.Services).Returns(services.BuildServiceProvider());
        context.SetupGet(c => c.PluginDataPath).Returns(_tempDir);
        context.SetupGet(c => c.Logger).Returns(_logger);

        _oauthService.Setup(o => o.GetCredentialAsync("google")).ReturnsAsync((OAuthCredential?)null);
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft")).ReturnsAsync((OAuthCredential?)null);

        await plugin.InitializeAsync(context.Object);
        await plugin.ActivateAsync();

        await plugin.DeactivateAsync();
        plugin.Dispose();
    }

    [Fact]
    public async Task EmailPlugin_FetchOnlyFallback_NoInboxService()
    {
        var plugin = new EmailPlugin();
        var services = new ServiceCollection();
        services.AddSingleton(_oauthService.Object);
        // No IInboxService

        var context = new Mock<IPluginContext>();
        context.SetupGet(c => c.Services).Returns(services.BuildServiceProvider());
        context.SetupGet(c => c.PluginDataPath).Returns(_tempDir);
        context.SetupGet(c => c.Logger).Returns(_logger);

        _oauthService.Setup(o => o.GetCredentialAsync("google")).ReturnsAsync((OAuthCredential?)null);
        _oauthService.Setup(o => o.GetCredentialAsync("microsoft")).ReturnsAsync((OAuthCredential?)null);

        await plugin.InitializeAsync(context.Object);
        await plugin.ActivateAsync();

        await plugin.DeactivateAsync();
        plugin.Dispose();
    }

}