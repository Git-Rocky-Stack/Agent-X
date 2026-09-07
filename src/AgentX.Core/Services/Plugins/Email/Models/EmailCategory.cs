namespace AgentX.Core.Services.Plugins.Email.Models;

/// <summary>
/// Triage category assigned to an incoming email by
/// <see cref="EmailTriageProcessor.Classify"/>. The name of the selected member is
/// stored on <c>InboxItemEntity.SourceCategory</c> and shown as the source label on
/// the Operations page.
///
/// Assignment is rule-based, not model-based: the rules run offline, cost nothing,
/// and return the same answer for the same message every time.
/// </summary>
public enum EmailCategory
{
    /// <summary>Nothing more specific matched. The default.</summary>
    Other = 0,

    /// <summary>The sender is waiting on the reader: approvals, overdue items, deadlines.</summary>
    ActionRequired = 1,

    /// <summary>Subscribed bulk mail, identified by an unsubscribe route or digest wording.</summary>
    Newsletter = 2,

    /// <summary>Automated machine mail from an unattended address.</summary>
    Notification = 3,

    /// <summary>An invitation, agenda, or calendar attachment.</summary>
    Meeting = 4,

    /// <summary>Invoices, receipts, statements, payments, and refunds.</summary>
    Financial = 5,

    /// <summary>Traffic from a social network.</summary>
    Social = 6,

    /// <summary>Marketing offers: discounts, sales, coupons.</summary>
    Promotion = 7,
}
