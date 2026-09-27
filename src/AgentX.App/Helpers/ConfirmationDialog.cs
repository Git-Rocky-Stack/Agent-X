using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Helpers;

/// <summary>
/// Shows a view model's <see cref="ConfirmationRequest"/> as a ContentDialog, the way the
/// conversation delete confirmation does: the confirm action is the primary button and Cancel
/// is the default, so Enter never confirms a destructive action by accident.
/// </summary>
public static class ConfirmationDialog
{
    /// <summary>True when the user chose the confirm button.</summary>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, ConfirmationRequest request)
    {
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = request.Message,
            PrimaryButtonText = request.ConfirmText,
            CloseButtonText = request.CancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
