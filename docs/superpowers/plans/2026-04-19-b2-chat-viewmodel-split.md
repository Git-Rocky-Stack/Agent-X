# B2: ChatViewModel Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `ChatViewModel.cs` (1,897 LOC) into 4 coordinator classes, reducing the ViewModel to ~300 LOC thin orchestrator.

**Architecture:** Extract distinct concerns into coordinator classes injected via DI. ChatViewModel retains UI state properties and delegates all logic to coordinators. Each coordinator is independently testable.

**Tech Stack:** C#, .NET 8, CommunityToolkit.Mvvm, WinUI 3, xUnit

---

### Task 1: ConversationCoordinator

**Files:**
- Create: `src/AgentX.App/ViewModels/Coordinators/ConversationCoordinator.cs`
- Create: `src/AgentX.App/ViewModels/Coordinators/IConversationCoordinator.cs`
- Create: `tests/AgentX.Tests/ViewModels/Coordinators/ConversationCoordinatorTests.cs`

- [ ] **Step 1: Define IConversationCoordinator interface**

```csharp
public interface IConversationCoordinator
{
    Task NewConversationAsync();
    Task DeleteConversationAsync(long conversationId);
    Task SelectConversationAsync(long conversationId);
    Task TogglePinAsync(long conversationId);
    Task SetConversationFolderAsync(long conversationId, string? folder);
    Task FilterByFolderAsync(string? folder);
    Task LoadFolderNamesAsync();
    Task<IReadOnlyList<ConversationListItem>> LoadConversationsAsync(string? searchQuery);
}
```

- [ ] **Step 2: Write failing tests**

Tests: NewConversationAsync creates conversation via IConversationService, DeleteConversationAsync removes and selects next, SelectConversationAsync loads messages, TogglePinAsync toggles IsPinned, folder CRUD works, search filters correctly.

- [ ] **Step 3: Extract conversation logic from ChatViewModel (lines 571-890, 1219-1293)**

Move: NewConversationAsync, DeleteConversationAsync, SelectConversationAsync, TogglePinAsync, SetConversationFolderAsync, FilterByFolderAsync, LoadFolderNamesAsync, conversation search.

- [ ] **Step 4: Wire coordinator into ChatViewModel via DI**

Replace inline logic with coordinator calls. ChatViewModel keeps Conversations collection and wires coordinator results to UI.

- [ ] **Step 5: Run tests**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~ConversationCoordinator" --blame-hang-timeout 60s
```

---

### Task 2: MessagingCoordinator

**Files:**
- Create: `src/AgentX.App/ViewModels/Coordinators/MessagingCoordinator.cs`
- Create: `src/AgentX.App/ViewModels/Coordinators/IMessagingCoordinator.cs`
- Create: `tests/AgentX.Tests/ViewModels/Coordinators/MessagingCoordinatorTests.cs`

- [ ] **Step 1: Define IMessagingCoordinator interface**

```csharp
public interface IMessagingCoordinator
{
    Task SendMessageAsync(string userInput, long? conversationId, string? systemPrompt);
    Task StopGenerationAsync();
    Task DeleteMessageAsync(long conversationId, long messageId);
    Task RegenerateMessageAsync(long conversationId, long messageId);
    Task SaveEditMessageAsync(long conversationId, long messageId, string newContent);
    Task SubmitFeedbackAsync(long conversationId, long messageId, int rating, string? comment);
}
```

- [ ] **Step 2: Write failing tests**

Tests: SendMessageAsync creates message + calls IAiService + streams response, StopGenerationAsync cancels CancellationTokenSource, DeleteMessageAsync removes via service, RegenerateMessageAsync replaces last assistant message, edit saves new content, feedback submits.

- [ ] **Step 3: Extract messaging logic from ChatViewModel (lines 373-570, 904-1123)**

Move: SendMessageAsync, StopGenerationAsync, StreamDirectResponseAsync, DeleteMessageAsync, SubmitFeedbackAsync, ThumbsUpAsync, ThumbsDownAsync, RegenerateMessageAsync, StartEditMessage, CancelEditMessage, SaveEditMessageAsync.

- [ ] **Step 4: Wire into ChatViewModel**

- [ ] **Step 5: Run tests**

---

### Task 3: VoiceCoordinator

**Files:**
- Create: `src/AgentX.App/ViewModels/Coordinators/VoiceCoordinator.cs`
- Create: `src/AgentX.App/ViewModels/Coordinators/IVoiceCoordinator.cs`
- Create: `tests/AgentX.Tests/ViewModels/Coordinators/VoiceCoordinatorTests.cs`

- [ ] **Step 1: Define IVoiceCoordinator interface**

```csharp
public interface IVoiceCoordinator
{
    bool IsRecording { get; }
    bool IsTranscribing { get; }
    string StatusMessage { get; }
    Task<string?> ToggleRecordingAsync();
    Task<string?> TranscribeFileAsync(string filePath);
    event EventHandler<bool>? RecordingStateChanged;
    event EventHandler<string>? StatusChanged;
}
```

- [ ] **Step 2: Write failing tests**

Tests: ToggleRecordingAsync starts/stops recording, returns transcription on stop, TranscribeFileAsync calls ITranscriptionService, status events fire correctly.

- [ ] **Step 3: Extract voice logic from ChatViewModel (lines 1298-1510)**

Move: ToggleVoiceRecordingAsync, PickAudioFileAsync, all recording state management, Whisper integration logic.

- [ ] **Step 4: Wire into ChatViewModel**

- [ ] **Step 5: Run tests**

---

### Task 4: BranchingCoordinator

**Files:**
- Create: `src/AgentX.App/ViewModels/Coordinators/BranchingCoordinator.cs`
- Create: `src/AgentX.App/ViewModels/Coordinators/IBranchingCoordinator.cs`
- Create: `tests/AgentX.Tests/ViewModels/Coordinators/BranchingCoordinatorTests.cs`

- [ ] **Step 1: Define IBranchingCoordinator interface**

```csharp
public interface IBranchingCoordinator
{
    Task<ConversationBranchTree?> LoadBranchTreeAsync(long conversationId);
    Task<long> BranchFromMessageAsync(long conversationId, long messageId, string label);
    Task SwitchToBranchAsync(long conversationId, long branchId);
    Task MergeToMainAsync(long conversationId, long branchId);
    Task DeleteBranchAsync(long conversationId, long branchId);
}
```

- [ ] **Step 2: Write failing tests**

Tests: BranchFromMessageAsync creates branch via IConversationBranchService, LoadBranchTreeAsync returns tree, SwitchToBranchAsync loads branch messages, MergeToMainAsync merges, DeleteBranchAsync removes.

- [ ] **Step 3: Extract branching logic from ChatViewModel (lines 1515-1657)**

Move: BranchFromMessageAsync, LoadBranchTreeAsync, SwitchToBranchAsync, MergeToMainAsync, DeleteBranchAsync, CompareBranches, MarkBranchPoints.

- [ ] **Step 4: Wire into ChatViewModel**

- [ ] **Step 5: Run tests**

---

### Task 5: Thin ChatViewModel + Final Wiring

**Files:**
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs` (thin to ~300 LOC)
- Modify: `src/AgentX.App/App.xaml.cs` (register coordinators in DI)
- Modify: `src/AgentX.App/Views/ChatPage.xaml` (update bindings if needed)

- [ ] **Step 1: Remove all extracted method bodies from ChatViewModel**

Replace with coordinator delegation. ChatViewModel retains:
- UI state properties (IsGenerating, CurrentStreamingResponse, etc.)
- ObservableCollection properties
- [RelayCommand] methods that delegate to coordinators
- InitializeAsync

- [ ] **Step 2: Register all coordinators in DI**

```csharp
// App.xaml.cs
services.AddTransient<IConversationCoordinator, ConversationCoordinator>();
services.AddTransient<IMessagingCoordinator, MessagingCoordinator>();
services.AddTransient<IVoiceCoordinator, VoiceCoordinator>();
services.AddTransient<IBranchingCoordinator, BranchingCoordinator>();
```

- [ ] **Step 3: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

ChatViewModel.cs ≤ 300 LOC. All existing + new coordinator tests pass. ChatPage UI works identically.

## Commit Strategy

- `refactor(chat): ConversationCoordinator extracted from ChatViewModel`
- `refactor(chat): MessagingCoordinator extracted from ChatViewModel`
- `refactor(chat): VoiceCoordinator extracted from ChatViewModel`
- `refactor(chat): BranchingCoordinator extracted from ChatViewModel`
- `refactor(chat): thin ChatViewModel orchestrator with DI coordinators`
