using System.Collections.ObjectModel;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

// =============================================================================
// ASK FILES VIEW MODEL
//
// Drives the "Ask Your Files" RAG page: accepts user questions, retrieves
// relevant chunks via the RAG pipeline, streams AI-generated answers with
// citations, and manages collection scope selection.
// =============================================================================

public partial class AskFilesViewModel : ObservableObject
{
    private readonly IRagPipeline _ragPipeline;
    private readonly IDocumentService _documentService;
    private readonly ICollectionService _collectionService;
    private readonly ILogger _logger;
    private readonly ILocalizationService _localization;

    private CancellationTokenSource? _generationCts;

    // ── Question Input & State ───────────────────────────────────
    [ObservableProperty] private string _questionText = string.Empty;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _hasCitations;
    [ObservableProperty] private bool _showEmptyState = true;

    // ── Collection Scope ─────────────────────────────────────────
    [ObservableProperty] private long? _selectedCollectionId;
    [ObservableProperty] private string? _selectedCollectionName;

    // ── Index Status ─────────────────────────────────────────────
    [ObservableProperty] private long _indexedChunkCount;
    [ObservableProperty] private string _indexStatusMessage = string.Empty;

    // ── Collections ──────────────────────────────────────────────
    public ObservableCollection<AskFilesMessage> Messages { get; } = new();
    public ObservableCollection<CitationItem> ActiveCitations { get; } = new();
    public ObservableCollection<CollectionOption> AvailableCollections { get; } = new();

    public AskFilesViewModel(
        IRagPipeline ragPipeline,
        IDocumentService documentService,
        ICollectionService collectionService,
        ILogger logger,
        ILocalizationService localization)
    {
        _ragPipeline = ragPipeline;
        _documentService = documentService;
        _collectionService = collectionService;
        _logger = logger;
        _localization = localization;
        IndexStatusMessage = _localization.GetString("AskFiles_IndexStatusLoading");
        _logger.Debug("AskFilesViewModel created with services");
    }

    // =================================================================
    // INITIALIZATION
    // =================================================================

    public async Task InitializeAsync()
    {
        _logger.Information("AskFilesViewModel initializing...");

        try
        {
            // Load available collections for the scope dropdown
            await LoadCollectionsAsync();

            // Load chunk count for the status badge
            await LoadIndexStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to initialize AskFilesViewModel");
            IndexStatusMessage = _localization.GetString("AskFiles_KnowledgeBaseStatusUnavailable");
        }
    }

    // =================================================================
    // COMMANDS
    // =================================================================

    /// <summary>
    /// Sends the user's question through the RAG pipeline.
    /// Adds a user message bubble, creates an assistant message,
    /// streams tokens into it, and populates citations on completion.
    /// </summary>
    [RelayCommand]
    private async Task AskAsync()
    {
        var question = QuestionText?.Trim();
        if (string.IsNullOrWhiteSpace(question))
            return;

        // Clear the input and empty state
        var questionCopy = question;
        QuestionText = string.Empty;
        ShowEmptyState = false;
        IsGenerating = true;

        // Add user message
        var userMessage = new AskFilesMessage
        {
            IsUser = true,
            Content = questionCopy,
            Timestamp = DateTime.UtcNow
        };
        Messages.Add(userMessage);

        // Add empty assistant message (will stream into)
        var assistantMessage = new AskFilesMessage
        {
            IsUser = false,
            Content = string.Empty,
            Timestamp = DateTime.UtcNow,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);

        _generationCts?.Cancel();
        _generationCts = new CancellationTokenSource();

        // The pipeline streams tokens from a thread-pool thread (its loop awaits with
        // ConfigureAwait(false)), but Content is bound to the view and must only change on
        // the UI thread. Tokens are marshalled back to the context the question was asked on.
        var uiContext = SynchronizationContext.Current;

        try
        {
            // Clear previous citations
            ActiveCitations.Clear();
            HasCitations = false;

            // Use AskAsync with an onToken callback for real-time streaming.
            // This returns the complete RagResponse with citations after streaming finishes.
            var ragResponse = await _ragPipeline.AskAsync(
                questionCopy,
                collectionId: SelectedCollectionId,
                onToken: token => AppendToken(assistantMessage, token, uiContext),
                ct: _generationCts.Token);

            // Streaming complete
            assistantMessage.IsStreaming = false;
            assistantMessage.Content = ragResponse.AnswerText;

            // Populate citations from the RAG response
            var citationItems = new List<CitationItem>();
            foreach (var citation in ragResponse.Citations)
            {
                var item = new CitationItem
                {
                    Number = citation.Number,
                    DocumentId = citation.DocumentId,
                    FileName = citation.FileName,
                    FilePath = citation.FilePath.Length > 0
                        ? citation.FilePath
                        : await GetFilePathForDocumentAsync(citation.DocumentId),
                    PageNumber = citation.PageNumber,
                    Excerpt = TruncateExcerpt(citation.Excerpt, 200),
                    RelevancePercent = (int)Math.Round(citation.RelevanceScore * 100),
                    Label = citation.PageNumber.HasValue
                        ? _localization.GetString("AskFiles_CitationLabelWithPage", citation.Number, citation.FileName, citation.PageNumber.Value)
                        : $"[{citation.Number}] {citation.FileName}"
                };
                citationItems.Add(item);
            }

            // Update the assistant message with citations
            assistantMessage.Citations = citationItems;

            // Update active citations panel
            ActiveCitations.Clear();
            foreach (var c in citationItems)
                ActiveCitations.Add(c);

            HasCitations = ActiveCitations.Count > 0;
        }
        catch (OperationCanceledException)
        {
            _logger.Information("RAG generation was cancelled by user");
            assistantMessage.Content = assistantMessage.Content.Length > 0
                ? assistantMessage.Content + "\n\n" + _localization.GetString("AskFiles_GenerationStoppedMarker")
                : _localization.GetString("AskFiles_GenerationStopped");
            assistantMessage.IsStreaming = false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "RAG pipeline failed for question: {Question}", questionCopy);
            assistantMessage.Content = _localization.GetString("AskFiles_AnswerFailed");
            assistantMessage.IsStreaming = false;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// Clears all conversation messages and active citations.
    /// </summary>
    [RelayCommand]
    private void ClearConversation()
    {
        _generationCts?.Cancel();
        Messages.Clear();
        ActiveCitations.Clear();
        HasCitations = false;
        ShowEmptyState = true;
    }

    /// <summary>
    /// Sets the collection scope for RAG queries.
    /// Null means search across all collections.
    /// </summary>
    [RelayCommand]
    private void SelectCollection(long? collectionId)
    {
        SelectedCollectionId = collectionId;

        if (collectionId is null)
        {
            SelectedCollectionName = null;
        }
        else
        {
            var option = AvailableCollections.FirstOrDefault(c => c.Id == collectionId);
            SelectedCollectionName = option?.Name;
        }
    }

    /// <summary>
    /// Opens a citation's source document in Windows Explorer.
    /// </summary>
    [RelayCommand]
    private void OpenCitation(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.Warning("Cannot open citation: empty file path");
                return;
            }

            if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                _logger.Warning("Citation file not found at path: {Path}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open citation file: {Path}", filePath);
        }
    }

    // =================================================================
    // PRIVATE HELPERS
    // =================================================================

    /// <summary>
    /// Appends a streamed token on the UI thread. Posts to <paramref name="uiContext"/> when
    /// called from another thread; posts run in order, and before the continuation that
    /// replaces the content with the final answer, which is posted after the last token.
    /// </summary>
    private static void AppendToken(AskFilesMessage message, string token, SynchronizationContext? uiContext)
    {
        if (uiContext is null || ReferenceEquals(SynchronizationContext.Current, uiContext))
        {
            message.Content += token;
            return;
        }

        uiContext.Post(_ => message.Content += token, null);
    }

    private async Task LoadCollectionsAsync()
    {
        try
        {
            var collections = await _collectionService.GetAllCollectionsAsync();

            AvailableCollections.Clear();

            // Add "All Collections" option
            AvailableCollections.Add(new CollectionOption
            {
                Id = null,
                Name = _localization.GetString("AskFiles_AllCollections")
            });

            foreach (var c in collections)
            {
                AvailableCollections.Add(new CollectionOption
                {
                    Id = c.Id,
                    Name = c.Name
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load collections for scope selector");
        }
    }

    private async Task LoadIndexStatusAsync()
    {
        try
        {
            // Get total documents and calculate approximate chunk count
            var totalDocs = await _documentService.GetTotalDocumentCountAsync();
            var allDocs = await _documentService.GetAllDocumentsAsync(statusFilter: "completed");
            long totalChunks = 0;
            foreach (var doc in allDocs)
                totalChunks += doc.ChunkCount;

            IndexedChunkCount = totalChunks;

            if (totalChunks > 0)
            {
                IndexStatusMessage = totalChunks == 1
                    ? _localization.GetString("AskFiles_ChunksAvailableOne", totalChunks.ToString("N0"))
                    : _localization.GetString("AskFiles_ChunksAvailableMany", totalChunks.ToString("N0"));
            }
            else if (totalDocs > 0)
            {
                IndexStatusMessage = _localization.GetString("AskFiles_DocumentsBeingIndexed");
            }
            else
            {
                IndexStatusMessage = _localization.GetString("AskFiles_ImportToGetStarted");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load index status");
            IndexStatusMessage = _localization.GetString("AskFiles_StatusUnavailable");
        }
    }

    /// <summary>
    /// Resolves a citation's file path when the answer did not carry one.
    /// <para>
    /// This runs inside the answer pipeline on the UI thread. Blocking on the lookup
    /// (Task.Wait / Task.Result) risks deadlocking against the UI SynchronizationContext,
    /// so the call stays awaited.
    /// </para>
    /// </summary>
    private async Task<string> GetFilePathForDocumentAsync(long documentId)
    {
        try
        {
            var document = await _documentService.GetDocumentAsync(documentId).ConfigureAwait(true);
            return document?.FilePath ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not resolve a file path for document {DocumentId}", documentId);
            return string.Empty;
        }
    }

    private static string TruncateExcerpt(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        cleaned = cleaned.Trim();

        if (cleaned.Length <= maxLength)
            return cleaned;

        var truncated = cleaned[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.6)
            truncated = truncated[..lastSpace];

        return truncated + "...";
    }
}

// =============================================================================
// ASK FILES MESSAGE — Display model for a chat message in the RAG conversation
// =============================================================================

public partial class AskFilesMessage : ObservableObject
{
    public bool IsUser { get; init; }
    [ObservableProperty] private string _content = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [ObservableProperty] private bool _isStreaming;

    /// <summary>
    /// Sources of the answer. Assigned when the answer completes, after the message is
    /// already on screen, so it notifies the inline citation badges bound to it.
    /// </summary>
    [ObservableProperty] private List<CitationItem> _citations = new();

    /// <summary>
    /// Returns true if this is an AI response (not a user question).
    /// Used in XAML binding for template selection.
    /// </summary>
    public bool IsAssistant => !IsUser;

    /// <summary>
    /// Formatted timestamp for display.
    /// </summary>
    public string FormattedTime => Timestamp.ToLocalTime().ToString("h:mm tt");
}

// =============================================================================
// CITATION ITEM — Display model for a source citation
// =============================================================================

public class CitationItem
{
    public int Number { get; init; }
    public long DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public string Excerpt { get; init; } = string.Empty;
    public int RelevancePercent { get; init; }

    /// <summary>
    /// Short display label for inline citation badges, e.g. "[1] report.pdf, p.12", set by the
    /// view model in the UI language.
    /// </summary>
    public string Label { get; init; } = string.Empty;
}

// =============================================================================
// COLLECTION OPTION — Dropdown item for collection scope selector
// =============================================================================

public class CollectionOption
{
    public long? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
