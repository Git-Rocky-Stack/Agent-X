using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AgentX.App.Views.Dialogs;

public sealed partial class JumpToDialog : ContentDialog
{
    public JumpToDialog(JumpToViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += JumpToDialog_Loaded;
    }

    public JumpToViewModel ViewModel { get; }

    private async void JumpToDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        if (ViewModel.Results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
        }

        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Query = SearchBox.Text;
        ResultsList.SelectedIndex = ViewModel.Results.Count > 0 ? 0 : -1;
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                await ExecuteSelectedAsync();
                break;
            case VirtualKey.Down:
                e.Handled = true;
                MoveSelection(1);
                break;
            case VirtualKey.Up:
                e.Handled = true;
                MoveSelection(-1);
                break;
        }
    }

    private async void ResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        e.Handled = true;
        await ExecuteSelectedAsync();
    }

    private async void ResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => await ExecuteSelectedAsync();

    private void MoveSelection(int delta)
    {
        if (ViewModel.Results.Count == 0)
        {
            ResultsList.SelectedIndex = -1;
            return;
        }

        var next = ResultsList.SelectedIndex + delta;
        if (next < 0)
        {
            next = ViewModel.Results.Count - 1;
        }
        else if (next >= ViewModel.Results.Count)
        {
            next = 0;
        }

        ResultsList.SelectedIndex = next;
        ResultsList.ScrollIntoView(ViewModel.Results[next]);
    }

    private async Task ExecuteSelectedAsync()
    {
        if (ResultsList.SelectedItem is not JumpToItem item) return;

        await ViewModel.ExecuteAsync(item);
        Hide();
    }
}
