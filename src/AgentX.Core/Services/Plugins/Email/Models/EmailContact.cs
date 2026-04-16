namespace AgentX.Core.Services.Plugins.Email.Models;

/// <summary>
/// Represents a person in an email address field (From, To, Cc, Bcc).
/// </summary>
public sealed class EmailContact
{
    public string DisplayName { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public bool IsMe { get; init; }
}