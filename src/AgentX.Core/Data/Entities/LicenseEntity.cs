namespace AgentX.Core.Data.Entities;

public class LicenseEntity
{
    public long Id { get; set; }
    public string LicenseKey { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public string Tier { get; set; } = "starter"; // starter, professional, ultimate
    public bool IsActivated { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
}
