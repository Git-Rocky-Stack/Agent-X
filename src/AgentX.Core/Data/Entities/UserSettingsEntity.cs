namespace AgentX.Core.Data.Entities;

public class UserSettingsEntity
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "string"; // string, int, bool, double, json
    public DateTime UpdatedAt { get; set; }
}
