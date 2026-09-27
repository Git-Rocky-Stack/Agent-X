using AgentX.App.Helpers;
using AgentX.Core.Search.Models;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private string _inlineContextStoryText = string.Empty;
    private IReadOnlyList<string> _inlineContextStorySourceChips = Array.Empty<string>();
    private int _tokenCount;
    private double _generationTimeMs;
    private IReadOnlyList<WebCitation>? _webCitations;

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

    /// <summary>
    /// Token count of the response. Set when a streamed reply completes, after the bubble is
    /// already on screen, so it notifies along with the stats derived from it.
    /// </summary>
    public int TokenCount
    {
        get => _tokenCount;
        set
        {
            if (SetProperty(ref _tokenCount, value))
            {
                OnPropertyChanged(nameof(FormattedTokens));
                OnPropertyChanged(nameof(FormattedTokenSpeed));
            }
        }
    }

    /// <summary>Generation time of the response in milliseconds. See <see cref="TokenCount"/>.</summary>
    public double GenerationTimeMs
    {
        get => _generationTimeMs;
        set
        {
            if (SetProperty(ref _generationTimeMs, value))
            {
                OnPropertyChanged(nameof(FormattedGenerationTime));
                OnPropertyChanged(nameof(FormattedTokenSpeed));
            }
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (SetProperty(ref _isStreaming, value))
            {
                OnPropertyChanged(nameof(HasInlineContextNote));
            }
        }
    }

    public string InlineContextStoryText
    {
        get => _inlineContextStoryText;
        set
        {
            if (SetProperty(ref _inlineContextStoryText, value))
            {
                OnPropertyChanged(nameof(HasInlineContextNote));
            }
        }
    }

    public IReadOnlyList<string> InlineContextStorySourceChips
    {
        get => _inlineContextStorySourceChips;
        set
        {
            if (SetProperty(ref _inlineContextStorySourceChips, value))
            {
                OnPropertyChanged(nameof(HasInlineContextStorySourceChips));
            }
        }
    }

    public bool HasInlineContextNote =>
        IsAssistant &&
        !IsStreaming &&
        !string.IsNullOrWhiteSpace(InlineContextStoryText);

    public bool HasInlineContextStorySourceChips => InlineContextStorySourceChips.Count > 0;

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

    /// <summary>
    /// The web sources Research Mode added to this answer. A streamed reply gets them when it
    /// completes, after the bubble is on screen, so this notifies along with what is built from it.
    /// </summary>
    public IReadOnlyList<WebCitation>? WebCitations
    {
        get => _webCitations;
        set
        {
            if (SetProperty(ref _webCitations, value))
            {
                OnPropertyChanged(nameof(HasWebCitations));
                OnPropertyChanged(nameof(WebCitationChips));
            }
        }
    }

    /// <summary>Whether this message has web citations to display.</summary>
    public bool HasWebCitations => WebCitations?.Count > 0;

    /// <summary>The web sources as numbered chips for the bubble.</summary>
    public IReadOnlyList<WebCitationChip> WebCitationChips => WebCitationChip.From(WebCitations);
}

/// <summary>
/// One web source under an answer: its number in the answer's [n] markers (the order Research
/// Mode listed the results in), its title and site, and the link that opens it. Only http and
/// https addresses become links; anything else is shown but cannot be opened.
/// </summary>
public sealed class WebCitationChip
{
    public int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public Uri? Link { get; init; }

    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null;

    /// <summary>The chip text, for example "[1] Release notes (example.org)".</summary>
    public string Label
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Title) ? Site : Title;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Url;
            }

            return string.IsNullOrWhiteSpace(Site) || string.Equals(name, Site, StringComparison.OrdinalIgnoreCase)
                ? $"[{Number}] {name}"
                : $"[{Number}] {name} ({Site})";
        }
    }

    public static IReadOnlyList<WebCitationChip> From(IReadOnlyList<WebCitation>? citations)
    {
        if (citations is not { Count: > 0 })
        {
            return Array.Empty<WebCitationChip>();
        }

        var chips = new List<WebCitationChip>(citations.Count);
        for (var i = 0; i < citations.Count; i++)
        {
            var citation = citations[i];
            var url = citation.Url?.Trim() ?? string.Empty;
            Uri? link = null;
            var site = string.Empty;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                link = uri;
                site = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            }

            chips.Add(new WebCitationChip
            {
                Number = i + 1,
                Title = citation.Title?.Trim() ?? string.Empty,
                Site = site,
                Url = url,
                Link = link
            });
        }

        return chips;
    }
}
