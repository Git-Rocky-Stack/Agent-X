using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
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
    // The frame caches this page (NavigationCacheMode="Enabled") and only builds a new instance
    // after it has evicted the previous one. The evicted page's view model is still subscribed
    // to the singleton chat coordinators, so it keeps reacting to every generation (duplicate
    // toasts, duplicate learning) until it is released here.
    private static ChatViewModel? s_liveViewModel;

    private readonly IShortcutRegistry _shortcutRegistry;
    private IDisposable? _shortcutScope;
    private DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorVisible = true;

    // The sidebar row whose context menu is open.
    private ConversationListItem? _contextConversation;

    public ChatViewModel ViewModel { get; }

    public ChatPage()
    {
        ViewModel = PageViewModelFactory.Create<ChatViewModel>();
        Interlocked.Exchange(ref s_liveViewModel, ViewModel)?.Dispose();
        _shortcutRegistry = App.GetService<IShortcutRegistry>();
        InitializeComponent();

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Honour the item the caller picked (Jump-To, command palette) rather than
        // dropping it and opening this page on whatever was last active.
        _ = ViewModel.ApplyNavigationParameterAsync(e.Parameter);

        _shortcutScope = _shortcutRegistry.RegisterShortcuts(
            new AgentX.Core.Services.Shortcuts.ShortcutDescriptor(
                "chat.new",
                "New conversation",
                new ShortcutScope(nameof(ChatPage)),
                new[] { new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.N) },
                _ => ViewModel.NewConversationCommand.ExecuteAsync(null),
                "Chat"),
            new AgentX.Core.Services.Shortcuts.ShortcutDescriptor(
                "chat.toggle-pane",
                "Toggle conversation pane",
                new ShortcutScope(nameof(ChatPage)),
                new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.B) },
                _ =>
                {
                    ViewModel.ToggleConversationPaneCommand.Execute(null);
                    return Task.CompletedTask;
                },
                "Chat"));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutScope?.Dispose();
        _shortcutScope = null;
    }

    // ═══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("ChatPage loaded");

        // Initialize the ViewModel
        await ViewModel.InitializeAsync();
        SyncConversationSelection();

        // Start the streaming cursor blink timer
        StartCursorBlinkTimer();

        // Subscribe to collection changes for auto-scroll
        ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;

        // GEN lamp: steady armed while the model is generating (red = LIVE)
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateGenLamp();

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
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsGenerating))
        {
            UpdateGenLamp();
        }
        else if (e.PropertyName == nameof(ChatViewModel.ActiveConversationId))
        {
            SyncConversationSelection();
        }
    }

    private void UpdateGenLamp()
        => GenLamp.State = ViewModel.IsGenerating ? Controls.LampState.Armed : Controls.LampState.Off;

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

    private void OnInspectInlineContextClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChatMessageItem message)
        {
            ViewModel.InspectInlineContextCommand.Execute(message);
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
    /// <summary>
    /// Exports every listed conversation in one pass. The export dialog is scoped to the
    /// open thread, so a whole-history export had no route before this.
    /// </summary>
    private async void OnExportAllConversationsClick(object sender, RoutedEventArgs e)
    {
        var conversationIds = ViewModel.Conversations.Select(conversation => conversation.Id).ToList();
        if (conversationIds.Count == 0)
        {
            return;
        }

        var exportViewModel = App.GetService<ExportViewModel>();
        await exportViewModel.ExportConversationsCommand.ExecuteAsync(
            new ExportBatchRequest(conversationIds, Title: "All Conversations"));

        Log.Information(
            "Batch export of {Count} conversations finished: {Status}",
            conversationIds.Count,
            exportViewModel.StatusMessage);
    }

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

    /// <summary>
    /// Applies the model chosen in the header picker. The ComboBox only reported its
    /// selection to itself before this, so switching models silently did nothing.
    /// </summary>
    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is AgentX.Core.AI.Models.AiModel model)
        {
            ViewModel.SelectModelCommand.Execute(model.Id);
        }
    }

    /// <summary>
    /// Applies the prompt picked in the system prompt flyout and closes the flyout.
    /// </summary>
    private void OnSystemPromptItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SystemPromptItem prompt)
        {
            ViewModel.SelectSystemPromptCommand.Execute(prompt);
            SystemPromptFlyout.Hide();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CONVERSATION LIST SELECTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the conversation clicked (or invoked with Enter) in the sidebar.
    /// </summary>
    private void OnConversationItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ConversationListItem item &&
            item.Id != ViewModel.ActiveConversationId &&
            ViewModel.SelectConversationCommand.CanExecute(item.Id))
        {
            ViewModel.SelectConversationCommand.Execute(item.Id);
        }
    }

    /// <summary>
    /// Keeps the highlighted sidebar row on the open conversation, however it was opened
    /// (sidebar, Jump-To, branch switch) or closed (New conversation, delete).
    /// </summary>
    private void SyncConversationSelection()
    {
        var active = ViewModel.ActiveConversationId;
        ConversationListView.SelectedItem = active is null
            ? null
            : ViewModel.Conversations.FirstOrDefault(conversation => conversation.Id == active);
    }

    /// <summary>
    /// Resolves the row the menu was opened on and offers Pin or Unpin to match it.
    /// </summary>
    private void OnConversationRowFlyoutOpening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        _contextConversation = flyout.Target is ListViewItem container
            ? ConversationListView.ItemFromContainer(container) as ConversationListItem
            : null;

        if (_contextConversation is null)
        {
            return;
        }

        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            item.Visibility = (item.Tag as string) switch
            {
                "Pin" => _contextConversation.IsPinned ? Visibility.Collapsed : Visibility.Visible,
                "Unpin" => _contextConversation.IsPinned ? Visibility.Visible : Visibility.Collapsed,
                _ => Visibility.Visible
            };
        }
    }

    private void OnTogglePinConversationClick(object sender, RoutedEventArgs e)
    {
        if (_contextConversation is { } conversation)
        {
            ViewModel.TogglePinCommand.Execute(conversation.Id);
        }
    }

    /// <summary>
    /// Deletes the row's conversation after confirmation. Deleting cannot be undone; branches
    /// made from the conversation are kept as conversations of their own.
    /// </summary>
    private async void OnDeleteConversationClick(object sender, RoutedEventArgs e)
    {
        if (_contextConversation is not { } conversation)
        {
            return;
        }

        var localization = App.GetService<ILocalizationService>();
        var dialog = new ContentDialog
        {
            Title = localization.GetString("Chat_DeleteConversationTitle"),
            Content = localization.GetString("Chat_DeleteConversationBody", conversation.Title),
            PrimaryButtonText = localization.GetString("Chat_DeleteConversationConfirm"),
            CloseButtonText = localization.GetString("Chat_DeleteConversationCancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteConversationCommand.ExecuteAsync(conversation.Id);
        }
    }
}
