using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class DashboardViewModelTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IAiProvider> _aiProvider = new();
    private readonly Mock<IConversationService> _conversationService = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IHardwareDetector> _hardwareDetector = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<IIndexingService> _indexingService = new();
    private readonly Mock<IRagPipeline> _ragPipeline = new();
    private readonly Mock<IOperationsOverviewService> _operationsOverviewService = new();
    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();
    private readonly Mock<IPrivacyStatusService> _privacyStatusService = new();

    public DashboardViewModelTests()
    {
        _privacyStatusService.Setup(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivacyStatus.FullyLocal);

        _aiProvider.Setup(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _aiService.SetupGet(service => service.ActiveProvider).Returns(_aiProvider.Object);
        _aiService.SetupGet(service => service.ActiveModelId).Returns("llama3.1:8b");

        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(12L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(2_048L);
        _documentService.Setup(service => service.GetFileTypeDistributionAsync())
            .ReturnsAsync(new Dictionary<string, int>());
        _documentService.Setup(service => service.GetRecentDocumentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DocumentEntity>());

        _conversationService.Setup(service => service.GetConversationCountAsync()).ReturnsAsync(4);
        _conversationService.Setup(service => service.GetTotalTokensUsedAsync()).ReturnsAsync(1600L);
        _conversationService.Setup(service => service.GetRecentConversationsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConversationEntity>());

        _hardwareDetector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HardwareCapability
            {
                GpuName = "RTX Test",
                GpuVramBytes = 8_000_000_000,
                TotalRamBytes = 32_000_000_000,
                AvailableRamBytes = 24_000_000_000
            });

        _collectionService.Setup(service => service.GetCollectionCountAsync()).ReturnsAsync(3);
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(2);
        _indexingService.Setup(service => service.GetProcessedCountAsync()).ReturnsAsync(8);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _ragPipeline.Setup(pipeline => pipeline.GetIndexedChunkCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(120L);

        _temporalIdentity.Setup(service => service.GetBeliefConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BeliefConflictEntity>());
        _temporalIdentity.Setup(service => service.GetActiveTopicsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "5",
                    Status = "Durable recall current",
                    Detail = "6 stored snapshots · latest 10 minutes ago"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "2 local changes pending",
                    Detail = "Syncing the full workspace."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "4",
                    Status = "4 items awaiting triage",
                    Detail = "Open Smart Inbox to triage connector and watch-folder imports."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 connectors enabled",
                    Detail = "Email Connector · Calendar Connector"
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "7",
                    Status = "86% success rate",
                    SupportingPrimary = "2 active / 30d",
                    SupportingSecondary = "42s avg run",
                    Detail = "Top workflow: Research Briefing · 4 runs"
                }
            });
    }

    [Fact]
    public async Task InitializeAsync_maps_shared_operations_snapshot()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        _documentService.Verify(service => service.GetRecentDocumentsAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(service => service.GetRecentConversationsAsync(5, false, It.IsAny<CancellationToken>()), Times.Once);

        viewModel.ConversationIntelligenceHeadline.Should().Be("5");
        viewModel.ConversationIntelligenceStatus.Should().Be("Durable recall current");
        viewModel.ConversationIntelligenceDetail.Should().Contain("stored snapshots");

        viewModel.SyncHealthHeadline.Should().Be("Configured");
        viewModel.SyncHealthStatus.Should().Be("2 local changes pending");
        viewModel.SyncHealthDetail.Should().Be("Syncing the full workspace.");

        viewModel.InboxHeadline.Should().Be("4");
        viewModel.InboxStatus.Should().Be("4 items awaiting triage");
        viewModel.InboxDetail.Should().Contain("connector and watch-folder");

        viewModel.ConnectorsHeadline.Should().Be("2");
        viewModel.ConnectorsStatus.Should().Be("2 connectors enabled");
        viewModel.ConnectorsDetail.Should().Contain("Email Connector");

        viewModel.WorkflowHeadline.Should().Be("7");
        viewModel.WorkflowStatus.Should().Be("86% success rate");
        viewModel.WorkflowRecentActivity.Should().Be("2 active / 30d");
        viewModel.WorkflowAverageDuration.Should().Be("42s avg run");
        viewModel.WorkflowDetail.Should().Contain("Research Briefing");
        viewModel.RecommendedActions.Select(action => action.Route)
            .Should().Equal("Operations", "Inbox", "AskFiles");
    }

    [Fact]
    public async Task InitializeAsync_keeps_operations_cards_actionable_with_empty_snapshot()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Durable recall inactive",
                    Detail = "Open Analytics to inspect summary coverage."
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    Detail = "Configure a shared folder to keep multiple installations aligned."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    Detail = "Watch folders and enabled connectors will surface new items here."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    Detail = "Install or enable plugins to bring external data and workflow extensions into the app."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.ConnectorsStatus.Should().Be("No plugins installed");
        viewModel.InboxStatus.Should().Be("Queue clear");
        viewModel.WorkflowStatus.Should().Be("Ready to automate");
        viewModel.WorkflowRecentActivity.Should().Be("No recent runs");
        viewModel.WorkflowAverageDuration.Should().Be("Avg duration unavailable");
        viewModel.WorkflowDetail.Should().Contain("Vault or Search");
        viewModel.RecommendedActions.Select(action => action.Route)
            .Should().Equal("Operations", "SyncSettings", "PluginManager");
    }

    [Fact]
    public async Task InitializeAsync_prioritizes_ai_setup_when_provider_is_unavailable()
    {
        _aiProvider.Setup(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _aiService.SetupGet(service => service.ActiveModelId).Returns(string.Empty);

        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Durable recall inactive",
                    Detail = "Open Analytics to inspect summary coverage."
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Not configured",
                    Status = "Collaborative sync is off",
                    Detail = "Configure a shared folder to keep multiple installations aligned."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    Detail = "Watch folders and enabled connectors will surface new items here."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    Detail = "Install or enable plugins to bring external data and workflow extensions into the app."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
                }
            });

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(0);
        _indexingService.Setup(service => service.GetProcessedCountAsync()).ReturnsAsync(0);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.RecommendedActions.Select(action => action.Route)
            .Should().Equal("Settings", "SyncSettings", "PluginManager");
    }

    [Fact]
    public async Task InitializeAsync_defers_ai_status_when_ai_service_is_still_starting()
    {
        _aiService.SetupGet(service => service.ActiveProvider)
            .Throws(new InvalidOperationException("AI service has not been initialized. Call InitializeAsync first."));
        _aiService.SetupGet(service => service.ActiveModelId).Returns(string.Empty);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsOllamaConnected.Should().BeFalse();
        viewModel.ConnectionStatus.Should().Be("AI service starting...");
        viewModel.ActiveModelName.Should().Be("Initializing...");
        _aiProvider.Verify(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Built-in LLM", true, "Connected to Built-in LLM")]
    [InlineData("OpenAI", false, "OpenAI not available")]
    [InlineData("", true, "Connected to AI provider")]
    public async Task InitializeAsync_names_the_active_provider_in_the_connection_status(
        string displayName, bool connected, string expected)
    {
        _aiProvider.SetupGet(provider => provider.DisplayName).Returns(displayName);
        _aiProvider.Setup(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connected);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.ConnectionStatus.Should().Be(expected);
        viewModel.ConnectionStatus.Should().NotContain("Ollama");
    }

    // --- Provider attention hint ---
    // "Connect Ollama to unlock AI chat" showed under the connection card whatever the provider
    // was and whether or not it was reachable.

    [Theory]
    [InlineData("ollama", "Ollama", "Check that Ollama is running with a model downloaded, and that its address in Settings is correct.")]
    [InlineData("local", "Built-in LLM", "Check that the built-in model is installed and that there is enough free memory to load it.")]
    [InlineData("openai", "OpenAI", "Check the OpenAI API key in Settings and your network connection.")]
    public async Task InitializeAsync_when_the_active_provider_is_unreachable_says_what_to_check_for_it(
        string providerId, string displayName, string expected)
    {
        _aiProvider.SetupGet(provider => provider.ProviderId).Returns(providerId);
        _aiProvider.SetupGet(provider => provider.DisplayName).Returns(displayName);
        _aiProvider.Setup(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.HasProviderAttentionHint.Should().BeTrue();
        viewModel.ProviderAttentionHint.Should().Be(expected);
    }

    [Fact]
    public async Task InitializeAsync_when_the_active_provider_is_reachable_shows_no_hint()
    {
        _aiProvider.SetupGet(provider => provider.ProviderId).Returns("local");
        _aiProvider.SetupGet(provider => provider.DisplayName).Returns("Built-in LLM");

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.HasProviderAttentionHint.Should().BeFalse();
        viewModel.ProviderAttentionHint.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_while_the_ai_service_is_starting_shows_no_hint()
    {
        _aiService.SetupGet(service => service.ActiveProvider)
            .Throws(new InvalidOperationException("AI service has not been initialized. Call InitializeAsync first."));

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.HasProviderAttentionHint.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_names_the_provider_and_its_advice_in_the_users_language()
    {
        _aiProvider.SetupGet(provider => provider.ProviderId).Returns("ollama");
        _aiProvider.SetupGet(provider => provider.DisplayName).Returns("Ollama");
        _aiProvider.Setup(provider => provider.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var viewModel = CreateViewModel(localization: ReswLocalization.For("de"));

        await viewModel.InitializeAsync();

        viewModel.ConnectionStatus.Should().Be("Ollama nicht verfügbar");
        viewModel.ProviderAttentionHint.Should().Be(
            "Prüfen Sie, ob Ollama läuft und ein Modell heruntergeladen ist und ob die Adresse in den Einstellungen stimmt.");
    }

    // --- New Chat ---
    // The New Chat tile was a plain navigation to Chat, so the cached Chat page reopened the last
    // conversation. It now carries the intent Ctrl+N and the palette's New Conversation use.

    [Fact]
    public void StartNewChatCommand_opens_chat_with_the_new_conversation_intent()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<(string Page, object? Parameter)>();
        viewModel.NavigateRequested = (page, parameter) => navigations.Add((page, parameter));

        viewModel.StartNewChatCommand.Execute(null);

        navigations.Should().Equal(("Chat", (object?)NavigationIntents.NewConversation));
    }

    [Fact]
    public void NavigateToChatCommand_still_opens_chat_where_it_was_left()
    {
        // "View All" under Recent Conversations shows the conversations, not a new one.
        var viewModel = CreateViewModel();
        var navigations = new List<(string Page, object? Parameter)>();
        viewModel.NavigateRequested = (page, parameter) => navigations.Add((page, parameter));

        viewModel.NavigateToChatCommand.Execute(null);

        navigations.Should().Equal(("Chat", (object?)null));
    }

    // --- Belief card ---
    // The card said "Your beliefs are consistent" / "No detected contradictions" when no belief
    // had been recorded at all, so there was nothing to compare.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_with_no_recorded_beliefs_does_not_claim_they_are_consistent(bool withEnglishResources)
    {
        _temporalIdentity.Setup(service => service.GetActiveTopicsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var viewModel = CreateViewModel(localization: EnglishOrNone(withEnglishResources));

        await viewModel.InitializeAsync();

        viewModel.HasBeliefConflicts.Should().BeFalse();
        viewModel.BeliefConflictsStatus.Should().Be("No beliefs to compare yet");
        viewModel.BeliefConflictsDetail.Should().Be(
            "Agent-X has not recorded your views on any topic more than once, so there is nothing to compare yet.");
        viewModel.BeliefConflictsDetail.Should().NotContain("contradictions");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_with_recorded_beliefs_and_no_conflict_says_they_are_consistent(bool withEnglishResources)
    {
        _temporalIdentity.Setup(service => service.GetActiveTopicsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Microservices" });

        var viewModel = CreateViewModel(localization: EnglishOrNone(withEnglishResources));

        await viewModel.InitializeAsync();

        viewModel.HasBeliefConflicts.Should().BeFalse();
        viewModel.BeliefConflictsStatus.Should().Be("Your beliefs are consistent");
        viewModel.BeliefConflictsDetail.Should().Be("No detected contradictions between your past and current views.");
    }

    [Fact]
    public async Task InitializeAsync_asks_for_every_recorded_belief_not_only_recent_ones()
    {
        // A belief recorded a year ago still makes "consistent" a claim about something.
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        _temporalIdentity.Verify(
            service => service.GetActiveTopicsAsync(It.Is<int>(days => days >= 36_500), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(1, "Your view on 1 topic has evolved over time.", false)]
    [InlineData(3, "Your views on 3 topics have evolved over time.", false)]
    [InlineData(1, "Your view on 1 topic has evolved over time.", true)]
    [InlineData(3, "Your views on 3 topics have evolved over time.", true)]
    public async Task InitializeAsync_with_conflicts_lists_them(int count, string expectedDetail, bool withEnglishResources)
    {
        var conflicts = Enumerable.Range(1, count)
            .Select(i => new BeliefConflictEntity { Id = i, PreviousStance = "before", CurrentStance = "after" })
            .ToList();
        _temporalIdentity.Setup(service => service.GetBeliefConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(conflicts);

        var viewModel = CreateViewModel(localization: EnglishOrNone(withEnglishResources));

        await viewModel.InitializeAsync();

        viewModel.HasBeliefConflicts.Should().BeTrue();
        viewModel.BeliefConflictsHeadline.Should().Be(count.ToString());
        viewModel.BeliefConflictsStatus.Should().Be("Belief evolution detected");
        viewModel.BeliefConflictsDetail.Should().Be(expectedDetail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcknowledgingTheLastConflict_says_so_rather_than_claiming_consistency(bool withEnglishResources)
    {
        var conflict = new BeliefConflictEntity { Id = 7, PreviousStance = "before", CurrentStance = "after" };
        _temporalIdentity.Setup(service => service.GetBeliefConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BeliefConflictEntity> { conflict });
        _temporalIdentity.Setup(service => service.AcknowledgeConflictAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var viewModel = CreateViewModel(localization: EnglishOrNone(withEnglishResources));
        await viewModel.InitializeAsync();

        await viewModel.AcknowledgeConflictCommand.ExecuteAsync(viewModel.BeliefConflicts.Single());

        viewModel.HasBeliefConflicts.Should().BeFalse();
        viewModel.BeliefConflictsStatus.Should().Be("No open conflicts");
        viewModel.BeliefConflictsDetail.Should().Be("All belief conflicts have been acknowledged.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_when_the_belief_history_cannot_be_read_says_so(bool withEnglishResources)
    {
        _temporalIdentity.Setup(service => service.GetBeliefConflictsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var viewModel = CreateViewModel(localization: EnglishOrNone(withEnglishResources));

        await viewModel.InitializeAsync();

        viewModel.HasBeliefConflicts.Should().BeFalse();
        viewModel.BeliefConflictsStatus.Should().Be("Belief status unavailable");
        viewModel.BeliefConflictsDetail.Should().Be("Agent-X could not load your belief history.");
    }

    [Fact]
    public async Task The_belief_card_is_read_from_the_users_language()
    {
        _temporalIdentity.Setup(service => service.GetActiveTopicsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var viewModel = CreateViewModel(localization: ReswLocalization.For("fr"));

        await viewModel.InitializeAsync();

        viewModel.BeliefConflictsStatus.Should().Be("Aucune conviction à comparer pour le moment");
    }

    [Fact]
    public async Task InitializeAsync_prefers_exact_targets_when_operations_snapshot_includes_preview_ids()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "5",
                    Status = "Durable recall current",
                    Detail = "6 stored snapshots · latest 10 minutes ago"
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Configured",
                    Status = "Standing by",
                    Detail = "Syncing the full workspace."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "2",
                    Status = "2 items awaiting triage",
                    Detail = "Open Smart Inbox to triage connector and watch-folder imports."
                },
                PendingInboxItems =
                [
                    new OperationsInboxPreview
                    {
                        ItemId = 701,
                        Title = "Board recap.msg",
                        Status = "Email Connector",
                        Detail = "Message awaiting preview generation"
                    }
                ],
                RecentImportedDocuments =
                [
                    new OperationsImportedDocumentPreview
                    {
                        DocumentId = 501,
                        Title = "Quarterly Brief.docx",
                        Status = "Email Connector",
                        HealthStatus = "Needs Attention",
                        Detail = "Embedding request failed."
                    }
                ],
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "1",
                    Status = "1 connector disabled",
                    Detail = "Email Connector is installed but currently disabled."
                },
                ConnectorPreviews =
                [
                    new OperationsConnectorPreview
                    {
                        PluginId = 301,
                        IsEnabled = false,
                        CanEnableFromOperations = true,
                        Title = "Email Connector",
                        Status = "Disabled",
                        Detail = "Connector disabled"
                    }
                ],
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
                }
            });

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.RecommendedActions.Select(action => action.Route)
            .Should().Equal("KnowledgeVault", "Inbox", "PluginManager");
        viewModel.RecommendedActions.Select(action => action.TargetId)
            .Should().Equal(501, 701, 301);
    }

    [Fact]
    public void Recommended_action_command_routes_to_target_page()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        viewModel.OpenRecommendedActionCommand.Execute(new DashboardRecommendedActionItem
        {
            Title = "Review intelligence trends",
            CommandText = "Open Analytics",
            Route = "Analytics"
        });

        navigations.Should().Equal("Analytics");
    }

    [Fact]
    public void OpenRecommendedActionCommand_stages_workflow_run_request_when_ids_are_present()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        viewModel.OpenRecommendedActionCommand.Execute(new DashboardRecommendedActionItem
        {
            Title = "Review Research Briefing",
            CommandText = "Review Run",
            Route = "Workflows",
            TargetId = 41,
            SecondaryTargetId = 88
        });

        _operationsDrillInService.Verify(service => service.StageWorkflowRunRequest(
            It.Is<OperationsWorkflowRunDrillInRequest>(request =>
                request.WorkflowId == 41 &&
                request.RunId == 88 &&
                request.SourceLabel.Contains("Review Research Briefing"))), Times.Once);
        navigations.Should().Equal("Workflows");
    }

    [Fact]
    public void Operations_navigation_commands_route_to_expected_pages()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<string>();
        viewModel.NavigateRequested = (page, _) => navigations.Add(page);

        viewModel.NavigateToAnalyticsCommand.Execute(null);
        viewModel.NavigateToOperationsCommand.Execute(null);
        viewModel.NavigateToInboxCommand.Execute(null);
        viewModel.NavigateToSyncSettingsCommand.Execute(null);
        viewModel.NavigateToWorkflowsCommand.Execute(null);
        viewModel.NavigateToPluginManagerCommand.Execute(null);

        navigations.Should().Equal("Analytics", "Operations", "Inbox", "SyncSettings", "Workflows", "PluginManager");
    }

    // ── Indexing status ──────────────────────────────────────────────────────
    // When the indexing query fails the dashboard used to report 100% indexed and
    // "Idle" — a green light for a state it had not observed.

    [Fact]
    public async Task InitializeAsync_WhenTheIndexingQueryFails_ReportsUnknownRatherThanAllIndexed()
    {
        _indexingService.Setup(service => service.GetQueueLengthAsync())
            .ThrowsAsync(new InvalidOperationException("index unavailable"));

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IndexingStatus.Should().Be("Status unavailable");
        viewModel.IndexedPercent.Should().Be(0);
    }

    // ── Quick search ─────────────────────────────────────────────────────────
    // The dashboard search box navigated to Search but dropped what the user typed,
    // landing them on an empty search page. The query has to travel with the route.

    [Fact]
    public void QuickSearchCommand_CarriesTheTypedQueryToTheSearchPage()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<(string Page, object? Parameter)>();
        viewModel.NavigateRequested = (page, parameter) => navigations.Add((page, parameter));
        viewModel.QuickSearchQuery = "quarterly revenue";

        viewModel.QuickSearchCommand.Execute(null);

        navigations.Should().ContainSingle();
        navigations[0].Page.Should().Be("Search");
        navigations[0].Parameter.Should().Be("quarterly revenue");
    }

    [Fact]
    public void QuickSearchCommand_WithABlankQuery_DoesNotNavigate()
    {
        var viewModel = CreateViewModel();
        var navigations = new List<(string Page, object? Parameter)>();
        viewModel.NavigateRequested = (page, parameter) => navigations.Add((page, parameter));
        viewModel.QuickSearchQuery = "   ";

        viewModel.QuickSearchCommand.Execute(null);

        navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_maps_fully_local_status_to_private_footer()
    {
        // AX-QA-008: a genuinely local configuration keeps the strong privacy assurance.
        _privacyStatusService.Setup(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivacyStatus.FullyLocal);
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsFullyPrivate.Should().BeTrue();
        viewModel.PrivacyTitle.Should().Be("100% Private");
        viewModel.PrivacyDisclosures.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_maps_cloud_status_to_disclosures()
    {
        // AX-QA-008: when cloud surfaces are active the footer must drop the "no cloud" claim and
        // surface every disclosure the evaluator returned.
        var status = new PrivacyStatus(false, new[]
        {
            new PrivacyDisclosure("AI model", "Your prompts and conversation content are sent to OpenAI for processing."),
            new PrivacyDisclosure("Web search", "Research mode sends your search queries to Brave Search.")
        });
        _privacyStatusService.Setup(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.IsFullyPrivate.Should().BeFalse();
        viewModel.PrivacyTitle.Should().Be("Cloud services active");
        viewModel.PrivacyDisclosures.Select(item => item.Surface)
            .Should().BeEquivalentTo(new[] { "AI model", "Web search" });
        viewModel.PrivacyDisclosures.Should().Contain(item => item.Detail.Contains("OpenAI"));
    }

    [Fact]
    public async Task InitializeAsync_does_not_read_the_database_until_the_startup_gate_opens()
    {
        // AX-QA-003 follow-up (dashboard race): MainWindow shows the dashboard shell before the
        // awaited migration completes, so InitializeAsync must block on the data-ready gate before
        // fanning out its DB reads — otherwise it queries a not-yet-migrated schema.
        var gate = new StartupGate(); // closed
        var viewModel = CreateViewModel(gate);

        var init = viewModel.InitializeAsync();

        // Give any (incorrect) eager DB work a chance to run before asserting it did not.
        await Task.Delay(100);

        init.IsCompleted.Should().BeFalse("InitializeAsync must wait for the closed startup gate");
        _documentService.Verify(service => service.GetTotalDocumentCountAsync(), Times.Never,
            "no database read may occur before the migration gate opens");
        _conversationService.Verify(service => service.GetConversationCountAsync(), Times.Never,
            "no database read may occur before the migration gate opens");

        // Open the gate — initialization must now complete and the reads must run.
        gate.SignalDataReady();
        await init.WaitAsync(TimeSpan.FromSeconds(5));

        init.IsCompletedSuccessfully.Should().BeTrue();
        _documentService.Verify(service => service.GetTotalDocumentCountAsync(), Times.Once);
        _conversationService.Verify(service => service.GetConversationCountAsync(), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_skips_loading_when_startup_enters_recovery_state()
    {
        // If the migration gate fails, the gate is cancelled. InitializeAsync must skip loading
        // (no reads) and complete without surfacing the cancellation as a crash.
        var gate = new StartupGate();
        var viewModel = CreateViewModel(gate);

        var init = viewModel.InitializeAsync();
        gate.SignalStartupFailed();

        var act = async () => await init.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().NotThrowAsync("a failed startup must not crash the dashboard initializer");
        _documentService.Verify(service => service.GetTotalDocumentCountAsync(), Times.Never);
    }

    // ── Texts in the user's language ──────────────────────────────────────────
    // The system card, the recommendations, the placeholders and the operations fallback were
    // English literals, and Core formats hardware sizes but words nothing.

    [Fact]
    public async Task InitializeAsync_WordsTheSystemCardInTheUsersLanguage()
    {
        _hardwareDetector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HardwareCapability
            {
                GpuName = "Unknown GPU",
                GpuVramBytes = 0,
                TotalRamBytes = 32_000_000_000,
                AvailableRamBytes = 24_000_000_000
            });
        var viewModel = CreateViewModel(localization: ReswLocalization.For("de"));

        await viewModel.InitializeAsync();

        viewModel.GpuName.Should().Be("Keine GPU erkannt", "a placeholder GPU name reads as the Hardware Advisor shows it");
        viewModel.GpuVramInfo.Should().Be("Integrierte GPU");
        viewModel.TotalRamInfo.Should().Be(new HardwareCapability { TotalRamBytes = 32_000_000_000 }.TotalRamFormatted + " gesamt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_NamesTheVramAndTotalRam(bool withEnglishResources)
    {
        var hardware = new HardwareCapability
        {
            GpuName = "RTX Test",
            GpuVramBytes = 8_000_000_000,
            TotalRamBytes = 32_000_000_000,
            AvailableRamBytes = 24_000_000_000
        };
        var viewModel = CreateViewModel(localization: EnglishOrNone(withEnglishResources));

        await viewModel.InitializeAsync();

        viewModel.GpuName.Should().Be("RTX Test");
        viewModel.GpuVramInfo.Should().Be($"{hardware.GpuVramFormatted} VRAM");
        viewModel.TotalRamInfo.Should().Be($"{hardware.TotalRamFormatted} total");
        viewModel.AvailableRam.Should().Be(hardware.AvailableRamFormatted);
    }

    [Fact]
    public async Task InitializeAsync_SaysWhenWindowsReportedNoMemory()
    {
        _hardwareDetector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HardwareCapability { GpuName = "RTX Test", GpuVramBytes = 8_000_000_000 });
        var viewModel = CreateViewModel(localization: ReswLocalization.For("fr"));

        await viewModel.InitializeAsync();

        viewModel.AvailableRam.Should().Be("Non détectée");
        viewModel.TotalRamInfo.Should().Be("Non détectée");
    }

    [Fact]
    public async Task InitializeAsync_WhenDetectionFails_SaysSoInTheUsersLanguage()
    {
        _hardwareDetector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WMI unavailable"));
        var viewModel = CreateViewModel(localization: ReswLocalization.For("es"));

        await viewModel.InitializeAsync();

        viewModel.GpuName.Should().Be("Error de detección");
        viewModel.AvailableRam.Should().Be("Desconocido");
        viewModel.GpuVramInfo.Should().Be("Desconocido");
    }

    [Fact]
    public void Placeholders_ReadInTheUsersLanguageBeforeAnythingLoads()
    {
        var viewModel = CreateViewModel(localization: ReswLocalization.For("ja"));

        viewModel.ConnectionStatus.Should().Be("接続を確認しています...");
        viewModel.GpuName.Should().Be("検出しています...");
        viewModel.PrivacyTitle.Should().Be("100% プライベート");
        viewModel.SyncHealthHeadline.Should().Be("未構成");
    }

    [Fact]
    public async Task InitializeAsync_ShowsTheRecommendationsInTheUsersLanguage()
    {
        var viewModel = CreateViewModel(localization: ReswLocalization.For("fr"));

        await viewModel.InitializeAsync();

        var backlog = viewModel.RecommendedActions[0];
        backlog.Route.Should().Be("Operations");
        backlog.CategoryLabel.Should().Be("Attention");
        backlog.Title.Should().Be("Résorbez le retard d'indexation");
        backlog.Detail.Should().Be("2 éléments importés doivent encore être vérifiés ou réindexés.");
        backlog.CommandText.Should().Be("Ouvrir Operations");
        viewModel.RecommendedActions[1].Detail.Should()
            .Be("4 éléments de la Smart Inbox attendent une classification, une orientation ou la génération d'un aperçu.");
    }

    [Fact]
    public async Task InitializeAsync_WhenTheOperationsOverviewFails_ShowsItsFallbackInTheUsersLanguage()
    {
        _operationsOverviewService.Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("overview unavailable"));
        var viewModel = CreateViewModel(localization: ReswLocalization.For("zh-CN"));

        await viewModel.InitializeAsync();

        viewModel.SyncHealthHeadline.Should().Be("不可用");
        viewModel.SyncHealthStatus.Should().Be("同步状态不可用");
        viewModel.ConnectorsStatus.Should().Be("未安装插件");
        viewModel.WorkflowDetail.Should().Be("打开工作流以创建或运行自动化。");
    }

    [Fact]
    public async Task InitializeAsync_CountsCollectionDocumentsInTheSingularForOne()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(new[]
            {
                new CollectionEntity { Id = 1, Name = "Research", DocumentCount = 3 },
                new CollectionEntity { Id = 2, Name = "Receipts", DocumentCount = 1 }
            });
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.TopCollections.Select(item => item.CountLabel).Should().Equal("3 docs", "1 doc");
    }

    [Fact]
    public void OpenRecommendedActionCommand_NamesTheRecommendationInTheUsersLanguage()
    {
        var viewModel = CreateViewModel(localization: ReswLocalization.For("de"));

        viewModel.OpenRecommendedActionCommand.Execute(new DashboardRecommendedActionItem
        {
            Title = "Connector verbinden",
            Route = "PluginManager",
            TargetId = 7
        });

        _operationsDrillInService.Verify(service => service.StagePluginRequest(
            It.Is<OperationsPluginDrillInRequest>(request =>
                request.PluginId == 7 &&
                request.SourceLabel == "Dashboard-Empfehlung „Connector verbinden“ geöffnet")), Times.Once);
    }

    /// <summary>
    /// The shipped en-US resources, or none: the view model then uses its English fallbacks, which
    /// must read the same.
    /// </summary>
    private static ILocalizationService? EnglishOrNone(bool withEnglishResources) =>
        withEnglishResources ? ReswLocalization.For("en-US") : null;

    private DashboardViewModel CreateViewModel(IStartupGate? startupGate = null, ILocalizationService? localization = null)
    {
        // Default to an already-open gate so the many InitializeAsync tests proceed immediately;
        // tests exercising the gate itself pass an explicit (closed) gate.
        var gate = startupGate;
        if (gate is null)
        {
            var ready = new StartupGate();
            ready.SignalDataReady();
            gate = ready;
        }

        return new DashboardViewModel(
            _aiService.Object,
            _conversationService.Object,
            _documentService.Object,
            _hardwareDetector.Object,
            _collectionService.Object,
            _indexingService.Object,
            _ragPipeline.Object,
            _operationsOverviewService.Object,
            _temporalIdentity.Object,
            gate,
            _privacyStatusService.Object,
            _operationsDrillInService.Object,
            localization);
    }
}
