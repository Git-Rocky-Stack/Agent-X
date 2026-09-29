using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Mobile.Models;
using AgentX.Mobile.Services;

namespace AgentX.Mobile.ViewModels;

/// <summary>
/// Backing view-model for <c>ConversationsPage</c>.
/// Loads the conversation list from the AgentX REST API.
/// </summary>
public sealed partial class ConversationsViewModel : ObservableObject
{
    private readonly AgentXApiClient _api;

    public ConversationsViewModel(AgentXApiClient api)
    {
        _api = api;
    }

    // -- Observable state ------------------------------------------------------

    [ObservableProperty]
    private ObservableCollection<ConversationDto> _conversations = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    // -- Commands --------------------------------------------------------------

    /// <summary>Loads (or refreshes) the conversation list from the API.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _api.GetConversationsAsync(ct).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                // Say why (not paired, unreachable, server error) instead of showing the
                // "No conversations yet" empty state for what is really a failure.
                HasError = true;
                ErrorMessage = result.ErrorMessage ?? "Could not load conversations.";
                IsEmpty = false;
                return;
            }

            Conversations.Clear();
            foreach (var c in result.Data!)
                Conversations.Add(c);

            IsEmpty = Conversations.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Navigation away - ignore
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Could not load conversations: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
