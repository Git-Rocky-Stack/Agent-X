using AgentX.Core.Helpers;
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

    /// <summary>
    /// How long ago the conversation last changed ("5m ago", then weeks, months and the date), in
    /// the user's language, worded like the dashboard's recent conversations.
    /// </summary>
    public string FormattedTime => FormatHelper.TimeAgoWithMonths(UpdatedAt);
}
