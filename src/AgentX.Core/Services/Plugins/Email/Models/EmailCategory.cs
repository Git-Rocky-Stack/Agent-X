namespace AgentX.Core.Services.Plugins.Email.Models;

/// <summary>
/// AI-assigned categories for email triage.
/// </summary>
public enum EmailCategory
{
    Other = 0,
    ActionRequired = 1,
    Newsletter = 2,
    Notification = 3,
    Meeting = 4,
    Financial = 5,
    Social = 6,
    Promotion = 7,
}