using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using AgentX.App.ViewModels;

namespace AgentX.App.Views;

/// <summary>
/// Code-behind for PastSelfPage - "What did I think about X?" mode.
/// </summary>
public sealed partial class PastSelfPage : Page, INotifyPropertyChanged
{
    public PastSelfViewModel ViewModel { get; }

    public PastSelfPage()
    {
        InitializeComponent();
        ViewModel = App.Host.Services.GetService(typeof(PastSelfViewModel)) as PastSelfViewModel
            ?? throw new InvalidOperationException("PastSelfViewModel not registered in DI container");

        // Subscribe to ViewModel property changes to update helper properties
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.SelectedTimeRange))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCustomDate)));
            if (e.PropertyName == nameof(ViewModel.CurrentResult))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEvidence)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasInsights)));
            }
            if (e.PropertyName == nameof(ViewModel.ActiveTopics))
                UpdateActiveTopicsPanel();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Set default time range to "Past month" (index 2)
        ViewModel.SelectedTimeRange = 2;
    }

    // ─── Search Handlers ───────────────────────────────────────────────────────

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
        {
            SearchButton_Click(sender, e);
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
            return;

        await ViewModel.SearchPastSelfAsync();
    }

    // ─── Time Range Handlers ────────────────────────────────────────────────────

    private void TimeRange_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int timeRange))
        {
            ViewModel.SelectedTimeRange = timeRange;
        }
    }

    private void CustomDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (sender.Date != null)
        {
            ViewModel.SelectedDate = sender.Date.Value.DateTime;
        }
    }

    // ─── Quick Action Handlers ──────────────────────────────────────────────────

    private async void InsightsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
            return;

        await ViewModel.GetRelevantInsightsAsync();
    }

    private async void EvolutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
            return;

        await ViewModel.ShowBeliefEvolutionAsync();
    }

    private async void ActiveTopicsButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GetActiveTopicsAsync();
    }

    // ─── Helper Properties for XAML Binding ──────────────────────────────────────

    /// <summary>
    /// Visibility of custom date picker (only when "Custom date" is selected).
    /// </summary>
    public Visibility ShowCustomDate => ViewModel.SelectedTimeRange == 4 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Whether the current result has evidence excerpts to display.
    /// </summary>
    public bool HasEvidence => ViewModel.CurrentResult?.EvidenceExcerpts != null
        && ViewModel.CurrentResult.EvidenceExcerpts.Length > 0;

    /// <summary>
    /// Whether the current result has relevant insights to display.
    /// </summary>
    public bool HasInsights => ViewModel.CurrentResult?.RelevantInsights != null
        && ViewModel.CurrentResult.RelevantInsights.Count > 0;

    // ─── Active Topics Panel ────────────────────────────────────────────────────

    private void UpdateActiveTopicsPanel()
    {
        if (ViewModel.ActiveTopics == null || !ViewModel.ActiveTopics.Any())
        {
            ActiveTopicsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ActiveTopicsPanel.Visibility = Visibility.Visible;
        ActiveTopicsList.ItemsSource = ViewModel.ActiveTopics;
    }

    // ─── Generative Identity: "Draft as Me" Panel ────────────────────────────────────

    private async void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadVoiceProfileAsync();
    }

    private async void GenerateDraftButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GenerateDraftAsMeAsync();
    }

    private void CopyDraftButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.DraftContent))
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(ViewModel.DraftContent);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }

    private void UseInChatButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Chat page with the draft pre-populated
        // This would require passing the draft content to the ChatViewModel
        // For now, just copy to clipboard as a fallback
        CopyDraftButton_Click(sender, e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
