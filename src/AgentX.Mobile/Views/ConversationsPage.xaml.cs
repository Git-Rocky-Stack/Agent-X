using AgentX.Mobile.ViewModels;

namespace AgentX.Mobile.Views;

/// <summary>
/// Displays all non-archived conversations from the AgentX desktop app.
/// Read-only — conversation creation happens exclusively in the desktop app.
/// </summary>
public sealed partial class ConversationsPage : ContentPage
{
    private readonly ConversationsViewModel _vm;

    public ConversationsPage(ConversationsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        Resources.Add("InvertedBoolConverter", new CommunityToolkit.Maui.Converters.InvertedBoolConverter());
    }

    /// <inheritdoc/>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_vm.Conversations.Count == 0)
        {
            await _vm.LoadAsync().ConfigureAwait(false);
        }
    }
}
