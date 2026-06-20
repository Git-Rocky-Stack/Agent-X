using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

/// <summary>
/// User Guide page - displays comprehensive documentation for Agent-X features.
/// Sections are rendered via ItemsControl bound to UserGuideViewModel.Sections.
/// </summary>
public sealed partial class UserGuidePage : Page
{
    public UserGuideViewModel ViewModel { get; }

    public UserGuidePage()
    {
        ViewModel = new UserGuideViewModel();
        InitializeComponent();
    }
}
