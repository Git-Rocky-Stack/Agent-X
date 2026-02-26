namespace AgentX.Core.Data.Entities;

public class WatchFolderEntity
{
    public long Id { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IncludeSubfolders { get; set; }
    public string? FileTypeFilter { get; set; } // e.g., "pdf,docx,txt,md"
    public long? TargetCollectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastScanAt { get; set; }
    public int FilesIndexed { get; set; }

    // Navigation
    public CollectionEntity? TargetCollection { get; set; }
}
