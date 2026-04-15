# Conversation Branching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add conversation branching UI to Agent-X — fork from any message, visualize branch trees, compare branches side-by-side, merge insights back to main thread.

**Architecture:** The backend (`ConversationBranchService`) already exists with 8 methods and full data model. This plan is UI-only: inject `IConversationBranchService` into `ChatViewModel`, add branch commands and properties, extend `ChatPage.xaml` with branch indicators and a branch tree sidebar, build a side-by-side comparison flyout, and create a merge dialog. `ConversationDto` gets branching fields so the conversation list can show branch counts.

**Tech Stack:** C#, .NET 8, WinUI 3, CommunityToolkit.Mvvm, xUnit

---

### Task 1: Update ConversationDto and ChatViewModel Branch Integration

**Files:**
- Modify: `src/AgentX.Core/DTOs/ConversationDto.cs`
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/DTOs/ConversationDtoTests.cs
using AgentX.Core.DTOs;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.DTOs;

public class ConversationDtoTests
{
    [Fact]
    public void ConversationDto_HasBranchingFields()
    {
        var dto = new ConversationDto
        {
            Id = 1,
            Title = "Test",
            ParentConversationId = 5,
            BranchPointMessageId = 42,
            BranchLabel = "Alt approach",
            BranchCount = 2
        };

        dto.ParentConversationId.Should().Be(5);
        dto.BranchPointMessageId.Should().Be(42);
        dto.BranchLabel.Should().Be("Alt approach");
        dto.BranchCount.Should().Be(2);
    }

    [Fact]
    public void ConversationDto_DefaultBranchingFields_AreNull()
    {
        var dto = new ConversationDto { Id = 1, Title = "Test" };

        dto.ParentConversationId.Should().BeNull();
        dto.BranchPointMessageId.Should().BeNull();
        dto.BranchLabel.Should().BeNull();
        dto.BranchCount.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AgentX.Tests --filter "ConversationDtoTests" -v n -r win-x64`
Expected: FAIL — `ConversationDto` does not have `ParentConversationId`, `BranchPointMessageId`, `BranchLabel`, or `BranchCount` properties.

- [ ] **Step 3: Add branching fields to ConversationDto**

```csharp
// Add to src/AgentX.Core/DTOs/ConversationDto.cs
public long? ParentConversationId { get; set; }
public long? BranchPointMessageId { get; set; }
public string? BranchLabel { get; set; }
public int BranchCount { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AgentX.Tests --filter "ConversationDtoTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 5: Inject IConversationBranchService into ChatViewModel**

Add `IConversationBranchService` to the `ChatViewModel` constructor. Store it in a readonly field `_branchService`. This does not change any existing behavior — it only makes the service available for later tasks.

```csharp
// In ChatViewModel constructor, add parameter:
private readonly IConversationBranchService _branchService;

public ChatViewModel(
    IChatService chatService,
    IConversationService conversationService,
    IAiService aiService,
    IModelManager modelManager,
    ISystemPromptService systemPromptService,
    IConversationMemoryService memoryService,
    IFeedbackService feedbackService,
    INotificationService notificationService,
    ITranscriptionService transcriptionService,
    IConversationBranchService branchService)  // NEW
{
    _branchService = branchService;
    // ... existing initialization unchanged
}
```

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.Core/DTOs/ConversationDto.cs src/AgentX.App/ViewModels/ChatViewModel.cs tests/AgentX.Tests/DTOs/ConversationDtoTests.cs
git commit -m "feat(branching): add DTO fields and inject IConversationBranchService into ChatViewModel"
```

---

### Task 2: Branch Commands and Message Actions

**Files:**
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Test: `tests/AgentX.Tests/ViewModels/ChatViewModelBranchingTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AgentX.Tests/ViewModels/ChatViewModelBranchingTests.cs
using AgentX.App.ViewModels;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Data.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class ChatViewModelBranchingTests
{
    [Fact]
    public async Task BranchFromMessageAsync_CreatesBranch()
    {
        // Arrange
        var branchService = new Mock<IConversationBranchService>();
        branchService
            .Setup(b => b.BranchAtMessageAsync(1, 10, It.IsAny<string?>(), default))
            .ReturnsAsync(new ConversationEntity { Id = 2, Title = "Branch 1" });

        var vm = ChatViewModelTestHelper.CreateViewModel(branchService.Object);
        vm.SetActiveConversation(1, "Test Conversation");

        // Act
        await vm.BranchFromMessageAsync(10, "Alt approach");

        // Assert
        branchService.Verify(b => b.BranchAtMessageAsync(1, 10, "Alt approach", default), Times.Once);
    }

    [Fact]
    public async Task LoadBranchTreeAsync_PopulatesBranchTree()
    {
        // Arrange
        var branchService = new Mock<IConversationBranchService>();
        var tree = new ConversationBranchTree
        {
            Conversation = new ConversationEntity { Id = 1, Title = "Root" },
            Children = new List<ConversationBranchTree>
            {
                new() { Conversation = new ConversationEntity { Id = 2, Title = "Branch A" }, BranchLabel = "Alt" }
            }
        };
        branchService.Setup(b => b.GetBranchTreeAsync(1, default)).ReturnsAsync(tree);

        var vm = ChatViewModelTestHelper.CreateViewModel(branchService.Object);
        vm.SetActiveConversation(1, "Test");

        // Act
        await vm.LoadBranchTreeAsync();

        // Assert
        vm.BranchTree.Should().NotBeNull();
        vm.BranchTree!.Children.Should().HaveCount(1);
    }

    [Fact]
    public async Task MergeToMainAsync_CallsService()
    {
        // Arrange
        var branchService = new Mock<IConversationBranchService>();
        var vm = ChatViewModelTestHelper.CreateViewModel(branchService.Object);
        var messageIds = new List<long> { 20, 21 };

        // Act
        await vm.MergeToMainAsync(2, messageIds, 1);

        // Assert
        branchService.Verify(
            b => b.MergeMessagesAsync(2, messageIds, 1, default),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AgentX.Tests --filter "ChatViewModelBranchingTests" -v n -r win-x64`
Expected: FAIL — methods `BranchFromMessageAsync`, `LoadBranchTreeAsync`, `MergeToMainAsync` don't exist; `BranchTree` property doesn't exist; `SetActiveConversation` may not exist.

- [ ] **Step 3: Add branch commands and properties to ChatViewModel**

Add the following to `ChatViewModel`:

```csharp
// Properties
public ConversationBranchTree? BranchTree
{
    get => _branchTree;
    set => SetProperty(ref _branchTree, value);
}
private ConversationBranchTree? _branchTree;

public bool HasBranches => BranchTree?.TotalBranchCount > 0;

public ObservableCollection<ConversationBranchTree> ActiveBranches { get; } = new();

// Commands
[RelayCommand]
private async Task BranchFromMessageAsync(long messageId, string? label = null)
{
    if (_activeConversationId is null) return;
    try
    {
        var branch = await _branchService.BranchAtMessageAsync(
            _activeConversationId.Value, messageId, label);
        await LoadBranchTreeAsync();
        _notificationService.Show($"Created branch: {branch.Title}");
    }
    catch (Exception ex)
    {
        _notificationService.Show($"Branch failed: {ex.Message}");
    }
}

[RelayCommand]
private async Task LoadBranchTreeAsync()
{
    if (_activeConversationId is null) return;
    try
    {
        BranchTree = await _branchService.GetBranchTreeAsync(_activeConversationId.Value);
        OnPropertyChanged(nameof(HasBranches));
        ActiveBranches.Clear();
        foreach (var child in BranchTree.Children)
            ActiveBranches.Add(child);
    }
    catch (Exception ex)
    {
        _notificationService.Show($"Failed to load branches: {ex.Message}");
    }
}

[RelayCommand]
private async Task SwitchToBranchAsync(long branchConversationId)
{
    await SelectConversationCommand.ExecuteAsync(branchConversationId);
}

[RelayCommand]
private async Task MergeToMainAsync(long sourceId, IReadOnlyList<long> messageIds, long targetId)
{
    try
    {
        await _branchService.MergeMessagesAsync(sourceId, messageIds, targetId);
        _notificationService.Show("Merged insights to main thread");
        await SelectConversationCommand.ExecuteAsync(targetId);
    }
    catch (Exception ex)
    {
        _notificationService.Show($"Merge failed: {ex.Message}");
    }
}

[RelayCommand]
private async Task DeleteBranchAsync(long branchConversationId)
{
    try
    {
        await _branchService.DeleteBranchAsync(branchConversationId);
        await LoadBranchTreeAsync();
        _notificationService.Show("Branch deleted");
    }
    catch (Exception ex)
    {
        _notificationService.Show($"Delete failed: {ex.Message}");
    }
}
```

Also add a helper test class for creating the ViewModel in tests:

```csharp
// tests/AgentX.Tests/ViewModels/ChatViewModelTestHelper.cs
using AgentX.App.ViewModels;
using AgentX.Core.Services.Chat;
using Moq;

namespace AgentX.Tests.ViewModels;

internal static class ChatViewModelTestHelper
{
    public static ChatViewModel CreateViewModel(IConversationBranchService? branchService = null)
    {
        // Create with all required mocks — adjust as needed for actual constructor
        var chatService = new Mock<ICoreChatService>();
        var conversationService = new Mock<IConversationService>();
        var aiService = new Mock<IAiService>();
        var modelManager = new Mock<IModelManager>();
        var systemPromptService = new Mock<ISystemPromptService>();
        var memoryService = new Mock<IConversationMemoryService>();
        var feedbackService = new Mock<IFeedbackService>();
        var notificationService = new Mock<INotificationService>();
        var transcriptionService = new Mock<ITranscriptionService>();

        return new ChatViewModel(
            chatService.Object,
            conversationService.Object,
            aiService.Object,
            modelManager.Object,
            systemPromptService.Object,
            memoryService.Object,
            feedbackService.Object,
            notificationService.Object,
            transcriptionService.Object,
            branchService ?? new Mock<IConversationBranchService>().Object);
    }
}
```

Note: The test helper must match the actual constructor signatures. The implementer should verify the exact interfaces and adjust mock types accordingly. `ICoreChatService` may need to be `IChatService` — check the actual `ChatViewModel` constructor parameter types.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AgentX.Tests --filter "ChatViewModelBranchingTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.App/ViewModels/ChatViewModel.cs tests/AgentX.Tests/ViewModels/ChatViewModelBranchingTests.cs tests/AgentX.Tests/ViewModels/ChatViewModelTestHelper.cs
git commit -m "feat(branching): add branch commands and properties to ChatViewModel"
```

---

### Task 3: "Branch from Here" Message Action UI

**Files:**
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `src/AgentX.App/Views/ChatPage.xaml.cs`
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`

- [ ] **Step 1: Add "Branch" button to user message actions in ChatPage.xaml**

In the `ChatPage.xaml` message template for user messages, add a "Branch from here" button alongside the existing Edit, Copy, Delete buttons. The button uses a fork icon and is only visible on user messages.

```xml
<!-- Add inside the user message action bar, after the existing Delete button -->
<Button
    ToolTipService.ToolTip="Branch from here"
    Command="{x:Bind ViewModel.BranchFromMessageCommand}"
    Visibility="{x:Bind IsUser, Mode=OneWay}">
    <PathIcon Data="M7,2 L7,10 L3,14 M7,10 L11,14 M12,2 L12,8 C12,10 10,12 7,12 C4,12 2,10 2,8 L2,2" />
    <Button.Flyout>
        <MenuFlyout>
            <TextBox
                x:Name="BranchLabelInput"
                PlaceholderText="Branch label (optional)"
                KeyDown="BranchLabelInput_KeyDown" />
            <ToggleMenuFlyoutItem Text="Branch without label" />
        </MenuFlyout>
    </Button.Flyout>
</Button>
```

Because WinUI 3 `x:Bind` in DataTemplates requires `x:DataType`, and the ChatMessageItem is an inner class, the implementer should verify the existing binding pattern and match it. If the existing action buttons use `Click` handlers in code-behind instead of `x:Bind` commands, follow that same pattern.

- [ ] **Step 2: Add branch-from-message handler in ChatPage.xaml.cs**

```csharp
// Add to ChatPage.xaml.cs
private async void BranchFromMessage_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement fe && fe.DataContext is ChatViewModel.ChatMessageItem msg)
    {
        var label = await ShowBranchLabelDialogAsync();
        await _viewModel.BranchFromMessageAsync(msg.MessageId, label);
    }
}

private async Task<string?> ShowBranchLabelDialogAsync()
{
    var input = new TextBox
    {
        PlaceholderText = "e.g. Alt approach, Different model",
        Width = 300
    };

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
    return result == ContentDialogResult.Primary ? input.Text : null;
}
```

- [ ] **Step 3: Add branch indicator on messages that have branches**

In the message template, add a small visual indicator on messages that are branch points. This requires a new property on `ChatMessageItem`:

```csharp
// Add to ChatMessageItem inner class in ChatViewModel.cs
public bool IsBranchPoint { get; set; }
public int BranchCountAtPoint { get; set; }
```

After loading a conversation's messages, query `_branchService.HasBranchesAtMessageAsync()` for each user message and set `IsBranchPoint` and `BranchCountAtPoint`. The XAML shows a small fork icon badge:

```xml
<!-- Add after the message content in the user message template -->
<Border
    Background="{ThemeResource AccentFillColorDefaultBrush}"
    CornerRadius="4"
    Padding="4,2"
    Visibility="{x:Bind IsBranchPoint, Mode=OneWay}">
    <StackPanel Orientation="Horizontal" Spacing="4">
        <FontIcon Glyph="&#xE735;" FontSize="10" />
        <TextBlock Text="{x:Bind BranchCountAtPoint, Mode=OneWay}" FontSize="10" />
    </StackPanel>
</Border>
```

- [ ] **Step 4: Verify manually**

Run: `dotnet build src/AgentX.App -r win-x64`
Expected: Build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.App/Views/ChatPage.xaml src/AgentX.App/Views/ChatPage.xaml.cs src/AgentX.App/ViewModels/ChatViewModel.cs
git commit -m "feat(branching): add Branch from Here button and branch point indicators to chat UI"
```

---

### Task 4: Branch Tree Sidebar Panel

**Files:**
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `src/AgentX.App/Views/ChatPage.xaml.cs`

- [ ] **Step 1: Add branch tree panel to ChatPage sidebar**

Add a collapsible "Branches" section below the conversation list in the sidebar. It shows a `TreeView` of branches for the active conversation.

```xml
<!-- Add inside the left sidebar StackPanel, after the conversation list section -->
<Expander
    x:Name="BranchPanel"
    Header="Branches"
    IsExpanded="False"
    Margin="0,8,0,0"
    Visibility="{x:Bind ViewModel.HasBranches, Mode=OneWay}">
    <TreeView
        x:Name="BranchTree"
        ItemsSource="{x:Bind ViewModel.ActiveBranches, Mode=OneWay}"
        ItemInvoked="BranchTree_ItemInvoked">
        <TreeView.ItemTemplate>
            <DataTemplate>
                <Grid Padding="4" ColumnSpacing="8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <FontIcon Glyph="&#xE735;" FontSize="12" />
                    <TextBlock
                        Grid.Column="1"
                        Text="{Binding BranchLabel}"
                        VerticalAlignment="Center" />
                    <Button
                        Grid.Column="2"
                        Content="&#xE712;"
                        FontSize="10"
                        Padding="2"
                        Click="DeleteBranch_Click"
                        Tag="{Binding Conversation.Id}" />
                </Grid>
            </DataTemplate>
        </TreeView.ItemTemplate>
    </TreeView>
</Expander>
```

- [ ] **Step 2: Add branch tree event handlers in ChatPage.xaml.cs**

```csharp
// Add to ChatPage.xaml.cs
private async void BranchTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
{
    if (args.InvokedItem is ConversationBranchTree node)
    {
        await _viewModel.SwitchToBranchCommand.ExecuteAsync(node.Conversation.Id);
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
            await _viewModel.DeleteBranchCommand.ExecuteAsync(branchId);
        }
    }
}
```

- [ ] **Step 3: Load branch tree when conversation is selected**

In the `SelectConversationCommand` handler (or equivalent selection logic in ChatViewModel), call `LoadBranchTreeAsync()` after the conversation is loaded. The existing `SelectConversationCommand` flow should include:

```csharp
// At the end of the existing conversation selection logic:
await LoadBranchTreeAsync();
```

- [ ] **Step 4: Verify manually**

Run: `dotnet build src/AgentX.App -r win-x64`
Expected: Build succeeds. Branches panel appears in sidebar when a branched conversation is selected.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.App/Views/ChatPage.xaml src/AgentX.App/Views/ChatPage.xaml.cs src/AgentX.App/ViewModels/ChatViewModel.cs
git commit -m "feat(branching): add branch tree sidebar panel with switch and delete actions"
```

---

### Task 5: Branch Comparison and Merge UI

**Files:**
- Create: `src/AgentX.App/Views/BranchCompareWindow.xaml.cs` (code-only, no XAML — WinUI 3 secondary Window constraint)
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`

- [ ] **Step 1: Create BranchCompareWindow as code-only Window**

Because WinUI 3 secondary Windows crash with XAML files (discovered during v1.4 QuickChatWindow implementation), this must be a code-only Window:

```csharp
// src/AgentX.App/Views/BranchCompareWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AgentX.Core.Services.Chat.Models;

namespace AgentX.App.Views;

public sealed class BranchCompareWindow : Window
{
    public BranchCompareWindow(
        ConversationBranchTree mainBranch,
        ConversationBranchTree compareBranch,
        string mainTitle,
        string compareTitle)
    {
        Title = "Branch Comparison";
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 600));

        var grid = new Grid
        {
            ColumnSpacing = 8,
            Margin = new(16)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Main branch panel
        var mainPanel = BuildBranchPanel(mainTitle, mainBranch);
        Grid.SetColumn(mainPanel, 0);
        grid.Children.Add(mainPanel);

        // Divider
        var divider = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);

        // Compare branch panel
        var comparePanel = BuildBranchPanel(compareTitle, compareBranch);
        Grid.SetColumn(comparePanel, 2);
        grid.Children.Add(comparePanel);

        this.Content = grid;
    }

    private StackPanel BuildBranchPanel(string title, ConversationBranchTree branch)
    {
        var panel = new StackPanel { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)App.Current.Resources["SubtitleTextBlockStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var label = !string.IsNullOrEmpty(branch.BranchLabel)
            ? $"Branch: {branch.BranchLabel}"
            : "Main Thread";
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            FontSize = 12
        });

        // Message count
        panel.Children.Add(new TextBlock
        {
            Text = $"{branch.Conversation.Title} — {branch.Children.Count} sub-branches"
        });

        // Scrollable message list — placeholder showing conversation structure
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var messagesList = new StackPanel { Spacing = 4 };

        foreach (var child in branch.Children)
        {
            messagesList.Children.Add(new Border
            {
                Background = (Brush)App.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new(4),
                Padding = new(8),
                Child = new TextBlock
                {
                    Text = child.BranchLabel ?? child.Conversation.Title,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        scrollViewer.Content = messagesList;
        panel.Children.Add(scrollViewer);

        return panel;
    }
}
```

- [ ] **Step 2: Add CompareBranches command to ChatViewModel**

```csharp
[RelayCommand]
private void CompareBranches()
{
    if (BranchTree?.Children.Count < 1) return;

    // Compare main thread with first branch
    var mainBranch = BranchTree;
    var compareBranch = BranchTree.Children[0];

    var window = new BranchCompareWindow(
        mainBranch, compareBranch,
        "Main Thread",
        compareBranch.BranchLabel ?? "Branch");

    window.Activate();
}
```

- [ ] **Step 3: Add merge dialog and merge button to branch tree items**

In `ChatPage.xaml`, add a "Merge to Main" button inside the branch tree item template:

```xml
<!-- Add alongside the Delete button in the branch tree item DataTemplate -->
<Button
    Content="&#xE8C8;"
    FontSize="10"
    Padding="2"
    ToolTipService.ToolTip="Merge to main thread"
    Click="MergeBranch_Click"
    Tag="{Binding Conversation.Id}" />
```

Add the handler in code-behind:

```csharp
private async void MergeBranch_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement fe && fe.Tag is long branchId)
    {
        var dialog = new ContentDialog
        {
            Title = "Merge to Main Thread",
            Content = "Select messages to merge into the main conversation thread.",
            PrimaryButtonText = "Merge All",
            SecondaryButtonText = "Merge Selected",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Merge all messages from branch to root
            var rootId = _viewModel.BranchTree?.Conversation.Id;
            if (rootId.HasValue)
            {
                await _viewModel.MergeToMainCommand.ExecuteAsync(
                    (branchId, new List<long>(), rootId.Value));
            }
        }
    }
}
```

- [ ] **Step 4: Add Compare Branches button to ChatPage top bar**

```xml
<!-- Add in ChatPage.xaml top bar, after the export button -->
<Button
    ToolTipService.ToolTip="Compare branches"
    Command="{x:Bind ViewModel.CompareBranchesCommand}"
    Visibility="{x:Bind ViewModel.HasBranches, Mode=OneWay}">
    <FontIcon Glyph="&#xE735;" />
</Button>
```

- [ ] **Step 5: Verify build and test manually**

Run: `dotnet build src/AgentX.App -r win-x64`
Expected: Build succeeds. When a conversation has branches, the Compare Branches button appears in the top bar.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Views/BranchCompareWindow.xaml.cs src/AgentX.App/ViewModels/ChatViewModel.cs src/AgentX.App/Views/ChatPage.xaml src/AgentX.App/Views/ChatPage.xaml.cs
git commit -m "feat(branching): add branch comparison window and merge UI"
```

---

### Task 6: Conversation List Branch Indicators and Export Integration

**Files:**
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `src/AgentX.Core/Services/Export/ExportService.cs`
- Modify: `src/AgentX.App/ViewModels/ExportViewModel.cs`
- Test: `tests/AgentX.Tests/Services/Export/BranchExportTests.cs`

- [ ] **Step 1: Add branch badge to conversation list items**

In the conversation list DataTemplate in `ChatPage.xaml`, add a branch count badge:

```xml
<!-- Add inside the conversation list item template, after the title -->
<Border
    Background="{ThemeResource AccentFillColorDefaultBrush}"
    CornerRadius="4"
    Padding="4,2"
    Margin="4,0,0,0"
    Visibility="{x:Bind BranchCount, Mode=OneWay, Converter={StaticResource ZeroToCollapsed}}">
    <StackPanel Orientation="Horizontal" Spacing="2">
        <FontIcon Glyph="&#xE735;" FontSize="10" />
        <TextBlock Text="{x:Bind BranchCount, Mode=OneWay}" FontSize="10" />
    </StackPanel>
</Border>
```

Note: The implementer should check if a `ZeroToCollapsed` converter already exists. If not, create one:

```csharp
// src/AgentX.App/Converters/ZeroToCollapsedConverter.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace AgentX.App.Converters;

public class ZeroToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Write test for branch-aware export**

```csharp
// tests/AgentX.Tests/Services/Export/BranchExportTests.cs
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

public class BranchExportTests
{
    [Fact]
    public void ExportOptions_IncludeBranches_DefaultsToTrue()
    {
        var options = new ExportOptions();
        options.IncludeBranches.Should().BeTrue();
    }

    [Fact]
    public void ExportFormat_Enum_HasNewFormats()
    {
        // Verify the new enum members exist
        ((int)ExportFormat.Docx).Should().BeGreaterOrEqualTo(0);
        ((int)ExportFormat.Pptx).Should().BeGreaterOrEqualTo(0);
    }
}
```

- [ ] **Step 3: Add IncludeBranches to ExportOptions**

```csharp
// Add to src/AgentX.Core/Services/Export/Models/ExportOptions.cs
public bool IncludeBranches { get; set; } = true;
```

- [ ] **Step 4: Add branch information to markdown and HTML export**

In `ExportService.BuildMarkdown()` and `BuildHtml()`, when `options.IncludeBranches` is true and the conversation has branches, append a "Branches" section listing each branch with its label and message count.

```csharp
// At the end of BuildMarkdown(), add:
if (options.IncludeBranches && conversation.ParentConversationId == null)
{
    var branches = await _conversationService.GetBranchesAsync(conversation.Id, ct);
    if (branches.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("## Branches");
        foreach (var branch in branches)
        {
            sb.AppendLine($"- **{branch.BranchLabel ?? branch.Title}** (ID: {branch.Id})");
        }
    }
}
```

Note: The implementer should check if `IConversationService` has a `GetBranchesAsync` method. If not, they should use `IConversationBranchService.GetBranchesAsync` instead, which may require injecting it into `ExportService`.

- [ ] **Step 5: Run test**

Run: `dotnet test tests/AgentX.Tests --filter "BranchExportTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Views/ChatPage.xaml src/AgentX.Core/Services/Export/Models/ExportOptions.cs src/AgentX.Core/Services/Export/ExportService.cs src/AgentX.App/ViewModels/ExportViewModel.cs src/AgentX.App/Converters/ZeroToCollapsedConverter.cs tests/AgentX.Tests/Services/Export/BranchExportTests.cs
git commit -m "feat(branching): add branch indicators to conversation list and branch-aware export"
```