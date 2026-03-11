using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Mobile.Models;
using AgentX.Mobile.Services;

namespace AgentX.Mobile.ViewModels;

/// <summary>
/// Backing view-model for <c>DocumentsPage</c>.
/// Loads the document list from the AgentX REST API and exposes it for binding.
/// </summary>
public sealed partial class DocumentsViewModel : ObservableObject
{
    private readonly AgentXApiClient _api;

    public DocumentsViewModel(AgentXApiClient api)
    {
        _api = api;
    }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<DocumentDto> _documents = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Loads (or refreshes) the document list from the API.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var docs = await _api.GetDocumentsAsync(ct).ConfigureAwait(true);

            Documents.Clear();
            foreach (var doc in docs)
                Documents.Add(doc);

            IsEmpty = Documents.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Navigation away — ignore
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Could not load documents: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
