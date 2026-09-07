using System.Text.RegularExpressions;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins.Email.Models;
using Serilog;

namespace AgentX.Core.Services.Plugins.Email;

/// <summary>
/// Converts an <see cref="EmailMessage"/> into parameters suitable for
/// <see cref="IInboxService.TriageExternalAsync"/>, building a rich
/// searchable text representation for the knowledge vault and assigning the
/// <see cref="EmailCategory"/> the inbox row is filed under.
/// </summary>
public sealed class EmailTriageProcessor
{
    private const string PluginId = "com.agentx.email";
    private const string SourceType = "email-connector";

    private readonly ILogger _log;

    public EmailTriageProcessor(ILogger logger)
    {
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<EmailTriageProcessor>();
    }

    /// <summary>
    /// Converts an <see cref="EmailMessage"/> into the 10-parameter tuple
    /// expected by <see cref="IInboxService.TriageExternalAsync"/>.
    /// </summary>
    public (string FileName, string FileType, string SourceType, string? SourceUrl,
            string SourcePluginId, string? SourceCategory, string ExternalId,
            string? ContentPreview, string ContentText)
        ConvertToInboxParameters(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var fileName = $"Email: {message.Subject}";
        var fileType = "EmailMessage";
        var externalId = $"{message.SourceProvider}:{message.FolderId}:{message.Id}";
        var contentPreview = message.BodyPreview;
        var contentText = ExtractSearchableContent(message);
        var category = Classify(message).ToString();

        return (fileName, fileType, SourceType, message.WebLink,
                PluginId, category, externalId, contentPreview, contentText);
    }

    /// <summary>
    /// Assigns a triage category to <paramref name="message"/>.
    /// </summary>
    /// <remarks>
    /// The rules are ordered, and the first match wins. Urgency comes first because a
    /// message that asks the reader to act is the one triage exists to surface; the
    /// sender-based rules come last because an unattended address says the least about
    /// what a message is for. Keyword rules match whole words, so "Salesforce" is not a
    /// sale and "idealized" is not a deal.
    /// </remarks>
    public static EmailCategory Classify(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var text = BuildClassificationText(message);

        // From and EmailAddress are declared non-nullable, but these DTOs are populated by
        // deserializing provider JSON, and System.Text.Json does not enforce nullability.
        // A message with a missing sender must classify, not throw.
        var sender = message.From?.EmailAddress ?? string.Empty;

        if (ActionRx.IsMatch(text)) return EmailCategory.ActionRequired;
        if (HasCalendarAttachment(message) || MeetingRx.IsMatch(text)) return EmailCategory.Meeting;
        if (FinancialRx.IsMatch(text)) return EmailCategory.Financial;
        if (IsSocialSender(sender)) return EmailCategory.Social;
        if (PromotionRx.IsMatch(text) || PercentOffRx.IsMatch(text)) return EmailCategory.Promotion;
        if (NewsletterRx.IsMatch(text)) return EmailCategory.Newsletter;
        if (IsUnattendedSender(sender)) return EmailCategory.Notification;

        return EmailCategory.Other;
    }

    /// <summary>
    /// Subject plus body, lower-cased. When only an HTML body is available its tags are
    /// replaced by a space, so that text split across elements does not fuse into a single
    /// unmatchable word ("roundup</b><a>unsubscribe" must not become "roundupunsubscribe").
    /// </summary>
    private static string BuildClassificationText(EmailMessage message)
    {
        var hasPlainBody = !string.IsNullOrWhiteSpace(message.BodyText);
        var body = hasPlainBody ? message.BodyText : message.BodyHtml;
        var joined = $"{message.Subject}\n{message.BodyPreview}\n{body}";

        // Skip the regex pass on the common path. Bodies can be large and this runs once
        // per message in the sync loop, so a plain-text body is lower-cased and nothing more.
        return hasPlainBody
            ? joined.ToLowerInvariant()
            : TagRx.Replace(joined, " ").ToLowerInvariant();
    }

    private static bool HasCalendarAttachment(EmailMessage message) =>
        message.AttachmentNames.Any(
            name => name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase));

    private static bool IsSocialSender(string address) =>
        MatchesDomain(address, SocialDomains);

    /// <remarks>
    /// Split on the LAST '@' in both helpers: an address may legally carry one inside a
    /// quoted local part, and the domain is always what follows the final separator.
    /// </remarks>
    private static bool IsUnattendedSender(string address)
    {
        var at = address.LastIndexOf('@');
        if (at <= 0) return false;

        // "noreply+tracking@..." is the same unattended mailbox as "noreply@...".
        var local = address[..at].ToLowerInvariant();
        foreach (var part in UnattendedLocalParts)
        {
            if (local == part || local.StartsWith(part + "+", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool MatchesDomain(string address, IReadOnlyCollection<string> domains)
    {
        var at = address.LastIndexOf('@');
        if (at < 0 || at == address.Length - 1) return false;

        // Match the domain itself or any subdomain: "notify@mail.linkedin.com" is social.
        var domain = address[(at + 1)..].ToLowerInvariant();
        foreach (var d in domains)
        {
            if (domain == d || domain.EndsWith("." + d, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static readonly Regex TagRx = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// Builds a whole-word matcher for a set of phrases.
    /// </summary>
    /// <remarks>
    /// The space inside a multi-word phrase becomes <c>\s+</c>, because the gap between two
    /// words in a real message is rarely one space. A subject wraps ("please\nreview"), and
    /// stripping HTML leaves one space per tag, so "please&lt;/td&gt;&lt;td&gt;review"
    /// arrives as "please  review". A literal space would miss every one of those.
    /// </remarks>
    private static Regex Words(params string[] phrases) =>
        new(@"\b(?:" + string.Join("|", phrases.Select(p => p.Replace(" ", @"\s+"))) + @")\b",
            RegexOptions.Compiled);

    private static readonly Regex ActionRx = Words(
        "action required", "action needed", "response required", "reply required",
        "please review", "please approve", "please confirm", "please sign",
        "awaiting your", "requires your", "needs your approval", "approval required",
        "past due", "overdue", "final notice", "deadline", "expires today", "urgent");

    private static readonly Regex MeetingRx = Words(
        "invitation", "invite", "meeting", "calendar invite", "agenda",
        "reschedule", "rescheduled", "standup", "stand-up",
        "zoom link", "teams meeting", "google meet");

    private static readonly Regex FinancialRx = Words(
        "invoice", "receipt", "statement", "payment", "payout", "refund",
        "billing", "billed", "charged", "subscription renewal", "order confirmation",
        "wire transfer", "direct deposit", "tax");

    private static readonly Regex PromotionRx = Words(
        "sale", "deal", "deals", "discount", "coupon", "promo", "clearance", "bogo",
        "half price", "limited time", "special offer", "free shipping", "save big",
        "flash sale");

    /// <summary>"40% off", "40 % off", "40%off": a discount written as a number.</summary>
    private static readonly Regex PercentOffRx = new(@"\d+\s*%\s*off", RegexOptions.Compiled);

    private static readonly Regex NewsletterRx = Words(
        "unsubscribe", "newsletter", "digest", "weekly roundup", "monthly roundup",
        "manage your preferences", "email preferences", "view in browser");

    private static readonly string[] SocialDomains =
    [
        "linkedin.com", "facebook.com", "facebookmail.com", "instagram.com",
        "twitter.com", "x.com", "reddit.com", "redditmail.com", "pinterest.com",
        "tiktok.com", "threads.net", "discord.com", "snapchat.com",
    ];

    private static readonly string[] UnattendedLocalParts =
    [
        "noreply", "no-reply", "no_reply", "donotreply", "do-not-reply",
        "notification", "notifications", "notify", "alert", "alerts",
        "automated", "auto-reply", "mailer-daemon", "postmaster", "bounce",
    ];

    /// <summary>
    /// Builds a rich text representation of the email for full-text search.
    /// </summary>
    public string ExtractSearchableContent(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var parts = new List<string>();

        // Subject
        parts.Add($"Subject: {message.Subject}");

        // From
        parts.Add($"From: {FormatContact(message.From)}");

        // To
        if (message.To.Count > 0)
            parts.Add($"To: {string.Join(", ", message.To.Select(FormatContact))}");

        // Cc
        if (message.Cc.Count > 0)
            parts.Add($"Cc: {string.Join(", ", message.Cc.Select(FormatContact))}");

        // Date
        parts.Add($"Date: {message.ReceivedAt:yyyy-MM-dd HH:mm}");

        // Folder
        parts.Add($"Folder: {message.FolderName}");

        // Flags
        var flags = new List<string>();
        if (message.IsStarred) flags.Add("Starred");
        if (message.HasAttachments) flags.Add("HasAttachments");
        if (message.IsRead) flags.Add("Read");
        if (flags.Count > 0)
            parts.Add($"Flags: {string.Join(", ", flags)}");

        // Attachments
        if (message.AttachmentNames.Count > 0)
            parts.Add($"Attachments: {string.Join(", ", message.AttachmentNames)}");

        // Source provider
        parts.Add($"Source: {message.SourceProvider}");

        // Body text (preferred over HTML for search)
        if (!string.IsNullOrWhiteSpace(message.BodyText))
            parts.Add(message.BodyText);
        else if (!string.IsNullOrWhiteSpace(message.BodyHtml))
            parts.Add(StripHtmlTags(message.BodyHtml));

        return string.Join("\n\n", parts);
    }

    private static string FormatContact(EmailContact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.DisplayName))
            return contact.EmailAddress;
        return $"{contact.DisplayName} <{contact.EmailAddress}>";
    }

    /// <summary>
    /// Strips HTML tags for plain-text search indexing.
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        // Remove HTML tags
        var result = new System.Text.StringBuilder(html.Length);
        var inTag = false;

        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) result.Append(c);
        }

        // Decode common HTML entities
        return result.ToString()
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");
    }
}
