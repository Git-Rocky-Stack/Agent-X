using System.Globalization;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Email;

/// <summary>
/// Unit tests for Email connector models, EmailTriageProcessor,
/// EmailPlugin lifecycle, and EmailProvider construction.
/// </summary>
public sealed class EmailModelsTests
{
    // -- EmailMessage ---------------------------------------------------------

    [Fact]
    public void EmailMessage_Defaults_AreSet()
    {
        var msg = new EmailMessage();
        msg.Id.Should().BeEmpty();
        msg.Subject.Should().BeEmpty();
        msg.BodyPreview.Should().BeEmpty();
        msg.BodyHtml.Should().BeEmpty();
        msg.BodyText.Should().BeEmpty();
        msg.From.Should().NotBeNull();
        msg.To.Should().BeEmpty();
        msg.Cc.Should().BeEmpty();
        msg.Bcc.Should().BeEmpty();
        msg.IsRead.Should().BeFalse();
        msg.IsStarred.Should().BeFalse();
        msg.HasAttachments.Should().BeFalse();
        msg.FolderName.Should().BeEmpty();
        msg.FolderId.Should().BeEmpty();
        msg.ThreadId.Should().BeEmpty();
        msg.SourceProvider.Should().BeEmpty();
        msg.AttachmentNames.Should().BeEmpty();
        msg.WebLink.Should().BeNull();
    }

    [Fact]
    public void EmailMessage_Init_SetsProperties()
    {
        var msg = new EmailMessage
        {
            Id = "msg-1",
            Subject = "Test Email",
            BodyPreview = "Preview text",
            BodyText = "Full body",
            ReceivedAt = DateTime.UtcNow,
            IsRead = true,
            SourceProvider = "google",
            From = new EmailContact { DisplayName = "Alice", EmailAddress = "alice@test.com" },
            To = [new() { DisplayName = "Bob", EmailAddress = "bob@test.com" }],
        };

        msg.Id.Should().Be("msg-1");
        msg.Subject.Should().Be("Test Email");
        msg.From.DisplayName.Should().Be("Alice");
        msg.To.Should().HaveCount(1);
    }

    // -- EmailContact --------------------------------------------------------

    [Fact]
    public void EmailContact_Defaults_AreSet()
    {
        var contact = new EmailContact();
        contact.DisplayName.Should().BeEmpty();
        contact.EmailAddress.Should().BeEmpty();
        contact.IsMe.Should().BeFalse();
    }

    // -- EmailFolderInfo ------------------------------------------------------

    [Fact]
    public void EmailFolderInfo_Defaults_AreSet()
    {
        var folder = new EmailFolderInfo();
        folder.Id.Should().BeEmpty();
        folder.Name.Should().BeEmpty();
        folder.TotalCount.Should().Be(0);
        folder.UnreadCount.Should().Be(0);
        folder.SourceProvider.Should().BeEmpty();
    }

    // -- EmailSyncSettings ----------------------------------------------------

    [Fact]
    public void EmailSyncSettings_Defaults_AreSet()
    {
        var settings = new EmailSyncSettings();
        settings.SyncIntervalMinutes.Should().Be(10);
        settings.MaxMessagesPerSync.Should().Be(50);
        settings.SyncDaysBack.Should().Be(30);
        settings.EnableAiCategorization.Should().BeTrue();
        settings.CategorizationPrompt.Should().BeNull();
        settings.IncludeHtmlBody.Should().BeTrue();
        settings.IncludeAttachmentNames.Should().BeTrue();
        settings.EnabledFolders.Should().ContainKey("INBOX");
        settings.EnabledFolders["INBOX"].Should().BeTrue();
    }

    [Fact]
    public void EmailSyncSettings_RoundTrip_PreservesValues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agentx-email-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = new EmailSyncSettings
            {
                SyncIntervalMinutes = 20,
                MaxMessagesPerSync = 100,
                SyncDaysBack = 60,
                EnableAiCategorization = false,
                CategorizationPrompt = "Custom prompt",
                IncludeHtmlBody = true,
                IncludeAttachmentNames = false,
            };
            settings.EnabledFolders["SENT"] = true;

            var path = Path.Combine(tempDir, "email-sync-settings.json");
            settings.Save(path);

            var loaded = EmailSyncSettings.Load(path);
            loaded.SyncIntervalMinutes.Should().Be(20);
            loaded.MaxMessagesPerSync.Should().Be(100);
            loaded.SyncDaysBack.Should().Be(60);
            loaded.EnableAiCategorization.Should().BeFalse();
            loaded.CategorizationPrompt.Should().Be("Custom prompt");
            loaded.IncludeHtmlBody.Should().BeTrue();
            loaded.IncludeAttachmentNames.Should().BeFalse();
            loaded.EnabledFolders.Should().ContainKey("SENT");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmailSyncSettings_IncludeHtmlBody_RoundTripsUnderItsNewKey(bool value)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agentx-email-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "email-sync-settings.json");
            new EmailSyncSettings { IncludeHtmlBody = value }.Save(path);

            File.ReadAllText(path).Should().Contain("\"includeHtmlBodyText\"").And.NotContain("\"includeHtmlBody\"");
            EmailSyncSettings.Load(path).IncludeHtmlBody.Should().Be(value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EmailSyncSettings_Load_IgnoresTheOldKeyWrittenWhileTheOptionDidNothing()
    {
        // Every settings file saved before carries "includeHtmlBody": false, the old default of
        // an option nothing read. It must not switch HTML-only bodies off now that it is applied.
        var tempDir = Path.Combine(Path.GetTempPath(), $"agentx-email-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "email-sync-settings.json");
            File.WriteAllText(path, "{ \"syncIntervalMinutes\": 20, \"includeHtmlBody\": false }");

            var loaded = EmailSyncSettings.Load(path);

            loaded.SyncIntervalMinutes.Should().Be(20);
            loaded.IncludeHtmlBody.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EmailSyncSettings_Load_NonExistentPath_ReturnsDefaults()
    {
        var settings = EmailSyncSettings.Load("/non/existent/path.json");
        settings.SyncIntervalMinutes.Should().Be(10);
    }

    [Fact]
    public void EmailSyncSettings_Load_CorruptFile_GivesDefaults_AndKeepsTheFileAside()
    {
        // A corrupt file used to throw out of Load, aborting connector startup (and the
        // calendar connector initialized after it).
        var tempDir = Path.Combine(Path.GetTempPath(), $"agentx-email-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "email-sync-settings.json");
            File.WriteAllText(path, """{ "syncIntervalMinutes": 20, "enabledFolders": """);

            var settings = EmailSyncSettings.Load(path);

            settings.SyncIntervalMinutes.Should().Be(10);
            settings.EnabledFolders.Should().ContainKey("INBOX");
            File.Exists(path + ".corrupt").Should().BeTrue("the broken file is kept for inspection");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EmailSyncSettings_Save_ReplacesTheFileAtomically()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agentx-email-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "email-sync-settings.json");
            new EmailSyncSettings { SyncIntervalMinutes = 15 }.Save(path);
            new EmailSyncSettings { SyncIntervalMinutes = 30 }.Save(path);

            EmailSyncSettings.Load(path).SyncIntervalMinutes.Should().Be(30);
            Directory.GetFiles(tempDir).Select(Path.GetFileName)
                .Should().Equal("email-sync-settings.json"); // no temporary file left behind
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

/// <summary>
/// Unit tests for the EmailTriageProcessor.
/// </summary>
public sealed class EmailTriageProcessorTests
{
    private readonly EmailTriageProcessor _processor = new(new LoggerConfiguration().CreateLogger());

    [Fact]
    public void ConvertToInboxParameters_SetsCorrectPluginId()
    {
        var msg = CreateSampleMessage();
        var (fileName, fileType, sourceType, sourceUrl, sourcePluginId, sourceCategory,
             externalId, contentPreview, contentText) = _processor.ConvertToInboxParameters(msg);

        sourcePluginId.Should().Be("com.agentx.email");
        sourceCategory.Should().Be(nameof(EmailCategory.Other));
        sourceType.Should().Be("email-connector");
        fileType.Should().Be("EmailMessage");
    }

    [Fact]
    public void ConvertToInboxParameters_ExternalId_ContainsProviderFolderId()
    {
        var msg = CreateSampleMessage();
        var (_, _, _, _, _, _, externalId, _, _) = _processor.ConvertToInboxParameters(msg);

        externalId.Should().Be("google:INBOX:msg-1");
    }

    [Fact]
    public void ConvertToInboxParameters_FileName_ContainsSubject()
    {
        var msg = CreateSampleMessage(subject: "Urgent: Review Needed");
        var (fileName, _, _, _, _, _, _, _, _) = _processor.ConvertToInboxParameters(msg);

        fileName.Should().Contain("Urgent: Review Needed");
    }

    [Fact]
    public void ExtractSearchableContent_IncludesSubject()
    {
        var msg = CreateSampleMessage(subject: "Project Update");
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("Subject: Project Update");
    }

    [Fact]
    public void ExtractSearchableContent_IncludesFrom()
    {
        var msg = CreateSampleMessage();
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("From: Alice <alice@example.com>");
    }

    [Fact]
    public void ExtractSearchableContent_IncludesToRecipients()
    {
        var msg = CreateSampleMessage();
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("To:");
        content.Should().Contain("bob@example.com");
    }

    [Fact]
    public void ExtractSearchableContent_IncludesFlags()
    {
        var msg = CreateSampleMessage(isStarred: true, hasAttachments: true);
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("Starred");
        content.Should().Contain("HasAttachments");
    }

    [Fact]
    public void ExtractSearchableContent_IncludesAttachments()
    {
        var msg = CreateSampleMessage();
        msg.AttachmentNames.Add("report.pdf");
        msg.AttachmentNames.Add("data.xlsx");

        var content = _processor.ExtractSearchableContent(msg);
        content.Should().Contain("report.pdf");
        content.Should().Contain("data.xlsx");
    }

    [Fact]
    public void ExtractSearchableContent_IncludesBodyText()
    {
        var msg = CreateSampleMessage(bodyText: "This is the email body content.");
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("This is the email body content.");
    }

    [Fact]
    public void ExtractSearchableContent_FallsBackToHtml_WhenNoText()
    {
        var msg = CreateSampleMessage(bodyText: "", bodyHtml: "<p>Hello <b>World</b></p>");
        var content = _processor.ExtractSearchableContent(msg);

        content.Should().Contain("Hello World");
        content.Should().NotContain("<p>");
    }

    // IncludeHtmlBody was documented as applied, but nothing read it: an HTML-only message was
    // always indexed with its tags cut out character by character, which kept the CSS of <style>
    // blocks and the code of scripts as text and ran paragraphs together.

    private const string HtmlOnlyBody =
        "<html><head><title>Weekly roundup</title><style>.x { color: red; }</style></head>" +
        "<body><div style=\"display:none\">hidden preheader</div>" +
        "<p>Invoice&nbsp;#42 is &lt;due&gt; &amp; payable.</p><p>Thanks,<br>Dana</p>" +
        "<script>track();</script></body></html>";

    [Fact]
    public void ExtractSearchableContent_HtmlOnlyMessage_StoresItsHtmlAsReadableText()
    {
        var msg = CreateSampleMessage(bodyText: "", bodyHtml: HtmlOnlyBody);

        var content = _processor.ExtractSearchableContent(msg, new EmailSyncSettings { IncludeHtmlBody = true });

        content.Should().EndWith("Invoice #42 is <due> & payable.\n\nThanks,\nDana");
        content.Should().NotContain("color: red").And.NotContain("track()")
            .And.NotContain("hidden preheader").And.NotContain("Weekly roundup").And.NotContain("<p>");
    }

    [Fact]
    public void ExtractSearchableContent_HtmlOnlyMessage_WithIncludeHtmlBodyOff_LeavesTheBodyOut()
    {
        var msg = CreateSampleMessage(bodyText: "", bodyHtml: HtmlOnlyBody);

        var content = _processor.ExtractSearchableContent(msg, new EmailSyncSettings { IncludeHtmlBody = false });

        content.Should().NotContain("Invoice").And.NotContain("Dana");
        content.Should().Contain("Subject: Test Email");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtractSearchableContent_APlainTextPart_IsAlwaysStoredAndTheHtmlNever(bool includeHtmlBody)
    {
        var msg = CreateSampleMessage(bodyText: "Plain body", bodyHtml: "<p>Html body</p>");

        var content = _processor.ExtractSearchableContent(msg, new EmailSyncSettings { IncludeHtmlBody = includeHtmlBody });

        content.Should().Contain("Plain body").And.NotContain("Html body");
    }

    [Fact]
    public void ConvertToInboxParameters_HtmlOnlyMessage_TakesThePreviewFromTheReadableText()
    {
        // An HTML-only Gmail message has no plain-text preview, so the inbox showed none.
        var msg = CreateSampleMessage(bodyText: "", bodyHtml: HtmlOnlyBody);

        var (_, _, _, _, _, _, _, preview, contentText) =
            _processor.ConvertToInboxParameters(msg, new EmailSyncSettings());

        preview.Should().Be("Invoice #42 is <due> & payable.\n\nThanks,\nDana");
        contentText.Should().Contain("Invoice #42");
    }

    [Fact]
    public void ConvertToInboxParameters_HtmlOnlyMessage_WithIncludeHtmlBodyOff_HasNoBodyOrPreview()
    {
        var msg = CreateSampleMessage(bodyText: "", bodyHtml: HtmlOnlyBody);

        var (_, _, _, _, _, _, _, preview, contentText) =
            _processor.ConvertToInboxParameters(msg, new EmailSyncSettings { IncludeHtmlBody = false });

        preview.Should().BeEmpty();
        contentText.Should().NotContain("Invoice");
    }

    [Fact]
    public void ConvertToInboxParameters_ALongHtmlBody_GivesAPreviewOfThreeHundredCharacters()
    {
        var msg = CreateSampleMessage(bodyText: "", bodyHtml: "<p>" + new string('a', 1000) + "</p>");

        var (_, _, _, _, _, _, _, preview, _) = _processor.ConvertToInboxParameters(msg);

        preview.Should().HaveLength(300);
    }

    [Fact]
    public void ExtractSearchableContent_AttachmentNamesSettingOff_LeavesThemOut()
    {
        var msg = CreateSampleMessage(hasAttachments: true);
        msg.AttachmentNames.Add("salaries-2026.xlsx");
        var off = new EmailSyncSettings { IncludeAttachmentNames = false };

        var content = _processor.ExtractSearchableContent(msg, off);
        var (_, _, _, _, _, _, _, _, contentText) = _processor.ConvertToInboxParameters(msg, off);

        content.Should().NotContain("salaries-2026.xlsx");
        contentText.Should().NotContain("salaries-2026.xlsx");
        _processor.ExtractSearchableContent(msg, new EmailSyncSettings()).Should().Contain("salaries-2026.xlsx");
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void ExtractSearchableContent_DateIsGregorianIso_WhateverTheUserCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var msg = new EmailMessage
            {
                Id = "m1",
                Subject = "Hello",
                ReceivedAt = new DateTime(2026, 4, 15, 9, 30, 0, DateTimeKind.Utc),
                SourceProvider = "google",
            };

            _processor.ExtractSearchableContent(msg).Should().Contain("Date: 2026-04-15 09:30 UTC");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ConvertToInboxParameters_NullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _processor.ConvertToInboxParameters(null!));
    }


    [Fact]
    public void ExtractSearchableContent_NullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _processor.ExtractSearchableContent(null!));
    }

    // -- Helpers --------------------------------------------------------------

    private static EmailMessage CreateSampleMessage(
        string id = "msg-1",
        string subject = "Test Email",
        string bodyText = "Hello World",
        string bodyHtml = "",
        bool isStarred = false,
        bool hasAttachments = false)
    {
        return new EmailMessage
        {
            Id = id,
            Subject = subject,
            BodyPreview = bodyText.Length > 100 ? bodyText[..100] : bodyText,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            From = new EmailContact { DisplayName = "Alice", EmailAddress = "alice@example.com" },
            To = [new() { DisplayName = "Bob", EmailAddress = "bob@example.com" }],
            ReceivedAt = DateTime.UtcNow,
            IsRead = false,
            IsStarred = isStarred,
            HasAttachments = hasAttachments,
            FolderId = "INBOX",
            FolderName = "INBOX",
            ThreadId = "thread-1",
            SourceProvider = "google",
            WebLink = "https://mail.google.com/mail/u/0/#inbox/msg-1",
        };
    }
}
