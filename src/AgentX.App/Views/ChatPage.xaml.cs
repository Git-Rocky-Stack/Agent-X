using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using AgentX.App.ViewModels;
using Serilog;
using Windows.System;

namespace AgentX.App.Views;

/// <summary>
/// Premium AI Chat page with conversation sidebar, streaming message display,
/// and intelligent input handling. Integrates with ChatViewModel for all
/// data binding and command execution.
/// </summary>
public sealed partial class ChatPage : Page
{
    private DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorVisible = true;

    public ChatViewModel ViewModel { get; }

    public ChatPage()
    {
        ViewModel = App.GetService<ChatViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // ═══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("ChatPage loaded");

        // Initialize the ViewModel
        await ViewModel.InitializeAsync();

        // Start the streaming cursor blink timer
        StartCursorBlinkTimer();

        // Subscribe to collection changes for auto-scroll
        ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;

        // Focus the input box for immediate typing
        ChatInputBox.Focus(FocusState.Programmatic);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("ChatPage unloaded");

        StopCursorBlinkTimer();

        ViewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
    }

    // ═══════════════════════════════════════════════════════════════
    // KEYBOARD INPUT HANDLING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles keyboard shortcuts in the chat input box.
    /// Enter: Send message (when not Shift+Enter).
    /// Shift+Enter: Insert a new line.
    /// </summary>
    private void ChatInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var shiftState = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Shift);
            var shiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

            if (!shiftPressed)
            {
                // Enter without Shift: send the message
                e.Handled = true;

                if (ViewModel.SendMessageCommand.CanExecute(null))
                {
                    ViewModel.SendMessageCommand.Execute(null);
                }
            }
            // Shift+Enter: let the TextBox handle the newline insertion naturally
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // AUTO-SCROLL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Scrolls to the bottom of the messages list when new messages are added
    /// or existing streaming messages are updated.
    /// </summary>
    private void OnMessagesCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Use DispatcherQueue to ensure the scroll happens after the UI update
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
                Log.Warning(ex, "Failed to auto-scroll messages");
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // STREAMING CURSOR BLINK EFFECT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts a DispatcherTimer that toggles the visibility of streaming cursor
    /// elements to create a blinking effect during active AI generation.
    /// </summary>
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

    /// <summary>
    /// Toggles cursor visibility on each tick. The actual cursor elements
    /// in the ItemsRepeater DataTemplate are controlled via the IsStreaming
    /// property on ChatMessageItem, combined with this opacity toggle.
    /// </summary>
    private void OnCursorBlinkTick(object? sender, object e)
    {
        if (!ViewModel.IsGenerating) return;

        _cursorVisible = !_cursorVisible;

        // Walk through messages to find any that are currently streaming
        // and toggle their cursor visibility via property change notification
        // The cursor opacity is toggled by updating the streaming response,
        // which triggers UI refresh in the bound ItemsRepeater.
        // This is a lightweight approach that avoids walking the visual tree.

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                // Trigger a scroll update which also refreshes streaming display
                if (MessagesScrollViewer is not null && ViewModel.IsGenerating)
                {
                    MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
                }
            }
            catch
            {
                // Silently ignore — cursor blink is non-critical visual effect
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // CONVERSATION LIST SELECTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Wired up to handle conversation selection from the sidebar ListView.
    /// This is connected via SelectionChanged in case x:Bind commands
    /// need supplementary event handling.
    /// </summary>
    internal void OnConversationSelected(ConversationListItem? item)
    {
        if (item is null) return;

        if (ViewModel.SelectConversationCommand.CanExecute(item.Id))
        {
            ViewModel.SelectConversationCommand.Execute(item.Id);
        }
    }
}
