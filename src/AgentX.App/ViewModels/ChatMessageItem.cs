using CommunityToolkit.Mvvm.ComponentModel;
using AgentX.App.Helpers;
using AgentX.Core.Search.Models;

namespace AgentX.App.ViewModels;

/// <summary>
/// Represents a single chat message displayed in the UI.
/// </summary>
public class ChatMessageItem : ObservableObject
{
    private string _content = string.Empty;
    private bool _isStreaming;
    private string _feedbackRating = "none";
    private bool _isEditing;
    private string _editContent = string.Empty;

    /// <summary>Database primary key. 0 if not yet persisted.</summary>
    public long MessageId { get; set; }

    /// <summary>Conversation this message belongs to.</summary>
    public long ConversationId { get; set; }

    /// <summary>Sort order within the conversation.</summary>
    public int SortOrder { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                OnPropertyChanged(nameof(ContentSegments));
            }
        }
    }

    /// <summary>
    /// Parsed markdown segments for rich rendering of assistant messages.
    /// Re-computed whenever Content changes.
    /// </summary>
    public List<MarkdownSegment> ContentSegments => MarkdownParser.Parse(Content);

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsUser { get; set; }
    public bool IsAssistant { get; set; }
    public bool IsSystem { get; set; }
    public int TokenCount { get; set; }
    public double GenerationTimeMs { get; set; }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    /// <summary>
    /// Feedback rating for assistant messages: "positive", "negative", or "none".
    /// </summary>
    public string FeedbackRating
    {
        get => _feedbackRating;
        set
        {
            if (SetProperty(ref _feedbackRating, value))
            {
                OnPropertyChanged(nameof(IsThumbsUp));
                OnPropertyChanged(nameof(IsThumbsDown));
            }
        }
    }

    public bool IsThumbsUp => FeedbackRating == "positive";
    public bool IsThumbsDown => FeedbackRating == "negative";

    /// <summary>Whether this user message is in inline edit mode.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }
    }

    public bool IsNotEditing => !IsEditing;

    /// <summary>Content of the edit TextBox while editing.</summary>
    public string EditContent
    {
        get => _editContent;
        set => SetProperty(ref _editContent, value);
    }

    public string FormattedTime => Timestamp.ToLocalTime().ToString("h:mm tt");

    public string FormattedTokens => TokenCount > 0
        ? $"{TokenCount} tokens"
        : string.Empty;

    public string FormattedGenerationTime => GenerationTimeMs > 0
        ? $"{GenerationTimeMs:F0}ms"
        : string.Empty;

    public string FormattedTokenSpeed => TokenCount > 0 && GenerationTimeMs > 0
        ? $"{TokenCount / (GenerationTimeMs / 1000.0):F1} tok/s"
        : string.Empty;

    /// <summary>Whether this message is a point where one or more branches diverge.</summary>
    private bool _isBranchPoint;
    public bool IsBranchPoint
    {
        get => _isBranchPoint;
        set => SetProperty(ref _isBranchPoint, value);
    }

    /// <summary>Number of branches diverging from this message.</summary>
    private int _branchCountAtPoint;
    public int BranchCountAtPoint
    {
        get => _branchCountAtPoint;
        set => SetProperty(ref _branchCountAtPoint, value);
    }

    /// <summary>Web citations associated with this message (from Deep Research Mode).</summary>
    public IReadOnlyList<WebCitation>? WebCitations { get; set; }

    /// <summary>Whether this message has web citations to display.</summary>
    public bool HasWebCitations => WebCitations?.Count > 0;
}
