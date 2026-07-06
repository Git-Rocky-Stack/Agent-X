using System;
using AgentX.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace AgentX.App.Controls;

/// <summary>LED semantics for a lamp tile (DESIGN.md dual-form red rule).</summary>
public enum LampState
{
    /// <summary>Unlit: graphite stencil, no tint, no glow.</summary>
    Off,
    /// <summary>Steady green: running / healthy.</summary>
    Go,
    /// <summary>Steady amber: caution / pending / queued.</summary>
    Hold,
    /// <summary>Blinking hot red at 1Hz until acknowledged. Never steady.</summary>
    Warn,
    /// <summary>Steady deep red: terminal fault. Ignites once, then holds.</summary>
    NoGo,
    /// <summary>Steady cyan: informational. The rarest color.</summary>
    Scope,
    /// <summary>Steady armed red: LIVE / command authority.</summary>
    Armed,
}

/// <summary>
/// A Command Console lamp tile: dark cap, Archivo stencil word, LED
/// state with strike ignition, 1Hz blink-until-acknowledge for warnings,
/// and an Invoked event so lit lamps can teleport to their source view.
/// </summary>
public sealed partial class LampTile : UserControl
{
    private static readonly UISettings UiSettings = new();

    // Night LED values (shift-invariant on the dark cap; HC swaps the
    // foreground brushes automatically and skips tint + glow).
    private static readonly Color GoColor = Color.FromArgb(0xFF, 0x41, 0xE2, 0x5E);
    private static readonly Color HoldColor = Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00);
    private static readonly Color WarnColor = Color.FromArgb(0xFF, 0xFF, 0x44, 0x38);
    private static readonly Color NoGoColor = Color.FromArgb(0xFF, 0xC8, 0x45, 0x3E);
    private static readonly Color ScopeColor = Color.FromArgb(0xFF, 0x58, 0xC4, 0xBC);
    private static readonly Color ArmedColor = Color.FromArgb(0xFF, 0xE0, 0x25, 0x2B);

    private Storyboard? _blinkStoryboard;
    private Storyboard? _strikeStoryboard;
    private bool _acknowledged;
    private bool _glowAttached;

    /// <summary>Raised when a blinking warning is clicked (the ack ritual).</summary>
    public event EventHandler? Acknowledged;

    /// <summary>Raised on click/Enter/Space outside the ack ritual - consumers teleport.</summary>
    public event EventHandler? Invoked;

    public LampTile()
    {
        InitializeComponent();

        Tapped += OnTapped;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => ApplyState(animateStrike: false);
    }

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(LampTile),
        new PropertyMetadata(string.Empty, static (d, _) => ((LampTile)d).OnCodeChanged()));

    /// <summary>The 2-5 letter stencil word (equipment vocabulary, not localized).</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(LampTile),
        new PropertyMetadata(LampState.Off, static (d, e) => ((LampTile)d).OnStateChanged((LampState)e.OldValue)));

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(LampTile),
        new PropertyMetadata(false, static (d, _) => ((LampTile)d).OnCompactChanged()));

    /// <summary>Compact 19px cap for dense strips (standard is 26px).</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private static bool AnimationsEnabled
    {
        get
        {
            try { return UiSettings.AnimationsEnabled; }
            catch { return true; }
        }
    }

    private void OnCodeChanged()
    {
        CodeText.Text = Code ?? string.Empty;
        UpdateAutomationName();
    }

    private void OnCompactChanged()
    {
        Cap.Height = IsCompact ? 19 : 26;
        Cap.Padding = IsCompact ? new Thickness(8, 0, 8, 0) : new Thickness(10, 0, 10, 0);
        CodeText.FontSize = IsCompact ? 9 : 10;
    }

    private void OnStateChanged(LampState oldState)
    {
        _acknowledged = false;
        var goingLive = oldState == LampState.Off && State != LampState.Off;
        ApplyState(animateStrike: goingLive);
    }

    private void ApplyState(bool animateStrike)
    {
        StopStoryboards();

        var (brushKey, color, lit) = State switch
        {
            LampState.Go => ("LedGoLampBrush", GoColor, true),
            LampState.Hold => ("LedHoldLampBrush", HoldColor, true),
            LampState.Warn => ("LedWarnLampBrush", WarnColor, true),
            LampState.NoGo => ("LedNoGoLampBrush", NoGoColor, true),
            LampState.Scope => ("LedScopeLampBrush", ScopeColor, true),
            LampState.Armed => ("AccentLitBrush", ArmedColor, true),
            _ => ("TextDisabledBrush", default, false),
        };

        if (Application.Current.Resources.TryGetValue(brushKey, out var brush) && brush is Brush fg)
        {
            CodeText.Foreground = fg;
        }

        if (lit)
        {
            Tint.Background = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B));
            Tint.Opacity = 1;
            EnsureGlow(color);
        }
        else
        {
            Tint.Opacity = 0;
            CompositionGlow.SetColor(CodeText, Microsoft.UI.Colors.Transparent);
        }

        UpdateAutomationName();

        if (!lit)
        {
            LampContent.Opacity = 1;
            return;
        }

        if (State == LampState.Warn && !_acknowledged)
        {
            if (AnimationsEnabled) StartBlink();
            else LampContent.Opacity = 1;
            return;
        }

        if (animateStrike && AnimationsEnabled) StartStrike();
        else LampContent.Opacity = 0.85;
    }

    private void EnsureGlow(Color color)
    {
        if (!_glowAttached)
        {
            CompositionGlow.Attach(CodeText, GlowHost, color);
            _glowAttached = true;
        }
        else
        {
            CompositionGlow.SetColor(CodeText, color);
        }
    }

    /// <summary>The strike: 80ms ignition attack with bloom, 240ms settle to 85%.</summary>
    private void StartStrike()
    {
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.2 });
        anim.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(80),
            Value = 1.0,
            KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(0.2, 0.7), ControlPoint2 = new Windows.Foundation.Point(0.3, 1.0) },
        });
        anim.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(320),
            Value = 0.85,
            KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(0.2, 0.7), ControlPoint2 = new Windows.Foundation.Point(0.3, 1.0) },
        });
        Storyboard.SetTarget(anim, LampContent);
        Storyboard.SetTargetProperty(anim, "Opacity");

        _strikeStoryboard = new Storyboard();
        _strikeStoryboard.Children.Add(anim);
        _strikeStoryboard.Begin();
    }

    /// <summary>1Hz master-caution blink; runs until the lamp is clicked.</summary>
    private void StartBlink()
    {
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(500), Value = 0.25 });
        Storyboard.SetTarget(anim, LampContent);
        Storyboard.SetTargetProperty(anim, "Opacity");

        _blinkStoryboard = new Storyboard();
        _blinkStoryboard.Children.Add(anim);
        _blinkStoryboard.Begin();
    }

    private void StopStoryboards()
    {
        _blinkStoryboard?.Stop();
        _blinkStoryboard = null;
        _strikeStoryboard?.Stop();
        _strikeStoryboard = null;
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e) => HandleActivation();

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            HandleActivation();
            e.Handled = true;
        }
    }

    private void HandleActivation()
    {
        if (State == LampState.Warn && !_acknowledged)
        {
            // The ack ritual: the click answers the blink. It does not teleport.
            _acknowledged = true;
            StopStoryboards();
            LampContent.Opacity = 1;
            UpdateAutomationName();
            Acknowledged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Invoked?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAutomationName()
    {
        var state = State == LampState.Warn && !_acknowledged ? "WARN unacknowledged" : State.ToString();
        AutomationProperties.SetName(this, $"{Code} lamp: {state}");
    }
}
