namespace AgentX.Core.Data.Entities;

public class DocumentTagEntity
{
    public long DocumentId { get; set; }
    public long TagId { get; set; }
    public double Confidence { get; set; } // 0.0 - 1.0 for auto-generated tags
    public DateTime AssignedAt { get; set; }

    // Navigation
    public DocumentEntity Document { get; set; } = null!;
    public TagEntity Tag { get; set; } = null!;
}
