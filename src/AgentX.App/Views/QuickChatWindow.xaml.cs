using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using AgentX.App.ViewModels;
using Serilog;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace AgentX.App.Views;

/// <summary>
/// Lightweight always-on-top overlay window for quick Q&A against the knowledge vault.
/// Activated by the Win+Shift+A global hotkey or the "Quick Chat" tray context menu.
/// Singleton — only one instance exists at a time; subsequent activations focus the existing window.
///
/// UI is built programmatically (no XAML) to avoid a WinUI 3 XAML compiler crash
/// that occurs when a secondary Window class has its own XAML file.
/// </summary>
public sealed class QuickChatWindow : Window
{
    private AppWindow _appWindow = null!;

    // ── UI Element References ──────────────────────────────────
    private ProgressRing _processingRing = null!;
    private TextBlock _statusTextBlock = null!;
    private ScrollViewer _responseScrollViewer = null!;
    private TextBlock _responseBlock = null!;
    private TextBox _queryInput = null!;
    private Button _askButton = null!;
    private Button _clearButton = null!;

    public QuickChatViewModel ViewModel { get; }

    public QuickChatWindow(QuickChatViewModel viewModel)
    {
        ViewModel = viewModel;

        Title = "Quick Chat";
        BuildUI();

        // Apply Mica backdrop to match the main window
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        else if (DesktopAcrylicController.IsSupported())
            SystemBackdrop = new DesktopAcrylicBackdrop();

        ConfigureOverlayWindow();
        WireViewModelBindings();

        Closed += OnWindowClosed;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UI CONSTRUCTION
    // ═══════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        var rootGrid = new Grid { RowSpacing = 0 };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Header Row ──────────────────────────────────────
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(16, 14, 16, 10)
        };

        var titleBlock = new TextBlock
        {
            Text = "Quick Chat",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        _processingRing = new ProgressRing { Width = 18, Height = 18, Visibility = Visibility.Collapsed };

        _statusTextBlock = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };

        headerPanel.Children.Add(titleBlock);
        headerPanel.Children.Add(_processingRing);
        headerPanel.Children.Add(_statusTextBlock);
        Grid.SetRow(headerPanel, 0);
        rootGrid.Children.Add(headerPanel);

        // ── Separator ───────────────────────────────────────
        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(headerPanel, 0);
        // Add separator inside header by using a sub-grid
        var headerGrid = new Grid { RowSpacing = 0 };
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headerPanel, 0);
        headerGrid.Children.Add(headerPanel);
        Grid.SetRow(separator, 1);
        headerGrid.Children.Add(separator);
        rootGrid.Children.Remove(headerPanel);
        Grid.SetRow(headerGrid, 0);
        rootGrid.Children.Add(headerGrid);

        // ── Response Area ───────────────────────────────────
        _responseBlock = new TextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 21
        };

        _responseScrollViewer = new ScrollViewer
        {
            Content = _responseBlock,
            Padding = new Thickness(16, 8, 16, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(_responseScrollViewer, 1);
        rootGrid.Children.Add(_responseScrollViewer);

        // ── Input Area ──────────────────────────────────────
        var inputSeparator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _queryInput = new TextBox
        {
            PlaceholderText = "Ask anything...",
            AcceptsReturn = false,
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _queryInput.KeyDown += QueryInput_KeyDown;

        _clearButton = new Button
        {
            Content = "Clear",
            Padding = new Thickness(16, 6, 16, 6),
            FontSize = 13
        };
        _clearButton.Click += ClearButton_Click;

        _askButton = new Button
        {
            Content = "Ask",
            Padding = new Thickness(24, 6, 24, 6),
            FontSize = 13
        };
        _askButton.Click += AskButton_Click;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttonPanel.Children.Add(_clearButton);
        buttonPanel.Children.Add(_askButton);

        var inputStack = new StackPanel
        {
            Padding = new Thickness(12, 8, 12, 12),
            Spacing = 8
        };
        inputStack.Children.Add(inputSeparator);
        inputStack.Children.Add(_queryInput);
        inputStack.Children.Add(buttonPanel);

        Grid.SetRow(inputStack, 2);
        rootGrid.Children.Add(inputStack);

        Content = rootGrid;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  WINDOW CONFIGURATION
    // ═══════════════════════════════════════════════════════════════════

    private void ConfigureOverlayWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Position at top-center of primary display
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - 480) / 2;
            _appWindow.Move(new PointInt32(centerX, 40));
        }

        _appWindow.Resize(new SizeInt32(480, 400));

        // Set as always-on-top (no resize, no maximize, no minimize)
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        Log.Debug("Quick Chat overlay window configured (480x400, top-center, always-on-top)");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VIEWMODEL BINDINGS
    // ═══════════════════════════════════════════════════════════════════

    private void WireViewModelBindings()
    {
        // Sync initial state
        _queryInput.Text = ViewModel.QueryText;
        _statusTextBlock.Text = ViewModel.StatusMessage;
        _responseBlock.Text = ViewModel.ResponseText;
        UpdateAskButtonState();

        // Two-way binding on query text
        _queryInput.TextChanged += (s, e) =>
        {
            ViewModel.QueryText = _queryInput.Text;
            UpdateAskButtonState();
        };

        // React to ViewModel property changes
        ViewModel.PropertyChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(ViewModel.ResponseText):
                        _responseBlock.Text = ViewModel.ResponseText;
                        // Auto-scroll to bottom as tokens stream in
                        _responseScrollViewer.ChangeView(null, _responseScrollViewer.ScrollableHeight, null);
                        break;
                    case nameof(ViewModel.StatusMessage):
                        _statusTextBlock.Text = ViewModel.StatusMessage;
                        break;
                    case nameof(ViewModel.IsProcessing):
                        _processingRing.IsActive = ViewModel.IsProcessing;
                        _processingRing.Visibility = ViewModel.IsProcessing
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                        _queryInput.IsEnabled = !ViewModel.IsProcessing;
                        UpdateAskButtonState();
                        break;
                }
            });
        };

        // Sync UI after Clear command fires
        ViewModel.ClearCommand.CanExecuteChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _queryInput.Text = ViewModel.QueryText;
                _responseBlock.Text = ViewModel.ResponseText;
                _statusTextBlock.Text = ViewModel.StatusMessage;
                UpdateAskButtonState();
            });
        };

        // React to SubmitQueryCommand CanExecute changes
        ViewModel.SubmitQueryCommand.CanExecuteChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(UpdateAskButtonState);
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════════════════

    private void QueryInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (ViewModel.SubmitQueryCommand.CanExecute(null))
            {
                ViewModel.SubmitQueryCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CancelQuery();
            Close();
            e.Handled = true;
        }
    }

    private void AskButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SubmitQueryCommand.CanExecute(null))
            ViewModel.SubmitQueryCommand.Execute(null);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearCommand.Execute(null);
        _queryInput.Text = ViewModel.QueryText;
        _responseBlock.Text = ViewModel.ResponseText;
        _statusTextBlock.Text = ViewModel.StatusMessage;
        UpdateAskButtonState();
    }

    private void UpdateAskButtonState()
    {
        _askButton.IsEnabled = ViewModel.SubmitQueryCommand.CanExecute(null);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        ViewModel.CancelQuery();
        Log.Debug("Quick Chat overlay window closed");
    }
}