using System.Reflection;
using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Plugins.Email.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Email;

/// <summary>
/// Covers the email triage category: the rule set that assigns an
/// <see cref="EmailCategory"/> to an incoming message, the persistence of that
/// category into <see cref="InboxItemEntity.SourceCategory"/>, and the display
/// label the Operations page derives from it.
///
/// Before this suite the enum existed but nothing assigned or read it, and every
/// email landed in the inbox tagged with the constant "email_message".
/// </summary>
public sealed class EmailCategoryClassificationTests
{
    private readonly EmailTriageProcessor _processor = new(new LoggerConfiguration().CreateLogger());

    // ── Rule coverage: one test per category ─────────────────────────────────

    [Fact]
    public void Classify_SubjectAsksForAction_ReturnsActionRequired()
    {
        var msg = Message(subject: "Action required: approve the Q3 budget");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_PastDueBody_ReturnsActionRequired()
    {
        var msg = Message(subject: "Your account", bodyText: "The balance is past due.");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_CalendarAttachment_ReturnsMeeting()
    {
        var msg = Message(subject: "Sync up", attachments: ["agenda.ics"]);

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Meeting);
    }

    [Fact]
    public void Classify_InvitationSubject_ReturnsMeeting()
    {
        var msg = Message(subject: "Invitation: Design review @ Thu Sep 4");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Meeting);
    }

    [Fact]
    public void Classify_InvoiceSubject_ReturnsFinancial()
    {
        var msg = Message(subject: "Invoice 4471 from Contoso");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Financial);
    }

    [Fact]
    public void Classify_ReceiptBody_ReturnsFinancial()
    {
        var msg = Message(subject: "Thanks", bodyText: "Here is your receipt for order 88.");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Financial);
    }

    [Fact]
    public void Classify_SocialNetworkSender_ReturnsSocial()
    {
        var msg = Message(subject: "You have 3 new connections", from: "notify@linkedin.com");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Social);
    }

    [Fact]
    public void Classify_DiscountSubject_ReturnsPromotion()
    {
        var msg = Message(subject: "40% off everything this weekend");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Promotion);
    }

    [Fact]
    public void Classify_UnsubscribeFooter_ReturnsNewsletter()
    {
        var msg = Message(
            subject: "The Monday Read",
            bodyText: "Stories for you.\n\nTo stop receiving these, unsubscribe here.");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Newsletter);
    }

    [Fact]
    public void Classify_NoReplySender_ReturnsNotification()
    {
        var msg = Message(subject: "Build 4412 finished", from: "no-reply@ci.example.com");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Notification);
    }

    [Fact]
    public void Classify_PlainPersonalMessage_ReturnsOther()
    {
        var msg = Message(subject: "Lunch tomorrow?", bodyText: "Are you free around one?");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Other);
    }

    // ── Precedence: the order the rules are applied in is part of the contract ─

    [Fact]
    public void Classify_ActionRequiredWinsOverPromotion()
    {
        var msg = Message(subject: "Action required: claim your 20% discount");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_FinancialWinsOverNewsletter()
    {
        var msg = Message(
            subject: "Your invoice is ready",
            bodyText: "Statement attached. Click to unsubscribe from billing emails.");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Financial);
    }

    [Fact]
    public void Classify_PromotionWinsOverNotification()
    {
        var msg = Message(subject: "Half price today only", from: "noreply@shop.example.com");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Promotion);
    }

    // ── Precision: keyword matching must respect word boundaries ─────────────

    [Fact]
    public void Classify_SaleInsideLongerWord_DoesNotMatchPromotion()
    {
        var msg = Message(subject: "Salesforce export finished", bodyText: "Wholesale data attached.");

        EmailTriageProcessor.Classify(msg).Should().NotBe(EmailCategory.Promotion);
    }

    [Fact]
    public void Classify_DealInsideLongerWord_DoesNotMatchPromotion()
    {
        var msg = Message(subject: "Idealized model results", bodyText: "See the dealt hand below.");

        EmailTriageProcessor.Classify(msg).Should().NotBe(EmailCategory.Promotion);
    }

    [Theory]
    [InlineData("Could you please\nreview the figures?")]      // wrapped mid-phrase
    [InlineData("Could you please  review the figures?")]      // collapsed markup gap
    [InlineData("Could you please\treview the figures?")]      // tab
    public void Classify_PhraseSplitByAnyWhitespace_StillMatches(string bodyText)
    {
        var msg = Message(subject: "Q3 numbers", bodyText: bodyText);

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_IsCaseInsensitive()
    {
        var msg = Message(subject: "ACTION REQUIRED: sign the form");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_HtmlOnlyBody_ReadsThroughTheMarkup()
    {
        // The only signal is "please review", and it is split across two table cells. If
        // tags were deleted rather than replaced by a space the words fuse into
        // "pleasereview", no rule matches, and the message is filed as Other. The subject
        // is deliberately neutral so nothing else can satisfy this assertion.
        var msg = Message(
            subject: "Q3 numbers",
            bodyText: "",
            bodyHtml: "<table><tr><td>please</td><td>review the attached figures.</td></tr></table>");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_HtmlAttributes_DoNotCountAsBodyText()
    {
        // "unsubscribe" appears only inside an href, never as text the reader sees.
        var msg = Message(
            subject: "Q3 numbers",
            bodyText: "",
            bodyHtml: "<p>Figures attached.</p><a href=\"https://x.test/unsubscribe\">Manage</a>");

        EmailTriageProcessor.Classify(msg).Should().NotBe(EmailCategory.Newsletter);
    }

    [Fact]
    public void Classify_SubdomainOfASocialNetwork_StillCountsAsSocial()
    {
        var msg = Message(subject: "Weekly summary", from: "notify@mail.linkedin.com");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Social);
    }

    [Fact]
    public void Classify_LookalikeDomain_IsNotSocial()
    {
        // "notlinkedin.com" ends with the string but is a different domain; only an exact
        // match or a dot-delimited subdomain may count.
        var msg = Message(subject: "Weekly summary", from: "notify@notlinkedin.com");

        EmailTriageProcessor.Classify(msg).Should().NotBe(EmailCategory.Social);
    }

    [Fact]
    public void Classify_TaggedNoReplyAddress_StillCountsAsNotification()
    {
        var msg = Message(subject: "Build finished", from: "noreply+ci@example.com");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Notification);
    }

    [Fact]
    public void Classify_NullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EmailTriageProcessor.Classify(null!));
    }

    [Fact]
    public void Classify_MissingSender_ClassifiesInsteadOfThrowing()
    {
        // These DTOs are filled by deserializing provider JSON, which does not honour the
        // non-nullable annotations, so a null sender is reachable at runtime.
        var msg = new EmailMessage { Subject = "Action required: sign the form", From = null! };

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.ActionRequired);
    }

    [Fact]
    public void Classify_SenderWithNoAtSign_IsNeitherSocialNorNotification()
    {
        var msg = Message(subject: "Hello there friend", from: "mailer-daemon");

        EmailTriageProcessor.Classify(msg).Should().Be(EmailCategory.Other);
    }

    // ── Wiring: the category reaches the inbox row ───────────────────────────

    [Fact]
    public void ConvertToInboxParameters_SourceCategory_IsTheClassifiedCategoryName()
    {
        var msg = Message(subject: "Action required: renew the certificate");

        var (_, _, _, _, _, sourceCategory, _, _, _) = _processor.ConvertToInboxParameters(msg);

        sourceCategory.Should().Be(nameof(EmailCategory.ActionRequired));
    }

    [Fact]
    public void ConvertToInboxParameters_UnremarkableMail_TagsItOther()
    {
        var msg = Message(subject: "Lunch tomorrow?", bodyText: "Are you free around one?");

        var (_, _, _, _, _, sourceCategory, _, _, _) = _processor.ConvertToInboxParameters(msg);

        sourceCategory.Should().Be(nameof(EmailCategory.Other));
    }

    [Fact]
    public void EveryCategoryName_FitsTheSourceCategoryColumn()
    {
        // InboxItemEntity.SourceCategory is [MaxLength(50)]; a longer name would be
        // truncated or rejected on save.
        var maxLength = typeof(InboxItemEntity)
            .GetProperty(nameof(InboxItemEntity.SourceCategory))!
            .GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>()!
            .Length;

        foreach (var name in Enum.GetNames<EmailCategory>())
        {
            name.Length.Should().BeLessThanOrEqualTo(maxLength, $"'{name}' must fit the column");
        }
    }

    [Fact]
    public void EmailCategory_Other_IsTheDefaultValue()
    {
        default(EmailCategory).Should().Be(EmailCategory.Other);
    }

    // ── Wiring: the Operations page renders the category ─────────────────────

    [Theory]
    [InlineData(EmailCategory.ActionRequired, "Action Required")]
    [InlineData(EmailCategory.Newsletter, "Newsletter")]
    [InlineData(EmailCategory.Financial, "Financial")]
    public void OperationsInboxLabel_RendersTheStoredCategory(EmailCategory category, string expected)
    {
        var item = new InboxItemEntity
        {
            SourcePluginId = "com.agentx.email",
            SourceCategory = category.ToString(),
            SourceType = "email-connector",
        };

        BuildInboxSourceLabel(item).Should().Be(expected);
    }

    /// <summary>
    /// <c>OperationsOverviewService.BuildInboxSourceLabel</c> is a private static
    /// helper on the live service; reflection lets the test assert the rendered
    /// label without standing up the whole overview pipeline.
    /// </summary>
    private static string BuildInboxSourceLabel(InboxItemEntity item)
    {
        var method = typeof(OperationsOverviewService).GetMethod(
            "BuildInboxSourceLabel",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("OperationsOverviewService must still derive the inbox source label");
        return (string)method!.Invoke(null, [item])!;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EmailMessage Message(
        string subject,
        string bodyText = "Body.",
        string bodyHtml = "",
        string from = "alice@example.com",
        IEnumerable<string>? attachments = null)
    {
        var names = attachments?.ToList() ?? [];
        return new EmailMessage
        {
            Id = "msg-1",
            Subject = subject,
            BodyPreview = bodyText.Length > 100 ? bodyText[..100] : bodyText,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            From = new EmailContact { DisplayName = "Alice", EmailAddress = from },
            To = [new() { EmailAddress = "bob@example.com" }],
            ReceivedAt = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            FolderId = "INBOX",
            FolderName = "INBOX",
            SourceProvider = "google",
            HasAttachments = names.Count > 0,
            AttachmentNames = names,
        };
    }
}
