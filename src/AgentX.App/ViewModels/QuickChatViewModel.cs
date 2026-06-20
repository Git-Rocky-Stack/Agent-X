using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Screen;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the Quick Chat overlay window. Provides lightweight Q&A
/// against the knowledge vault with streaming AI responses.
/// When screen awareness is enabled, captures the screen at query time
/// and injects OCR text as context into the system prompt.
/// </summary>
public partial class QuickChatViewModel : ObservableObject
{
    private readonly IAiService _aiService;
    private readonly IScreenCaptureService? _screenCaptureService;
    private CancellationTokenSource? _queryCts;

    // ── Observable Properties ─────────────────────────────────────

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string _responseText = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _screenContextCaptured;

    public QuickChatViewModel(IAiService aiService)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    }

    public QuickChatViewModel(IAiService aiService, IScreenCaptureService screenCaptureService)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _screenCaptureService = screenCaptureService;
    }

    // ── Commands ─────────────────────────────────────────────────

    /// <summary>
    /// Submits the query to the AI service and streams the response token-by-token.
    /// Builds a brief system prompt that contextualizes the query against the
    /// user's knowledge vault.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSubmitQuery))]
    private async Task SubmitQueryAsync()
    {
        var query = QueryText.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        IsProcessing = true;
        StatusMessage = "Thinking...";
        ResponseText = string.Empty;
        ScreenContextCaptured = false;

        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;

        try
        {
            // Capture screen context if screen awareness service is available.
            ScreenContextResult? screenContext = null;
            if (_screenCaptureService is not null)
            {
                try
                {
                    screenContext = await _screenCaptureService.CaptureActiveWindowAndOcrAsync(ct).ConfigureAwait(false);
                    ScreenContextCaptured = !screenContext.IsEmpty;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Screen capture failed; continuing without screen context");
                }
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = query }
            };

            var systemPrompt = "You are Agent-X Quick Chat, a fast assistant that answers questions " +
                               "concisely based on the user's knowledge vault. Be brief, accurate, and helpful. " +
                               "If you don't know the answer, say so clearly.";

            // Append IDE context and screen context to the system prompt if available.
            if (screenContext is not null && !screenContext.IsEmpty)
            {
                var contextSection = new StringBuilder();
                contextSection.AppendLine();
                contextSection.AppendLine();

                // IDE context comes first — it's structured and more precise than OCR
                if (screenContext.IdeContext is not null)
                {
                    contextSection.AppendLine("--- IDE CONTEXT ---");
                    contextSection.AppendLine($"Active IDE: {screenContext.IdeContext.IdeName}");
                    contextSection.AppendLine($"Active File: {screenContext.IdeContext.ActiveFileName}");

                    if (!string.IsNullOrWhiteSpace(screenContext.IdeContext.ProjectName))
                        contextSection.AppendLine($"Project: {screenContext.IdeContext.ProjectName}");

                    if (!string.IsNullOrWhiteSpace(screenContext.IdeContext.Language))
                        contextSection.AppendLine($"Language: {screenContext.IdeContext.Language}");

                    contextSection.AppendLine();
                }

                contextSection.AppendLine("--- SCREEN CONTEXT ---");

                if (!string.IsNullOrWhiteSpace(screenContext.ActiveWindowTitle))
                    contextSection.AppendLine($"Active window: {screenContext.ActiveWindowTitle}");

                if (!string.IsNullOrWhiteSpace(screenContext.OcrText))
                    contextSection.AppendLine($"Screen text (captured {screenContext.CapturedAtUtc:HH:mm:ss} UTC):\n{screenContext.OcrText}");

                systemPrompt += contextSection.ToString();
            }

            var options = new ChatOptions
            {
                Temperature = 0.5,
                MaxTokens = 1024
            };

            var sb = new StringBuilder();

            await foreach (var token in _aiService.StreamChatAsync(messages, systemPrompt, options, ct))
            {
                sb.Append(token);
                ResponseText = sb.ToString();
            }

            StatusMessage = string.IsNullOrEmpty(ResponseText) ? "No response received" : "Done";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled";
            Log.Debug("Quick Chat query cancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = "Error";
            ResponseText = $"Failed to get response: {ex.Message}";
            Log.Warning(ex, "Quick Chat query failed");
        }
        finally
        {
            IsProcessing = false;
            SubmitQueryCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSubmitQuery() => !IsProcessing && !string.IsNullOrWhiteSpace(QueryText);

    /// <summary>
    /// Clears the query input, response text, and resets the status message.
    /// Cancels any in-progress query.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        _queryCts?.Cancel();
        QueryText = string.Empty;
        ResponseText = string.Empty;
        StatusMessage = "Ready";
        IsProcessing = false;
        ScreenContextCaptured = false;
    }

    /// <summary>
    /// Cancels any in-progress streaming query without clearing the existing response.
    /// </summary>
    public void CancelQuery()
    {
        _queryCts?.Cancel();
    }
}
