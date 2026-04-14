using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the Quick Chat overlay window. Provides lightweight Q&A
/// against the knowledge vault with streaming AI responses.
/// </summary>
public partial class QuickChatViewModel : ObservableObject
{
    private readonly IAiService _aiService;
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

    public QuickChatViewModel(IAiService aiService)
    {
        _aiService = aiService;
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

        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;

        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = query }
            };

            var systemPrompt = "You are Agent-X Quick Chat, a fast assistant that answers questions " +
                               "concisely based on the user's knowledge vault. Be brief, accurate, and helpful. " +
                               "If you don't know the answer, say so clearly.";

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
    }

    /// <summary>
    /// Cancels any in-progress streaming query without clearing the existing response.
    /// </summary>
    public void CancelQuery()
    {
        _queryCts?.Cancel();
    }
}