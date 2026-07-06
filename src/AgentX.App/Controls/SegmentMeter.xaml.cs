using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace AgentX.App.Controls;

/// <summary>
/// A Command Console segment meter: green zone to 60%, amber to 85%,
/// red above (steady tones only - the blink form stays reserved for
/// unacknowledged warnings per the dual-form rule).
/// </summary>
public sealed partial class SegmentMeter : UserControl
{
    private static readonly UISettings UiSettings = new();

    // Shift-invariant lamp tones (the meter window is a dark display).
    private static readonly Color GoColor = Color.FromArgb(0xFF, 0x41, 0xE2, 0x5E);
    private static readonly Color HoldColor = Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00);
    private static readonly Color HotColor = Color.FromArgb(0xFF, 0xC8, 0x45, 0x3E);
    private static readonly Color UnlitColor = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);

    private Rectangle[] _segments = Array.Empty<Rectangle>();
    private double _displayedValue;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _ballisticsTimer;

    public SegmentMeter()
    {
        InitializeComponent();
        Loaded += (_, _) => { BuildSegments(); SnapOrAnimate(); };
        Unloaded += (_, _) => StopBallistics();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SegmentMeter),
        new PropertyMetadata(0d, static (d, _) => ((SegmentMeter)d).SnapOrAnimate()));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SegmentMeter),
        new PropertyMetadata(100d, static (d, _) => ((SegmentMeter)d).SnapOrAnimate()));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty SegmentCountProperty = DependencyProperty.Register(
        nameof(SegmentCount), typeof(int), typeof(SegmentMeter),
        new PropertyMetadata(12, static (d, _) => { var m = (SegmentMeter)d; m.BuildSegments(); m.SnapOrAnimate(); }));

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    private static bool AnimationsEnabled
    {
        get
        {
            try { return UiSettings.AnimationsEnabled; }
            catch { return true; }
        }
    }

    private void BuildSegments()
    {
        var count = Math.Max(1, SegmentCount);
        SegmentHost.Children.Clear();
        SegmentHost.ColumnDefinitions.Clear();
        _segments = new Rectangle[count];
        for (var i = 0; i < count; i++)
        {
            SegmentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var seg = new Rectangle
            {
                RadiusX = 1,
                RadiusY = 1,
                Fill = new SolidColorBrush(UnlitColor),
            };
            Grid.SetColumn(seg, i);
            SegmentHost.Children.Add(seg);
            _segments[i] = seg;
        }
    }

    private void SnapOrAnimate()
    {
        if (_segments.Length == 0) return;

        if (!AnimationsEnabled)
        {
            _displayedValue = Value;
            Render();
            return;
        }

        // IEC-style ballistics: exponential approach, ~95% in 300ms.
        if (_ballisticsTimer is null)
        {
            _ballisticsTimer = DispatcherQueue.CreateTimer();
            _ballisticsTimer.Interval = TimeSpan.FromMilliseconds(33);
            _ballisticsTimer.Tick += (_, _) =>
            {
                var target = Value;
                var delta = target - _displayedValue;
                if (Math.Abs(delta) < 0.5)
                {
                    _displayedValue = target;
                    Render();
                    StopBallistics();
                    return;
                }
                _displayedValue += delta * 0.28; // tau ~ 100ms at 33ms ticks
                Render();
            };
        }
        _ballisticsTimer.Start();
    }

    private void StopBallistics() => _ballisticsTimer?.Stop();

    private void Render()
    {
        var max = Maximum <= 0 ? 100d : Maximum;
        var fraction = Math.Clamp(_displayedValue / max, 0d, 1d);
        var lit = (int)Math.Round(fraction * _segments.Length);
        for (var i = 0; i < _segments.Length; i++)
        {
            var zone = (i + 1) / (double)_segments.Length;
            var color = i < lit
                ? zone <= 0.6 ? GoColor : zone <= 0.85 ? HoldColor : HotColor
                : UnlitColor;
            if (_segments[i].Fill is SolidColorBrush b) b.Color = color;
        }
    }
}
