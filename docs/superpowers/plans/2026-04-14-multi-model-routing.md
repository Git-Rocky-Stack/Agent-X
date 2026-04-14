# Multi-Model Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add intelligent model routing that automatically selects the best available AI model based on task type, cost, latency, and quality — so users experience a seamless multi-provider AI without manual switching.

**Architecture:** Insert a `ModelRouterService` between the existing `ChatService` and `IAiService`. The router inspects the prompt/task type, evaluates available providers and their cost/latency/quality profiles, and routes to the optimal model. Three default routing profiles (Cost Optimized, Quality Optimized, Balanced) with user-customizable rules. The router logs routing decisions to `CostTracker` and shows per-response indicators in the UI.

**Tech Stack:** C#, .NET 8, WinUI 3, CommunityToolkit.Mvvm, xUnit

---

### Task 1: Routing Models and Configuration

**Files:**
- Create: `src/AgentX.Core/AI/Routing/RoutingModels.cs`
- Create: `src/AgentX.Core/AI/Routing/RoutingProfile.cs`
- Test: `tests/AgentX.Tests/AI/Routing/RoutingModelsTests.cs`

- [ ] **Step 1: Write the routing model tests**

```csharp
// tests/AgentX.Tests/AI/Routing/RoutingModelsTests.cs
using AgentX.Core.AI.Routing;
using Xunit;

namespace AgentX.Tests.AI.Routing;

public class RoutingModelsTests
{
    [Fact]
    public void TaskType_Extraction_IsRecognized()
    {
        var type = TaskType.FromString("extraction");
        Assert.Equal("extraction", type.Name);
        Assert.True(type.PreferLocal);
        Assert.True(type.PreferSpeed);
    }

    [Fact]
    public void TaskType_Analysis_PrefersQuality()
    {
        var type = TaskType.FromString("analysis");
        Assert.Equal("analysis", type.Name);
        Assert.True(type.PreferQuality);
        Assert.False(type.PreferLocal);
    }

    [Fact]
    public void TaskType_Generation_PrefersCloud()
    {
        var type = TaskType.FromString("generation");
        Assert.Equal("generation", type.Name);
        Assert.False(type.PreferLocal);
        Assert.True(type.PreferQuality);
    }

    [Fact]
    public void RoutingProfile_CostOptimized_PrioritizesLocal()
    {
        var profile = RoutingProfile.CostOptimized;
        Assert.Equal("cost-optimized", profile.Id);
        Assert.True(profile.PreferLocalFirst);
    }

    [Fact]
    public void RoutingProfile_QualityOptimized_PrioritizesCloud()
    {
        var profile = RoutingProfile.QualityOptimized;
        Assert.Equal("quality-optimized", profile.Id);
        Assert.False(profile.PreferLocalFirst);
    }

    [Fact]
    public void RoutingProfile_Balanced_IsBalanced()
    {
        var profile = RoutingProfile.Balanced;
        Assert.Equal("balanced", profile.Id);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~RoutingModelsTests" -v n`
Expected: Build error — `AgentX.Core.AI.Routing` namespace does not exist.

- [ ] **Step 3: Create the routing models**

```csharp
// src/AgentX.Core/AI/Routing/RoutingModels.cs
namespace AgentX.Core.AI.Routing;

public class TaskType
{
    public string Name { get; }
    public bool PreferLocal { get; }
    public bool PreferSpeed { get; }
    public bool PreferQuality { get; }

    private TaskType(string name, bool preferLocal, bool preferSpeed, bool preferQuality)
    {
        Name = name;
        PreferLocal = preferLocal;
        PreferSpeed = preferSpeed;
        PreferQuality = preferQuality;
    }

    private static readonly Dictionary<string, TaskType> Types = new()
    {
        ["extraction"] = new TaskType("extraction", preferLocal: true, preferSpeed: true, preferQuality: false),
        ["summarization"] = new TaskType("summarization", preferLocal: true, preferSpeed: true, preferQuality: false),
        ["analysis"] = new TaskType("analysis", preferLocal: false, preferSpeed: false, preferQuality: true),
        ["generation"] = new TaskType("generation", preferLocal: false, preferSpeed: false, preferQuality: true),
        ["code"] = new TaskType("code", preferLocal: false, preferSpeed: false, preferQuality: true),
        ["creative"] = new TaskType("creative", preferLocal: false, preferSpeed: false, preferQuality: true),
        ["chat"] = new TaskType("chat", preferLocal: true, preferSpeed: true, preferQuality: false),
        ["embedding"] = new TaskType("embedding", preferLocal: true, preferSpeed: true, preferQuality: false),
    };

    public static TaskType FromString(string name)
    {
        return Types.TryGetValue(name, out var type)
            ? type
            : new TaskType(name, preferLocal: false, preferSpeed: false, preferQuality: false);
    }

    public static IEnumerable<string> AllNames => Types.Keys.OrderBy(k => k);
}

public class RoutingDecision
{
    public string ProviderId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string TaskType { get; init; } = "chat";
    public string RoutingProfile { get; init; } = "balanced";
    public string Reason { get; init; } = string.Empty;
    public DateTime DecidedAt { get; init; } = DateTime.UtcNow;
}
```

```csharp
// src/AgentX.Core/AI/Routing/RoutingProfile.cs
namespace AgentX.Core.AI.Routing;

public class RoutingProfile
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool PreferLocalFirst { get; init; }
    public Dictionary<string, string> TaskOverrides { get; init; } = []; // taskType -> providerId

    public static RoutingProfile CostOptimized => new()
    {
        Id = "cost-optimized",
        DisplayName = "Cost Optimized",
        Description = "Use local models first, fall back to cloud only when necessary",
        PreferLocalFirst = true,
        TaskOverrides = new Dictionary<string, string>
        {
            ["extraction"] = "local",
            ["summarization"] = "local",
            ["chat"] = "local",
            ["embedding"] = "local",
            ["analysis"] = "ollama",
            ["generation"] = "ollama",
            ["code"] = "openai",
            ["creative"] = "anthropic",
        }
    };

    public static RoutingProfile QualityOptimized => new()
    {
        Id = "quality-optimized",
        DisplayName = "Quality Optimized",
        Description = "Use the best available model for each task, regardless of cost",
        PreferLocalFirst = false,
        TaskOverrides = new Dictionary<string, string>
        {
            ["extraction"] = "openai",
            ["summarization"] = "anthropic",
            ["chat"] = "anthropic",
            ["embedding"] = "local",
            ["analysis"] = "anthropic",
            ["generation"] = "openai",
            ["code"] = "openai",
            ["creative"] = "anthropic",
        }
    };

    public static RoutingProfile Balanced => new()
    {
        Id = "balanced",
        DisplayName = "Balanced",
        Description = "Balance cost and quality — local for routine tasks, cloud for complex ones",
        PreferLocalFirst = true,
        TaskOverrides = new Dictionary<string, string>
        {
            ["extraction"] = "local",
            ["summarization"] = "ollama",
            ["chat"] = "ollama",
            ["embedding"] = "local",
            ["analysis"] = "openai",
            ["generation"] = "anthropic",
            ["code"] = "openai",
            ["creative"] = "anthropic",
        }
    };

    public static IEnumerable<RoutingProfile> Defaults =>
    [CostOptimized, QualityOptimized, Balanced];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~RoutingModelsTests" -v n`
Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/AI/Routing/RoutingModels.cs src/AgentX.Core/AI/Routing/RoutingProfile.cs tests/AgentX.Tests/AI/Routing/RoutingModelsTests.cs
git commit -m "feat(routing): add routing models and default profiles"
```

---

### Task 2: Task Type Detection

**Files:**
- Create: `src/AgentX.Core/AI/Routing/TaskTypeDetector.cs`
- Create: `src/AgentX.Core/AI/Routing/ITaskTypeDetector.cs`
- Test: `tests/AgentX.Tests/AI/Routing/TaskTypeDetectorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AgentX.Tests/AI/Routing/TaskTypeDetectorTests.cs
using AgentX.Core.AI.Routing;
using Xunit;

namespace AgentX.Tests.AI.Routing;

public class TaskTypeDetectorTests
{
    private readonly ITaskTypeDetector _detector = new TaskTypeDetector();

    [Theory]
    [InlineData("Extract the key points from this document", "extraction")]
    [InlineData("Summarize this article", "summarization")]
    [InlineData("Analyze the differences between these two reports", "analysis")]
    [InlineData("Write a blog post about AI trends", "generation")]
    [InlineData("Write a Python function that sorts a list", "code")]
    [InlineData("Tell me a creative story about space", "creative")]
    [InlineData("What is the capital of France?", "chat")]
    [InlineData("Hello, how are you?", "chat")]
    public void Detect_ReturnsCorrectTaskType(string prompt, string expectedType)
    {
        var result = _detector.Detect(prompt);
        Assert.Equal(expectedType, result.Name);
    }

    [Fact]
    public void Detect_WithExplicitTag_OverridesDetection()
    {
        var result = _detector.Detect("[analysis] Summarize this document");
        Assert.Equal("analysis", result.Name);
    }

    [Fact]
    public void Detect_EmptyPrompt_ReturnsChat()
    {
        var result = _detector.Detect("");
        Assert.Equal("chat", result.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~TaskTypeDetectorTests" -v n`
Expected: Build error.

- [ ] **Step 3: Create the task type detector**

```csharp
// src/AgentX.Core/AI/Routing/ITaskTypeDetector.cs
namespace AgentX.Core.AI.Routing;

public interface ITaskTypeDetector
{
    TaskType Detect(string prompt);
}

// src/AgentX.Core/AI/Routing/TaskTypeDetector.cs
using System.Text.RegularExpressions;

namespace AgentX.Core.AI.Routing;

public partial class TaskTypeDetector : ITaskTypeDetector
{
    public TaskType Detect(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return TaskType.FromString("chat");

        // Check for explicit tag: [analysis], [code], etc.
        var tagMatch = ExplicitTagRegex().Match(prompt);
        if (tagMatch.Success)
        {
            return TaskType.FromString(tagMatch.Groups[1].Value.ToLowerInvariant());
        }

        var lower = prompt.ToLowerInvariant();

        // Extraction keywords
        if (ContainsAny(lower, "extract", "pull out", "identify the key", "list the", "find all", "get the"))
            return TaskType.FromString("extraction");

        // Summarization keywords
        if (ContainsAny(lower, "summarize", "summarise", "summary", "tldr", "tldr", "brief overview", "condense"))
            return TaskType.FromString("summarization");

        // Analysis keywords
        if (ContainsAny(lower, "analyze", "analyse", "compare", "contrast", "evaluate", "assess", "critique", "examine"))
            return TaskType.FromString("analysis");

        // Code keywords
        if (ContainsAny(lower, "write a function", "write a script", "write code", "implement", "debug this code", "fix this bug", "refactor"))
            return TaskType.FromString("code");

        // Creative keywords
        if (ContainsAny(lower, "write a story", "write a poem", "creative", "imagine", "fiction", "brainstorm"))
            return TaskType.FromString("generation");

        // Generation keywords
        if (ContainsAny(lower, "write a", "draft", "compose", "create a", "generate"))
            return TaskType.FromString("generation");

        return TaskType.FromString("chat");
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    [GeneratedRegex(@"^\[(\w+)\]")]
    private static partial Regex ExplicitTagRegex();
}
```

- [ ] **Step 4: Run tests**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~TaskTypeDetectorTests" -v n`
Expected: All 10 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/AI/Routing/ITaskTypeDetector.cs src/AgentX.Core/AI/Routing/TaskTypeDetector.cs tests/AgentX.Tests/AI/Routing/TaskTypeDetectorTests.cs
git commit -m "feat(routing): add task type detection with keyword matching and explicit tags"
```

---

### Task 3: ModelRouterService

**Files:**
- Create: `src/AgentX.Core/AI/Routing/IModelRouterService.cs`
- Create: `src/AgentX.Core/AI/Routing/ModelRouterService.cs`
- Test: `tests/AgentX.Tests/AI/Routing/ModelRouterServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AgentX.Tests/AI/Routing/ModelRouterServiceTests.cs
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using Moq;
using Xunit;

namespace AgentX.Tests.AI.Routing;

public class ModelRouterServiceTests
{
    [Fact]
    public async Task RouteAsync_CostOptimized_LocalForExtraction()
    {
        var (router, _) = CreateRouter("cost-optimized");
        var decision = await router.RouteAsync("Extract the key points from this text");
        Assert.Equal("local", decision.ProviderId);
    }

    [Fact]
    public async Task RouteAsync_QualityOptimized_CloudForAnalysis()
    {
        var (router, _) = CreateRouter("quality-optimized");
        var decision = await router.RouteAsync("Analyze the differences between these reports");
        Assert.Equal("anthropic", decision.ProviderId);
    }

    [Fact]
    public async Task RouteAsync_Balanced_LocalForChat()
    {
        var (router, _) = CreateRouter("balanced");
        var decision = await router.RouteAsync("Hello, how are you?");
        Assert.Equal("ollama", decision.ProviderId);
    }

    [Fact]
    public async Task RouteAsync_FallbackWhenPreferredUnavailable()
    {
        var (router, aiService) = CreateRouter("quality-optimized", localAvailable: false);
        // When local is not available, should fall back to next provider
        var decision = await router.RouteAsync("Extract the key points");
        Assert.NotEqual("local", decision.ProviderId);
    }

    [Fact]
    public async Task RouteAsync_DecisionIncludesReason()
    {
        var (router, _) = CreateRouter("balanced");
        var decision = await router.RouteAsync("Analyze this data");
        Assert.False(string.IsNullOrEmpty(decision.Reason));
    }

    private (ModelRouterService router, Mock<IAiService> aiService) CreateRouter(
        string profileId, bool localAvailable = true)
    {
        var aiService = new Mock<IAiService>();

        var providers = new List<IAiProvider>();
        if (localAvailable)
        {
            var localProvider = new Mock<IAiProvider>();
            localProvider.SetupGet(p => p.ProviderId).Returns("local");
            localProvider.SetupGet(p => p.IsAvailable).Returns(true);
            localProvider.SetupGet(p => p.DisplayName).Returns("Local LLM");
            providers.Add(localProvider.Object);
        }

        var ollamaProvider = new Mock<IAiProvider>();
        ollamaProvider.SetupGet(p => p.ProviderId).Returns("ollama");
        ollamaProvider.SetupGet(p => p.IsAvailable).Returns(true);
        ollamaProvider.SetupGet(p => p.DisplayName).Returns("Ollama");
        providers.Add(ollamaProvider.Object);

        var openAiProvider = new Mock<IAiProvider>();
        openAiProvider.SetupGet(p => p.ProviderId).Returns("openai");
        openAiProvider.SetupGet(p => p.IsAvailable).Returns(true);
        openAiProvider.SetupGet(p => p.DisplayName).Returns("OpenAI");
        providers.Add(openAiProvider.Object);

        var anthropicProvider = new Mock<IAiProvider>();
        anthropicProvider.SetupGet(p => p.ProviderId).Returns("anthropic");
        anthropicProvider.SetupGet(p => p.IsAvailable).Returns(true);
        anthropicProvider.SetupGet(p => p.DisplayName).Returns("Anthropic");
        providers.Add(anthropicProvider.Object);

        var detector = new TaskTypeDetector();
        var router = new ModelRouterService(aiService.Object, detector);

        // Set the active profile
        var profile = RoutingProfile.Defaults.First(p => p.Id == profileId);
        router.SetActiveProfile(profile);

        return (router, aiService);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~ModelRouterServiceTests" -v n`
Expected: Build error.

- [ ] **Step 3: Create the ModelRouterService**

```csharp
// src/AgentX.Core/AI/Routing/IModelRouterService.cs
namespace AgentX.Core.AI.Routing;

public interface IModelRouterService
{
    RoutingProfile ActiveProfile { get; }
    void SetActiveProfile(RoutingProfile profile);
    void SetActiveProfile(string profileId);
    Task<RoutingDecision> RouteAsync(string prompt);
    Task<RoutingDecision> RouteAsync(string prompt, string taskTypeOverride);
    event EventHandler<RoutingDecision>? DecisionMade;
}

// src/AgentX.Core/AI/Routing/ModelRouterService.cs
using AgentX.Core.AI;
using Microsoft.Extensions.Logging;

namespace AgentX.Core.AI.Routing;

public class ModelRouterService : IModelRouterService
{
    private readonly IAiService _aiService;
    private readonly ITaskTypeDetector _taskTypeDetector;
    private readonly ILogger<ModelRouterService> _logger;

    public RoutingProfile ActiveProfile { get; private set; } = RoutingProfile.Balanced;

    public event EventHandler<RoutingDecision>? DecisionMade;

    public ModelRouterService(
        IAiService aiService,
        ITaskTypeDetector taskTypeDetector,
        ILogger<ModelRouterService>? logger = null)
    {
        _aiService = aiService;
        _taskTypeDetector = taskTypeDetector;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ModelRouterService>.Instance;
    }

    public void SetActiveProfile(RoutingProfile profile)
    {
        ActiveProfile = profile;
        _logger.LogInformation("Routing profile changed to {ProfileId}", profile.Id);
    }

    public void SetActiveProfile(string profileId)
    {
        var profile = RoutingProfile.Defaults.FirstOrDefault(p => p.Id == profileId);
        if (profile != null)
            SetActiveProfile(profile);
        else
            _logger.LogWarning("Unknown routing profile: {ProfileId}", profileId);
    }

    public Task<RoutingDecision> RouteAsync(string prompt)
    {
        var taskType = _taskTypeDetector.Detect(prompt);
        return RouteWithTaskType(prompt, taskType);
    }

    public Task<RoutingDecision> RouteAsync(string prompt, string taskTypeOverride)
    {
        var taskType = TaskType.FromString(taskTypeOverride);
        return RouteWithTaskType(prompt, taskType);
    }

    private Task<RoutingDecision> RouteWithTaskType(string prompt, TaskType taskType)
    {
        // Check profile task overrides first
        if (ActiveProfile.TaskOverrides.TryGetValue(taskType.Name, out var preferredProviderId))
        {
            var provider = GetAvailableProvider(preferredProviderId);
            if (provider != null)
            {
                var decision = new RoutingDecision
                {
                    ProviderId = provider.ProviderId,
                    ModelId = GetDefaultModelForProvider(provider),
                    TaskType = taskType.Name,
                    RoutingProfile = ActiveProfile.Id,
                    Reason = $"Profile '{ActiveProfile.DisplayName}' routes '{taskType.Name}' to {provider.DisplayName}"
                };

                DecisionMade?.Invoke(this, decision);
                return Task.FromResult(decision);
            }
        }

        // Fallback: select based on task preferences and available providers
        var fallbackProvider = SelectFallback(taskType);
        var fallbackDecision = new RoutingDecision
        {
            ProviderId = fallbackProvider.ProviderId,
            ModelId = GetDefaultModelForProvider(fallbackProvider),
            TaskType = taskType.Name,
            RoutingProfile = ActiveProfile.Id,
            Reason = $"Fallback: '{taskType.Name}' routed to {fallbackProvider.DisplayName} (preferred provider unavailable)"
        };

        DecisionMade?.Invoke(this, fallbackDecision);
        return Task.FromResult(fallbackDecision);
    }

    private IAiProvider? GetAvailableProvider(string providerId)
    {
        // Check if the AI service has this provider available
        try
        {
            // The AI service holds providers internally — expose via a method
            // For now, check by attempting to switch
            var currentProvider = _aiService.ActiveProvider;
            if (currentProvider?.ProviderId == providerId && currentProvider.IsAvailable)
                return currentProvider;

            // Try switching to check availability
            _aiService.SwitchProviderAsync(providerId).Wait();
            return _aiService.ActiveProvider;
        }
        catch
        {
            return null;
        }
    }

    private IAiProvider SelectFallback(TaskType taskType)
    {
        // Priority order based on task preferences
        var priority = ActiveProfile.PreferLocalFirst
            ? new[] { "local", "ollama", "openai", "anthropic" }
            : new[] { "anthropic", "openai", "ollama", "local" };

        foreach (var providerId in priority)
        {
            var provider = GetAvailableProvider(providerId);
            if (provider != null)
                return provider;
        }

        // Absolute fallback: return whatever is currently active
        return _aiService.ActiveProvider
            ?? throw new InvalidOperationException("No AI providers available");
    }

    private string GetDefaultModelForProvider(IAiProvider provider)
    {
        // Return the provider's default model or empty string
        try
        {
            var models = provider.ListModelsAsync().Result;
            return models.FirstOrDefault()?.Id ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~ModelRouterServiceTests" -v n`
Expected: All 5 tests PASS (may need adjustment for the mock setup — refine the `GetAvailableProvider` logic if needed).

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/AI/Routing/IModelRouterService.cs src/AgentX.Core/AI/Routing/ModelRouterService.cs tests/AgentX.Tests/AI/Routing/ModelRouterServiceTests.cs
git commit -m "feat(routing): add ModelRouterService with profile-based routing and fallback"
```

---

### Task 4: ChatService Integration

**Files:**
- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`
- Modify: `src/AgentX.App/App.xaml.cs`

- [ ] **Step 1: Add ModelRouterService as optional dependency to ChatService**

Read `src/AgentX.Core/Services/Chat/ChatService.cs`. Add `IModelRouterService` as an optional constructor parameter. When present, the router is consulted before each chat message to select the optimal provider:

```csharp
private readonly IModelRouterService? _modelRouter;

// In constructor:
public ChatService(
    IAiService aiService,
    IConversationService conversationService,
    ISettingsService settingsService,
    IContextWindowManager contextWindowManager,
    IConversationMemoryService memoryService,
    IModelRouterService? modelRouter = null,
    ILogger<ChatService>? logger = null)
{
    _modelRouter = modelRouter;
    // ... existing initialization ...
}

// In SendMessageAsync, before calling _aiService.StreamChatAsync:
if (_modelRouter != null && _settingsService.GetValueAsync<bool>("enableModelRouting").Result)
{
    var decision = await _modelRouter.RouteAsync(userMessage);
    await _aiService.SwitchProviderAsync(decision.ProviderId);
    if (!string.IsNullOrEmpty(decision.ModelId))
    {
        await _aiService.SetActiveModelAsync(decision.ModelId);
    }
}
```

- [ ] **Step 2: Register ModelRouterService in DI**

In `src/AgentX.App/App.xaml.cs`, inside `ConfigureServices`:

```csharp
services.AddSingleton<ITaskTypeDetector, TaskTypeDetector>();
services.AddSingleton<IModelRouterService, ModelRouterService>();
```

- [ ] **Step 3: Run full test suite**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests -v n`
Expected: All tests pass — routing is opt-in and doesn't break existing chat when disabled.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Services/Chat/ChatService.cs src/AgentX.App/App.xaml.cs
git commit -m "feat(routing): integrate ModelRouterService into ChatService with opt-in routing"
```

---

### Task 5: Routing UI — Settings and Per-Response Indicator

**Files:**
- Modify: `src/AgentX.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AgentX.App/Views/SettingsPage.xaml`
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs` (or equivalent chat VM)
- Modify: `src/AgentX.App/Views/ChatPage.xaml`

- [ ] **Step 1: Add routing settings to SettingsViewModel**

Read `src/AgentX.App/ViewModels/SettingsViewModel.cs`. Add:

```csharp
[ObservableProperty]
private bool _enableModelRouting;

[ObservableProperty]
private string _activeRoutingProfileId = "balanced";

[ObservableProperty]
private List<string> _availableRoutingProfiles = RoutingProfile.Defaults.Select(p => p.Id).ToList();
```

In `InitializeAsync()`, load routing settings:

```csharp
EnableModelRouting = await _settingsService.GetValueAsync<bool>("enableModelRouting");
ActiveRoutingProfileId = await _settingsService.GetValueAsync<string>("routingProfile") ?? "balanced";
```

In `SaveSettingsAsync()`, persist:

```csharp
await _settingsService.SetValueAsync("enableModelRouting", EnableModelRouting);
await _settingsService.SetValueAsync("routingProfile", ActiveRoutingProfileId);
```

- [ ] **Step 2: Add routing section to SettingsPage.xaml**

Read `src/AgentX.App/Views/SettingsPage.xaml`. Add a new section:

```xml
<!-- Model Routing Section -->
<TextBlock Text="Model Routing" Style="{StaticResource SubtitleTextBlockStyle}" Margin="0,24,0,8"/>

<ToggleSwitch IsOn="{x:Bind ViewModel.EnableModelRouting, Mode=TwoWay}"
              Header="Enable Auto-Routing"
              OffContent="Manual"
              OnContent="Automatic"
              Description="Automatically select the best AI model for each task"/>

<ComboBox Header="Routing Profile"
          IsEnabled="{x:Bind ViewModel.EnableModelRouting, Mode=OneWay}"
          SelectedItem="{x:Bind ViewModel.ActiveRoutingProfileId, Mode=TwoWay}"
          ItemsSource="{x:Bind ViewModel.AvailableRoutingProfiles, Mode=OneWay}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding}"/>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

- [ ] **Step 3: Add per-response routing indicator to chat**

In the chat message template, add a small indicator showing which model was used:

```xml
<!-- Inside the AI message template -->
<StackPanel Orientation="Horizontal" Spacing="4" Opacity="0.6" Margin="0,4,0,0">
    <TextBlock Text="{x:Bind ModelUsed, Mode=OneWay}" FontSize="10"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
    <TextBlock Text="{x:Bind RoutingReason, Mode=OneWay}" FontSize="10"
               Foreground="{ThemeResource TextFillColorTertiaryBrush}"/>
</StackPanel>
```

In the chat ViewModel, subscribe to `ModelRouterService.DecisionMade` and update the `ModelUsed` and `RoutingReason` properties for each response.

- [ ] **Step 4: Run the app and verify routing works**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet run --project src/AgentX.App`
Expected: Settings page shows Model Routing toggle. Enabling it and setting profile to "Cost Optimized" should route extraction queries to local model. Each AI response shows which model was used.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.App/ViewModels/SettingsViewModel.cs src/AgentX.App/Views/SettingsPage.xaml
git commit -m "feat(routing): add routing settings UI and per-response model indicator"
```

---

### Task 6: Full Integration Test

**Files:** No new files

- [ ] **Step 1: Run full test suite**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests -v n`
Expected: All existing + new routing tests pass.

- [ ] **Step 2: Manual integration test checklist**

- [ ] Settings → Enable Model Routing → Select "Cost Optimized" profile
- [ ] Send "Extract the key points from this text" → verify local model is used
- [ ] Send "Analyze the differences between these reports" → verify cloud model is used
- [ ] Verify per-response indicator shows model name and routing reason
- [ ] Switch to "Quality Optimized" → verify cloud models used for most tasks
- [ ] Disable Model Routing → verify manual provider switching still works
- [ ] Verify [analysis] explicit tag in prompt overrides detection

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix(routing): resolve integration issues from model routing"
```