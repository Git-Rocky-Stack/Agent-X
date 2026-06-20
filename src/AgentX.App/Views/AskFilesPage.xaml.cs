using AgentX.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
using Windows.System;

namespace AgentX.App.Views;

/// <summary>
/// Premium "Ask Your Files" page with RAG-powered chat interface,
/// streaming AI answers with citations, and a collapsible citations sidebar.
/// Integrates with AskFilesViewModel for all data binding and command execution.
/// </summary>
public sealed partial class AskFilesPage : Page
{
    private DispatcherTimer? _cursorBlinkTimer;

    public AskFilesViewModel ViewModel { get; }

    public AskFilesPage()
    {
        ViewModel = App.GetService<AskFilesViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // =================================================================
    // LIFECYCLE
    // =================================================================

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("AskFilesPage loaded");

        await ViewModel.InitializeAsync();

        // Start cursor blink timer for streaming effect
        StartCursorBlinkTimer();

        // Subscribe to messages for auto-scroll
        ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;

        // Focus the input box for immediate typing
        QuestionInputBox.Focus(FocusState.Programmatic);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("AskFilesPage unloaded");

        StopCursorBlinkTimer();
        ViewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
    }

    // =================================================================
    // KEYBOARD INPUT HANDLING
    // =================================================================

    /// <summary>
    /// Handles Enter key in the question input TextBox to trigger Ask.
    /// Shift+Enter inserts a new line.
    /// </summary>
    private void QuestionInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var shiftState = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Shift);
            var shiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

            if (!shiftPressed)
            {
                e.Handled = true;

                if (ViewModel.AskCommand.CanExecute(null))
                {
                    ViewModel.AskCommand.Execute(null);
                }
            }
        }
    }

    // =================================================================
    // AUTO-SCROLL
    // =================================================================

    /// <summary>
    /// Scrolls to the bottom of the messages list when new messages are added
    /// or streaming content is updated.
    /// </summary>
    private void OnMessagesCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (MessagesScrollViewer is not null)
                {
                    MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-scroll Ask Files messages");
            }
        });
    }

    // =================================================================
    // STREAMING CURSOR BLINK EFFECT
    // =================================================================

    private void StartCursorBlinkTimer()
    {
        _cursorBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(530)
        };
        _cursorBlinkTimer.Tick += OnCursorBlinkTick;
        _cursorBlinkTimer.Start();
    }

    private void StopCursorBlinkTimer()
    {
        if (_cursorBlinkTimer is not null)
        {
            _cursorBlinkTimer.Stop();
            _cursorBlinkTimer.Tick -= OnCursorBlinkTick;
            _cursorBlinkTimer = null;
        }
    }

    private void OnCursorBlinkTick(object? sender, object e)
    {
        if (!ViewModel.IsGenerating) return;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                // Trigger scroll update to keep chat at bottom during streaming
                if (MessagesScrollViewer is not null && ViewModel.IsGenerating)
                {
                    MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
                }
            }
            catch
            {
                // Cursor blink is non-critical visual effect
            }
        });
    }

    // =================================================================
    // COLLECTION SCOPE SELECTION
    // =================================================================

    /// <summary>
    /// Handles collection scope ComboBox selection changes.
    /// </summary>
    private void OnCollectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionScopeCombo.SelectedItem is CollectionOption option)
        {
            if (ViewModel.SelectCollectionCommand.CanExecute(option.Id))
            {
                ViewModel.SelectCollectionCommand.Execute(option.Id);
            }
        }
    }

    // =================================================================
    // EXAMPLE QUESTION CLICK
    // =================================================================

    /// <summary>
    /// Fills the question input with an example question when clicked.
    /// </summary>
    private void OnExampleQuestionClick(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string question })
        {
            ViewModel.QuestionText = question;
            QuestionInputBox.Focus(FocusState.Programmatic);
        }
    }

    // =================================================================
    // CITATION ACTIONS
    // =================================================================

    /// <summary>
    /// Opens a citation's source document in Windows Explorer.
    /// </summary>
    private void OnOpenCitationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath })
        {
            if (ViewModel.OpenCitationCommand.CanExecute(filePath))
            {
                ViewModel.OpenCitationCommand.Execute(filePath);
            }
        }
    }

    // =================================================================
    // STATIC HELPERS for x:Bind in DataTemplate
    // =================================================================

    /// <summary>
    /// Returns Visibility.Visible if pageNumber is not null, else Collapsed.
    /// </summary>
    public static Visibility PageNumberToVisibility(int? pageNumber)
    {
        return pageNumber.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Returns Visibility.Visible if the citations list has items.
    /// </summary>
    public static Visibility HasCitationsVisibility(List<CitationItem> citations)
    {
        return citations is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }
}
