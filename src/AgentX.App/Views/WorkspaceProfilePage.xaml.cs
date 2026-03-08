using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using AgentX.App.ViewModels;

namespace AgentX.App.Views;

/// <summary>
/// Workspace Profiles page: Two-panel layout with a profile list sidebar on
/// the left (280px) and a full profile editor on the right.
/// Users can create, edit, duplicate, set-default, and delete workspace profiles.
/// </summary>
public sealed partial class WorkspaceProfilePage : Page
{
    public WorkspaceProfileViewModel ViewModel { get; }

    public WorkspaceProfilePage()
    {
        ViewModel = App.GetService<WorkspaceProfileViewModel>();
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    /// <summary>
    /// Initializes the ViewModel when the page finishes loading.
    /// </summary>
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Handles click on a profile item in the sidebar list.
    /// Selects the profile and populates the editor panel.
    /// The ProfileDisplayItem is passed via the Border's Tag property.
    /// </summary>
    private void OnProfileItemClick(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is ProfileDisplayItem profile)
        {
            ViewModel.SelectProfileCommand.Execute(profile);
        }
    }

    /// <summary>
    /// Helper for the empty-state visibility: returns Visible when the profile
    /// count is 0 and the page is not currently loading.
    /// </summary>
    private Visibility HasNoProfiles(int profileCount, bool isLoading)
    {
        return profileCount == 0 && !isLoading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
