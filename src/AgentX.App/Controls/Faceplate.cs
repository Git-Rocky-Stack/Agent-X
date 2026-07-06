using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Controls;

/// <summary>
/// A Layer 1 raised faceplate (DESIGN.md four-layer depth system): brushed
/// aluminum plate with a machined stripe header, Departure Mono kicker,
/// four corner hex socket bolts, and ambient depth. Content is the plate
/// body. Template lives in Themes/Generic.xaml.
///
/// Kicker strings are equipment vocabulary ("MOD - VAULT - 01") and are
/// intentionally not localized; localized placards belong in the body.
/// </summary>
public sealed partial class Faceplate : ContentControl
{
    public Faceplate()
    {
        DefaultStyleKey = typeof(Faceplate);
    }

    public static readonly DependencyProperty KickerProperty = DependencyProperty.Register(
        nameof(Kicker), typeof(string), typeof(Faceplate), new PropertyMetadata(string.Empty));

    /// <summary>Stripe header text in Departure Mono caps (module code).</summary>
    public string Kicker
    {
        get => (string)GetValue(KickerProperty);
        set => SetValue(KickerProperty, value);
    }

    public static readonly DependencyProperty StripeVisibilityProperty = DependencyProperty.Register(
        nameof(StripeVisibility), typeof(Visibility), typeof(Faceplate), new PropertyMetadata(Visibility.Visible));

    /// <summary>Collapse for a plain plate without the stripe header.</summary>
    public Visibility StripeVisibility
    {
        get => (Visibility)GetValue(StripeVisibilityProperty);
        set => SetValue(StripeVisibilityProperty, value);
    }

    public static readonly DependencyProperty BoltsVisibilityProperty = DependencyProperty.Register(
        nameof(BoltsVisibility), typeof(Visibility), typeof(Faceplate), new PropertyMetadata(Visibility.Visible));

    /// <summary>Collapse the corner bolts (small plates under 160px wide).</summary>
    public Visibility BoltsVisibility
    {
        get => (Visibility)GetValue(BoltsVisibilityProperty);
        set => SetValue(BoltsVisibilityProperty, value);
    }
}
