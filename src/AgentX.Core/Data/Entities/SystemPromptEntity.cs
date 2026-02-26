namespace AgentX.Core.Data.Entities;

public class SystemPromptEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // General, Writing, Code, Analysis, Creative
    public bool IsBuiltIn { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int UsageCount { get; set; }
}
