using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views.Dialogs;

public sealed partial class CheatsheetDialog : ContentDialog
{
    public CheatsheetDialog(CheatsheetViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public CheatsheetViewModel ViewModel { get; }
}
