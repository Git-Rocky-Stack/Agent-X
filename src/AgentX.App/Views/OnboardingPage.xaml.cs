using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class OnboardingPage : Page
{
    public OnboardingViewModel ViewModel { get; }

    public OnboardingPage()
    {
        ViewModel = App.GetService<OnboardingViewModel>();
        InitializeComponent();
    }
}
