using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Chat.Models;
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

        // Stop any active voice recording when navigating away
        if (ViewModel.IsRecording)
        {
            ViewModel.ToggleVoiceRecordingCommand.Execute(null);
        }

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
    // SUGGESTED QUESTIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles click events on suggested follow-up question buttons,
    /// populating the input box with the selected question text.
    /// </summary>
    private void OnSuggestedQuestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Content is string question)
        {
            ViewModel.UseSuggestedQuestionCommand.Execute(question);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PER-MESSAGE ACTION HANDLERS (#18, #19)
    // ═══════════════════════════════════════════════════════════════

    private void OnCopyMessageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.CopyMessageCommand.Execute(message.Content);
        }
    }

    private void OnDeleteMessageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.DeleteMessageCommand.Execute(message);
        }
    }

    private void OnRegenerateMessageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.RegenerateMessageCommand.Execute(message);
        }
    }

    private void OnThumbsUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.ThumbsUpCommand.Execute(message);
        }
    }

    private void OnThumbsDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.ThumbsDownCommand.Execute(message);
        }
    }

    private void OnEditMessageClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.StartEditMessageCommand.Execute(message);
        }
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.CancelEditMessageCommand.Execute(message);
        }
    }

    private void OnSaveEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.SaveEditMessageCommand.Execute(message);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // VOICE INPUT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Right-click on mic button opens the audio file picker for transcribing
    /// an existing audio file (alternative to live recording).
    /// </summary>
    private void OnMicRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.PickAudioFileCommand.Execute(null);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOLDER ORGANIZATION
    // ═══════════════════════════════════════════════════════════════

    private void OnFolderFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var folder = btn.Tag as string;
            ViewModel.FilterByFolderCommand.Execute(string.IsNullOrEmpty(folder) ? null : folder);
        }
    }

    private void OnSetFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var folder = btn.Tag as string;
            ViewModel.SetConversationFolderCommand.Execute(string.IsNullOrEmpty(folder) ? null : folder);
        }
    }

    private void OnCustomFolderKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox textBox)
        {
            var folder = textBox.Text?.Trim();
            if (!string.IsNullOrEmpty(folder))
            {
                ViewModel.SetConversationFolderCommand.Execute(folder);
                textBox.Text = string.Empty;
            }
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CONVERSATION BRANCHING
    // ═══════════════════════════════════════════════════════════════

    private async void BranchFromMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem msg)
        {
            var input = new TextBox { PlaceholderText = "Branch label (optional)", Width = 300 };
            var dialog = new ContentDialog
            {
                Title = "Create Branch",
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Give this branch an optional label:", Margin = new(0, 0, 0, 8) },
                        input
                    }
                },
                PrimaryButtonText = "Branch",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.PendingBranchLabel = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text;
                await ViewModel.BranchFromMessageCommand.ExecuteAsync(msg.MessageId);
            }
        }
    }

    private async void BranchTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ConversationBranchTree node)
        {
            await ViewModel.SwitchToBranchCommand.ExecuteAsync(node.Conversation.Id);
        }
    }

    private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is long branchId)
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Branch",
                Content = "Delete this branch and all its sub-branches?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteBranchCommand.ExecuteAsync(branchId);
            }
        }
    }

    private async void MergeBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is long branchId)
        {
            var rootId = ViewModel.BranchTree?.Conversation.Id;
            if (rootId == null) return;

            var dialog = new ContentDialog
            {
                Title = "Merge to Main Thread",
                Content = "Merge all messages from this branch into the main conversation?",
                PrimaryButtonText = "Merge",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var request = new MergeBranchRequest(branchId, rootId.Value);
                await ViewModel.MergeToMainCommand.ExecuteAsync(request);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EXPORT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the ExportDialog for the active conversation.
    /// Replaces the previous MenuFlyout approach with a full-featured
    /// dialog that supports all 8 export formats and 3 built-in templates.
    /// </summary>
    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveConversationId is null) return;

        var exportVm = App.GetService<ExportViewModel>();
        var dialog = new ExportDialog(exportVm);
        dialog.SetConversation(
            ViewModel.ActiveConversationId.Value,
            ViewModel.ActiveConversationTitle ?? "Conversation");
        dialog.XamlRoot = this.XamlRoot;

        await dialog.ShowAsync();
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
