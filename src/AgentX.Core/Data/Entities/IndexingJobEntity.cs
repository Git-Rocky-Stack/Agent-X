namespace AgentX.Core.Data.Entities;

public class IndexingJobEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string Status { get; set; } = "queued"; // queued, processing, completed, failed
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChunksProcessed { get; set; }
    public int EmbeddingsGenerated { get; set; }
    public double? ProcessingTimeMs { get; set; }

    // Navigation
    public DocumentEntity Document { get; set; } = null!;
}
