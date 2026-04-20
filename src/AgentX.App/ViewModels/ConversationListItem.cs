using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels;

/// <summary>
/// Represents a conversation entry in the sidebar list.
/// </summary>
public class ConversationListItem : ObservableObject
{
    private bool _isPinned;
    private string _lastMessage = string.Empty;
    private string? _folderName;

    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;

    public string LastMessage
    {
        get => _lastMessage;
        set => SetProperty(ref _lastMessage, value);
    }

    public DateTime UpdatedAt { get; set; }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public string? FolderName
    {
        get => _folderName;
        set => SetProperty(ref _folderName, value);
    }

    public int MessageCount { get; set; }

    public string FormattedTime
    {
        get
        {
            var span = DateTime.UtcNow - UpdatedAt;
            return span.TotalMinutes < 1 ? "now"
                : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m ago"
                : span.TotalHours < 24 ? $"{(int)span.TotalHours}h ago"
                : span.TotalDays < 7 ? $"{(int)span.TotalDays}d ago"
                : UpdatedAt.ToLocalTime().ToString("MMM d");
        }
    }
}
