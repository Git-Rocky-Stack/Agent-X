using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Controls;

/// <summary>
/// The OAuth App Credentials form of the Calendar and Email connector pages: the client of the
/// user's own Google and Microsoft OAuth apps, which Connect signs in with. It builds its own
/// <see cref="OAuthAppCredentialsViewModel"/> and reloads the saved values each time it is
/// shown, so credentials saved on one page appear on the other.
/// </summary>
public sealed partial class OAuthAppCredentialsPanel : UserControl
{
    public OAuthAppCredentialsViewModel ViewModel { get; }

    public OAuthAppCredentialsPanel()
    {
        ViewModel = PageViewModelFactory.Create<OAuthAppCredentialsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
}
