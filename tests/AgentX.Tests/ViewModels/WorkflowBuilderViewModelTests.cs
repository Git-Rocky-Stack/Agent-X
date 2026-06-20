using AgentX.App.ViewModels;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class WorkflowBuilderViewModelTests : IDisposable
{
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
    private readonly Mock<IModelManager> _modelManager = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IExportService> _exportService = new();
    private readonly Mock<IWorkflowLaunchService> _workflowLaunchService = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();

    // AX-QA-011: a disposable per-test app-data root. Save tests inject this so production code writes
    // workflow-result artifacts here, never into the real %LOCALAPPDATA%/AgentX user profile.
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "agentx-tests", Guid.NewGuid().ToString("N"));
    private readonly IAppPathService _appPaths;

    public WorkflowBuilderViewModelTests()
    {
        _appPaths = new TestAppPathService(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — never fail a test because temp deletion raced a file handle.
        }
    }

    /// <summary>Roots every app path under a disposable test directory.</summary>
    private sealed class TestAppPathService : IAppPathService
    {
        private readonly string _root;
        public TestAppPathService(string root) => _root = root;
        public string GetAppDataPath() => Ensure(_root);
        public string GetTempPath() => Ensure(Path.Combine(_root, "Temp"));
        private static string Ensure(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }

    [Fact]
    public async Task InitializeAsync_seeds_and_loads_workflows_and_models()
    {
        _workflowService.Setup(service => service.SeedBuiltInWorkflowsAsync())
            .Returns(Task.CompletedTask);
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 42,
                    Name = "Research Brief",
                    Description = "Creates a structured brief",
                    Category = "Research",
                    IsBuiltIn = true,
                    RunCount = 3,
                    Steps =
                    [
                        new WorkflowStepEntity { Id = 1, StepOrder = 0, Name = "Analyze" },
                        new WorkflowStepEntity { Id = 2, StepOrder = 1, Name = "Summarize" }
                    ]
                }
            ]);
        _modelManager.Setup(service => service.GetAvailableModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AiModel { Id = "llama3.1:8b", Name = "Llama 3.1 8B" }
            ]);

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        await viewModel.InitializeAsync();

        _workflowService.Verify(service => service.SeedBuiltInWorkflowsAsync(), Times.Once);
        viewModel.HasWorkflows.Should().BeTrue();
        viewModel.Workflows.Should().ContainSingle();
        viewModel.Workflows[0].StepCount.Should().Be(2);
        viewModel.AvailableModels.Should().ContainSingle(model => model.Id == "llama3.1:8b");
    }

    [Fact]
    public async Task SaveWorkflowAsync_creates_workflow_and_persists_default_steps()
    {
        _workflowService.Setup(service => service.CreateWorkflowAsync("New Workflow", string.Empty, "Custom"))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 100,
                Name = "New Workflow",
                Category = "Custom"
            });
        _workflowService.Setup(service => service.AddStepAsync(It.IsAny<long>(), It.IsAny<WorkflowStepEntity>()))
            .Returns(Task.CompletedTask);
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<WorkflowEntity>());

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        viewModel.CreateWorkflowCommand.Execute(null);
        viewModel.EditSteps.Should().ContainSingle();

        await viewModel.SaveWorkflowCommand.ExecuteAsync(null);

        _workflowService.Verify(
            service => service.CreateWorkflowAsync("New Workflow", string.Empty, "Custom"),
            Times.Once);
        _workflowService.Verify(
            service => service.AddStepAsync(
                100,
                It.Is<WorkflowStepEntity>(step =>
                    step.StepOrder == 1 &&
                    step.Name == "Step 1" &&
                    step.StepType == "AiPrompt" &&
                    step.PromptTemplate == "{{input}}")),
            Times.Once);

        viewModel.IsEditing.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("Workflow \"New Workflow\" saved");
    }

    [Fact]
    public async Task RunWorkflowAsync_maps_progress_outputs_and_completion_state()
    {
        _workflowService.Setup(service => service.GetWorkflowAsync(77))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 77,
                Name = "Document Review",
                Steps =
                [
                    new WorkflowStepEntity { Id = 1, StepOrder = 0, Name = "Analyze" },
                    new WorkflowStepEntity { Id = 2, StepOrder = 1, Name = "Draft" }
                ]
            });
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 77,
                    Name = "Document Review",
                    Category = "Analysis",
                    Steps =
                    [
                        new WorkflowStepEntity { Id = 1, StepOrder = 0, Name = "Analyze" },
                        new WorkflowStepEntity { Id = 2, StepOrder = 1, Name = "Draft" }
                    ]
                }
            ]);
        _workflowService.Setup(service => service.GetRecentRunsAsync(77, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkflowRunHistoryItem>());

        _workflowEngine.Setup(engine => engine.ExecuteWorkflowAsync(
                77,
                "Review this document",
                It.IsAny<IProgress<WorkflowStepResult>>(),
                It.IsAny<CancellationToken>()))
            .Returns<long, string, IProgress<WorkflowStepResult>?, CancellationToken>((_, _, progress, _) =>
            {
                progress?.Report(new WorkflowStepResult
                {
                    StepName = "Analyze",
                    StepOrder = 1,
                    Output = "analysis",
                    TokensUsed = 45,
                    DurationMs = 120,
                    ModelUsed = "llama3.1:8b",
                    Success = true
                });

                progress?.Report(new WorkflowStepResult
                {
                    StepName = "Draft",
                    StepOrder = 2,
                    Output = "final draft",
                    TokensUsed = 65,
                    DurationMs = 180,
                    ModelUsed = "llama3.1:8b",
                    Success = true
                });

                return Task.FromResult(new WorkflowRunResult
                {
                    WorkflowName = "Document Review",
                    FinalOutput = "final draft",
                    TotalTokensUsed = 110,
                    TotalDurationMs = 300,
                    Success = true
                });
            });

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object)
        {
            RunInput = "Review this document",
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 77,
                Name = "Document Review"
            }
        };

        // The view model relays workflow step progress through System.Progress<T>,
        // which marshals each report onto the SynchronizationContext captured when the
        // Progress<T> is constructed. In the running app that is the WinUI dispatcher -
        // a single-threaded FIFO queue - so step reports are delivered in order and
        // RunProgress lands on the final step. xUnit runs without an ambient context,
        // in which case Progress<T> falls back to unordered ThreadPool delivery; reports
        // can then execute out of order (leaving RunProgress at 1) or after the awaited
        // run completes. Install an ordered single-threaded context for the duration of
        // the run so the test faithfully reproduces the production dispatcher and is
        // deterministic.
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new OrderedSynchronizationContext());
        try
        {
            await viewModel.RunWorkflowCommand.ExecuteAsync(77L);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        await WaitForAsync(() => viewModel.StepOutputs.Count == 2 && viewModel.RunCompleted, TimeSpan.FromSeconds(1));

        viewModel.RunCompleted.Should().BeTrue();
        viewModel.RunFailed.Should().BeFalse();
        viewModel.RunOutput.Should().Be("final draft");
        viewModel.RunProgress.Should().Be(2);
        viewModel.StepOutputs.Should().HaveCount(2);
        viewModel.StepOutputs.Select(step => step.StepName).Should().BeEquivalentTo(["Analyze", "Draft"]);
        viewModel.StepOutputs.Select(step => step.Output).Should().Contain("final draft");
        viewModel.RunTotalTokens.Should().Be(110);
        viewModel.StatusMessage.Should().Be("Workflow completed in 300ms");
    }

    [Fact]
    public async Task Selecting_workflow_loads_recent_runs()
    {
        _workflowService.Setup(service => service.GetRecentRunsAsync(42, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkflowRunHistoryItem
                {
                    RunId = 7,
                    WorkflowId = 42,
                    Status = "completed",
                    StartedAt = new DateTime(2026, 4, 23, 15, 30, 0, DateTimeKind.Utc),
                    CompletedAt = new DateTime(2026, 4, 23, 15, 31, 0, DateTimeKind.Utc),
                    StepsCompleted = 2,
                    TotalSteps = 2,
                    TotalTokensUsed = 180,
                    DurationMs = 60000,
                    FinalOutput = "Completed brief",
                    StepResults =
                    [
                        new WorkflowStepResult
                        {
                            StepName = "Analyze",
                            StepOrder = 1,
                            Output = "analysis",
                            Success = true
                        }
                    ]
                }
            ]);

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 42,
                Name = "Research Brief",
                IsBuiltIn = true
            }
        };

        await Task.Delay(50);

        _workflowService.Verify(service => service.GetRecentRunsAsync(42, 8, It.IsAny<CancellationToken>()), Times.Once);
        viewModel.HasRecentRuns.Should().BeTrue();
        viewModel.RecentRuns.Should().ContainSingle();
        viewModel.RecentRuns[0].StatusText.Should().Be("Completed");
        viewModel.HasSelectedTemplateGuide.Should().BeTrue();
        viewModel.SelectedTemplateGuideSummary.Should().NotBeNullOrWhiteSpace();
        viewModel.HasSelectedTemplateGuideExamples.Should().BeTrue();
    }

    [Fact]
    public void OpenHistoricalRun_maps_stored_run_into_existing_result_surface()
    {
        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        var startedAt = new DateTime(2026, 4, 23, 16, 0, 0, DateTimeKind.Utc);
        var historicalRun = new WorkflowRunHistoryDisplayItem(
            new WorkflowRunHistoryItem
            {
                RunId = 11,
                WorkflowId = 42,
                Status = "failed",
                StartedAt = startedAt,
                CompletedAt = startedAt.AddSeconds(42),
                StepsCompleted = 1,
                TotalSteps = 2,
                TotalTokensUsed = 95,
                DurationMs = 42000,
                FinalOutput = "partial result",
                ErrorMessage = "Draft step timed out",
                StepResults =
                [
                    new WorkflowStepResult
                    {
                        StepName = "Analyze",
                        StepOrder = 1,
                        Output = "partial result",
                        TokensUsed = 95,
                        DurationMs = 42000,
                        Success = true
                    }
                ]
            });

        viewModel.OpenHistoricalRunCommand.Execute(historicalRun);

        viewModel.RunCompleted.Should().BeFalse();
        viewModel.RunFailed.Should().BeTrue();
        viewModel.RunOutput.Should().Be("partial result");
        viewModel.RunErrorMessage.Should().Be("Draft step timed out");
        viewModel.HasStepOutputs.Should().BeTrue();
        viewModel.StepOutputs.Should().ContainSingle();
        viewModel.StepOutputs[0].StepName.Should().Be("Analyze");
        viewModel.RunResultContextText.Should().Contain("Showing stored run from");
    }

    [Fact]
    public async Task UseTemplateAsync_clones_built_in_workflow_and_opens_editor_for_copy()
    {
        _workflowService.Setup(service => service.GetWorkflowAsync(42))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 42,
                Name = "Research Brief",
                Description = "Starter template",
                Category = "Research",
                IsBuiltIn = true
            });
        _workflowService.Setup(service => service.CreateWorkflowFromTemplateAsync(42, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 100,
                Name = "Research Brief Copy",
                Description = "Starter template",
                Category = "Research",
                IsBuiltIn = false
            });
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 42,
                    Name = "Research Brief",
                    Description = "Starter template",
                    Category = "Research",
                    IsBuiltIn = true
                },
                new WorkflowEntity
                {
                    Id = 100,
                    Name = "Research Brief Copy",
                    Description = "Starter template",
                    Category = "Research",
                    IsBuiltIn = false
                }
            ]);
        _workflowService.Setup(service => service.GetWorkflowAsync(100))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 100,
                Name = "Research Brief Copy",
                Description = "Starter template",
                Category = "Research",
                IsBuiltIn = false,
                Steps =
                [
                    new WorkflowStepEntity
                    {
                        Id = 1,
                        StepOrder = 1,
                        Name = "Topic Analysis",
                        StepType = "AiPrompt",
                        PromptTemplate = "{{input}}"
                    }
                ]
            });

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        await viewModel.UseTemplateCommand.ExecuteAsync(42L);

        _workflowService.Verify(service => service.CreateWorkflowFromTemplateAsync(42, null, It.IsAny<CancellationToken>()), Times.Once);
        viewModel.IsEditing.Should().BeTrue();
        viewModel.EditName.Should().Be("Research Brief Copy");
        viewModel.EditSteps.Should().ContainSingle(step => step.Name == "Topic Analysis");
        viewModel.StatusMessage.Should().Be("Created workflow \"Research Brief Copy\" from template");
    }

    [Fact]
    public async Task GetWorkflowExportJsonAsync_returns_serialized_workflow_json()
    {
        _workflowService.Setup(service => service.ExportWorkflowAsJsonAsync(42))
            .ReturnsAsync("{\"name\":\"Research Brief\"}");

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        var json = await viewModel.GetWorkflowExportJsonAsync(42);

        json.Should().Be("{\"name\":\"Research Brief\"}");
    }

    [Fact]
    public async Task ImportWorkflowAsync_selects_imported_workflow_after_reload()
    {
        const string workflowJson = "{\"name\":\"Imported Workflow\"}";

        _workflowService.Setup(service => service.ImportWorkflowFromJsonAsync(workflowJson))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 88,
                Name = "Imported Workflow",
                Description = "Imported from JSON",
                Category = "Custom",
                IsBuiltIn = false
            });
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 88,
                    Name = "Imported Workflow",
                    Description = "Imported from JSON",
                    Category = "Custom",
                    IsBuiltIn = false
                }
            ]);
        _workflowService.Setup(service => service.GetRecentRunsAsync(88, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkflowRunHistoryItem>());

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        await viewModel.ImportWorkflowCommand.ExecuteAsync(workflowJson);
        await Task.Delay(50);

        _workflowService.Verify(service => service.ImportWorkflowFromJsonAsync(workflowJson), Times.Once);
        viewModel.SelectedWorkflow.Should().NotBeNull();
        viewModel.SelectedWorkflow!.Id.Should().Be(88);
        viewModel.SelectedWorkflow.Name.Should().Be("Imported Workflow");
        viewModel.ShowWorkflowRunnerSection.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("Imported workflow \"Imported Workflow\"");
    }

    [Fact]
    public async Task Selecting_custom_workflow_hides_template_guide()
    {
        _workflowService.Setup(service => service.GetRecentRunsAsync(200, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkflowRunHistoryItem>());

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 200,
                Name = "My Custom Workflow",
                IsBuiltIn = false
            }
        };

        await Task.Delay(50);

        viewModel.HasSelectedTemplateGuide.Should().BeFalse();
        viewModel.SelectedTemplateGuideSummary.Should().BeEmpty();
        viewModel.SelectedTemplateGuideExamples.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowStarterTemplates_include_only_built_ins_and_selecting_one_updates_state()
    {
        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object);

        viewModel.Workflows.Add(new WorkflowListItem
        {
            Id = 1,
            Name = "Research Brief",
            Category = "Research",
            Description = "Starter",
            IsBuiltIn = true
        });
        viewModel.Workflows.Add(new WorkflowListItem
        {
            Id = 2,
            Name = "Custom Workflow",
            Category = "Custom",
            Description = "User owned",
            IsBuiltIn = false
        });

        viewModel.ShowWorkflowStarterEmptyState.Should().BeTrue();
        viewModel.ShowWorkflowRunnerSection.Should().BeFalse();
        viewModel.WorkflowStarterTemplates.Should().ContainSingle();
        viewModel.WorkflowStarterTemplates[0].Name.Should().Be("Research Brief");

        viewModel.SelectTemplateCommand.Execute(1L);

        viewModel.SelectedWorkflow.Should().NotBeNull();
        viewModel.SelectedWorkflow!.Id.Should().Be(1);
        viewModel.ShowWorkflowStarterEmptyState.Should().BeFalse();
        viewModel.ShowWorkflowRunnerSection.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCurrentResultToVaultAsync_imports_wrapped_workflow_result()
    {
        var savedDocument = new DocumentEntity
        {
            Id = 501,
            FileName = "Document Review Result 2026-04-23_090000.txt",
            FilePath = Path.Combine(Path.GetTempPath(), "workflow-result.txt"),
            FileType = "WorkflowResult"
        };

        string? importedPath = null;
        string? importedText = null;

        _documentService.Setup(service => service.ImportExternalContentAsync(
                It.IsAny<string>(),
                "WorkflowResult",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, long?, CancellationToken>((path, _, _, _, _, _) =>
            {
                importedPath = path;
                importedText = File.ReadAllText(path);
            })
            .ReturnsAsync(savedDocument);

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            appPathService: _appPaths)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 77,
                Name = "Document Review"
            },
            RunOutput = "final draft",
            RunResultContextText = "Showing latest execution result"
        };

        await viewModel.SaveCurrentResultToVaultCommand.ExecuteAsync(null);

        _documentService.Verify(service => service.ImportExternalContentAsync(
            It.IsAny<string>(),
            "WorkflowResult",
            It.IsAny<string>(),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        importedPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(importedPath!).Should().BeTrue();
        importedPath!.Should().StartWith(_tempRoot,
            "workflow artifacts must be written under the injected temp root, never the real user profile (AX-QA-011)");
        importedText.Should().Contain("Workflow: Document Review");
        importedText.Should().Contain("Context: Showing latest execution result");
        importedText.Should().Contain("final draft");
        viewModel.StatusMessage.Should().Contain("Saved workflow result to Knowledge Vault");
    }

    [Fact]
    public async Task SaveCurrentResultToVaultAsync_resolves_focused_stored_run_after_successful_save()
    {
        var savedDocument = new DocumentEntity
        {
            Id = 701,
            FileName = "Research Brief Result 2026-04-24_103000.txt",
            FilePath = Path.Combine(Path.GetTempPath(), "workflow-run-resolution.txt"),
            FileType = "WorkflowResult"
        };

        _documentService.Setup(service => service.ImportExternalContentAsync(
                It.IsAny<string>(),
                "WorkflowResult",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedDocument);

        var run = new WorkflowRunHistoryDisplayItem(
            new WorkflowRunHistoryItem
            {
                RunId = 77,
                WorkflowId = 42,
                Status = "completed",
                StartedAt = new DateTime(2026, 4, 24, 10, 30, 0, DateTimeKind.Utc),
                FinalOutput = "stored result"
            })
        {
            IsFocused = true
        };

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            appPathService: _appPaths)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 42,
                Name = "Research Brief"
            },
            RunOutput = "stored result",
            RunResultContextText = $"Showing stored run from {run.StartedAtText}",
            FocusedWorkflowRunSourceLabel = "Opened stored workflow run for \"Research Brief\" from Operations"
        };
        viewModel.RecentRuns.Add(run);

        await viewModel.SaveCurrentResultToVaultCommand.ExecuteAsync(null);

        viewModel.HasFocusedWorkflowRunLanding.Should().BeFalse();
        viewModel.FocusedWorkflowRunSourceLabel.Should().BeEmpty();
        viewModel.RecentRuns.Should().OnlyContain(item => !item.IsFocused);
        viewModel.StatusMessage.Should().Be(
            "Resolved the focused workflow run by saving it to Knowledge Vault as \"Research Brief Result 2026-04-24_103000.txt\".");
    }

    [Fact]
    public async Task SaveHistoricalRunToVaultAsync_imports_selected_run_content()
    {
        var savedDocument = new DocumentEntity
        {
            Id = 601,
            FileName = "Research Brief Result 2026-04-23_093000.txt",
            FilePath = Path.Combine(Path.GetTempPath(), "workflow-history-result.txt"),
            FileType = "WorkflowResult"
        };

        string? importedText = null;

        _documentService.Setup(service => service.ImportExternalContentAsync(
                It.IsAny<string>(),
                "WorkflowResult",
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, long?, CancellationToken>((path, _, _, _, _, _) =>
            {
                importedText = File.ReadAllText(path);
            })
            .ReturnsAsync(savedDocument);

        var run = new WorkflowRunHistoryDisplayItem(
            new WorkflowRunHistoryItem
            {
                RunId = 12,
                WorkflowId = 42,
                Status = "completed",
                StartedAt = new DateTime(2026, 4, 23, 16, 30, 0, DateTimeKind.Utc),
                StepsCompleted = 2,
                TotalSteps = 2,
                FinalOutput = "brief summary"
            });

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            appPathService: _appPaths)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 42,
                Name = "Research Brief"
            }
        };

        await viewModel.SaveHistoricalRunToVaultCommand.ExecuteAsync(run);

        importedText.Should().Contain("Workflow: Research Brief");
        importedText.Should().Contain("Context: Stored run from");
        importedText.Should().Contain("brief summary");
        viewModel.StatusMessage.Should().Contain("Saved stored workflow result to Knowledge Vault");
    }

    [Fact]
    public async Task ExportCurrentResultAsync_sends_workflow_artifact_to_export_service()
    {
        TextArtifactExportItem? capturedArtifact = null;

        _exportService.Setup(service => service.ExportTextArtifactAsync(
                It.IsAny<TextArtifactExportItem>(),
                It.IsAny<ExportOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<TextArtifactExportItem, ExportOptions, CancellationToken>((artifact, _, _) =>
            {
                capturedArtifact = artifact;
            })
            .ReturnsAsync(ExportResult.Ok(@"C:\exports\workflow-result.md", 128));

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            _exportService.Object)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 77,
                Name = "Document Review"
            },
            RunOutput = "final draft",
            RunResultContextText = "Showing latest execution result",
            RunTotalTokens = 110,
            RunDurationMs = 300
        };

        var result = await viewModel.ExportCurrentResultAsync(new ExportOptions
        {
            Format = ExportFormat.Markdown,
            IncludeMetadata = true
        });

        result.Success.Should().BeTrue();
        capturedArtifact.Should().NotBeNull();
        capturedArtifact!.Title.Should().Contain("Document Review Result");
        capturedArtifact.Content.Should().Be("final draft");
        capturedArtifact.Metadata.Should().ContainKey("Workflow");
        capturedArtifact.Metadata!["Workflow"].Should().Be("Document Review");
        capturedArtifact.Metadata["Context"].Should().Be("Showing latest execution result");
        viewModel.StatusMessage.Should().Be("Exported workflow result to workflow-result.md");
    }

    [Fact]
    public async Task ExportHistoricalRunAsync_sends_stored_run_artifact_to_export_service()
    {
        TextArtifactExportItem? capturedArtifact = null;

        _exportService.Setup(service => service.ExportTextArtifactAsync(
                It.IsAny<TextArtifactExportItem>(),
                It.IsAny<ExportOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<TextArtifactExportItem, ExportOptions, CancellationToken>((artifact, _, _) =>
            {
                capturedArtifact = artifact;
            })
            .ReturnsAsync(ExportResult.Ok(@"C:\exports\workflow-history.json", 256));

        var run = new WorkflowRunHistoryDisplayItem(
            new WorkflowRunHistoryItem
            {
                RunId = 12,
                WorkflowId = 42,
                Status = "completed",
                StartedAt = new DateTime(2026, 4, 23, 16, 30, 0, DateTimeKind.Utc),
                StepsCompleted = 2,
                TotalSteps = 2,
                TotalTokensUsed = 180,
                DurationMs = 42000,
                FinalOutput = "brief summary"
            });

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            _exportService.Object)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 42,
                Name = "Research Brief"
            }
        };

        var result = await viewModel.ExportHistoricalRunAsync(run, new ExportOptions
        {
            Format = ExportFormat.Json,
            IncludeMetadata = true
        });

        result.Success.Should().BeTrue();
        capturedArtifact.Should().NotBeNull();
        capturedArtifact!.Content.Should().Be("brief summary");
        capturedArtifact.Metadata.Should().ContainKey("Status");
        capturedArtifact.Metadata!["Status"].Should().Be("Completed");
        capturedArtifact.Metadata["Workflow"].Should().Be("Research Brief");
        viewModel.StatusMessage.Should().Be("Exported stored workflow result to workflow-history.json");
    }

    [Fact]
    public async Task ExportHistoricalRunAsync_resolves_focused_stored_run_after_successful_export()
    {
        _exportService.Setup(service => service.ExportTextArtifactAsync(
                It.IsAny<TextArtifactExportItem>(),
                It.IsAny<ExportOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExportResult.Ok(@"C:\exports\focused-workflow-run.json", 256));

        var run = new WorkflowRunHistoryDisplayItem(
            new WorkflowRunHistoryItem
            {
                RunId = 12,
                WorkflowId = 42,
                Status = "completed",
                StartedAt = new DateTime(2026, 4, 23, 16, 30, 0, DateTimeKind.Utc),
                StepsCompleted = 2,
                TotalSteps = 2,
                FinalOutput = "brief summary"
            })
        {
            IsFocused = true
        };

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            _exportService.Object)
        {
            SelectedWorkflow = new WorkflowListItem
            {
                Id = 42,
                Name = "Research Brief"
            },
            FocusedWorkflowRunSourceLabel = "Opened stored workflow run for \"Research Brief\" from Operations"
        };
        viewModel.RecentRuns.Add(run);

        var result = await viewModel.ExportHistoricalRunAsync(run, new ExportOptions
        {
            Format = ExportFormat.Json,
            IncludeMetadata = true
        });

        result.Success.Should().BeTrue();
        viewModel.HasFocusedWorkflowRunLanding.Should().BeFalse();
        viewModel.FocusedWorkflowRunSourceLabel.Should().BeEmpty();
        viewModel.RecentRuns.Should().OnlyContain(item => !item.IsFocused);
        viewModel.StatusMessage.Should().Be(
            "Resolved the focused workflow run by exporting it to focused-workflow-run.json.");
    }

    [Fact]
    public async Task InitializeAsync_consumes_pending_workflow_launch_request()
    {
        _workflowService.Setup(service => service.SeedBuiltInWorkflowsAsync())
            .Returns(Task.CompletedTask);
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 10,
                    Name = "Summarize & Act",
                    Category = "Writing",
                    IsBuiltIn = true
                }
            ]);
        _modelManager.Setup(service => service.GetAvailableModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());
        _workflowService.Setup(service => service.GetRecentRunsAsync(10, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkflowRunHistoryItem>());
        _workflowLaunchService.Setup(service => service.ConsumePendingRequest())
            .Returns(new WorkflowLaunchRequest
            {
                InputText = "Source: Knowledge Vault document",
                SourceLabel = "Loaded document context from \"QuarterlyPlan.pdf\"",
                RecommendedWorkflowName = "Summarize & Act"
            });

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            exportService: null,
            workflowLaunchService: _workflowLaunchService.Object);

        await viewModel.InitializeAsync();
        await WaitForAsync(() => viewModel.SelectedWorkflow?.Id == 10, TimeSpan.FromSeconds(1));

        viewModel.RunInput.Should().Be("Source: Knowledge Vault document");
        viewModel.SelectedWorkflow.Should().NotBeNull();
        viewModel.SelectedWorkflow!.Name.Should().Be("Summarize & Act");
        viewModel.StatusMessage.Should().Be("Loaded document context from \"QuarterlyPlan.pdf\"");
    }

    [Fact]
    public void OpenKnowledgeVaultCommand_navigates_to_vault()
    {
        string? navigatedPage = null;

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object)
        {
            NavigateRequested = page => navigatedPage = page
        };

        viewModel.OpenKnowledgeVaultCommand.Execute(null);

        navigatedPage.Should().Be("KnowledgeVault");
    }

    [Fact]
    public async Task InitializeAsync_consumes_pending_operations_run_request_and_reopens_run()
    {
        _workflowService.Setup(service => service.SeedBuiltInWorkflowsAsync())
            .Returns(Task.CompletedTask);
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 42,
                    Name = "Research Briefing",
                    Category = "Research"
                }
            ]);
        _modelManager.Setup(service => service.GetAvailableModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());
        _workflowService.Setup(service => service.GetRecentRunsAsync(42, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkflowRunHistoryItem
                {
                    RunId = 77,
                    WorkflowId = 42,
                    Status = "completed",
                    StartedAt = new DateTime(2026, 4, 23, 17, 0, 0, DateTimeKind.Utc),
                    CompletedAt = new DateTime(2026, 4, 23, 17, 1, 0, DateTimeKind.Utc),
                    StepsCompleted = 2,
                    TotalSteps = 2,
                    FinalOutput = "stored result",
                    StepResults =
                    [
                        new WorkflowStepResult
                        {
                            StepName = "Analyze",
                            StepOrder = 1,
                            Output = "stored result",
                            Success = true
                        }
                    ]
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingWorkflowRunRequest())
            .Returns(new OperationsWorkflowRunDrillInRequest(
                42,
                77,
                "Opened stored workflow run for \"Research Briefing\" from Operations"));

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            exportService: null,
            workflowLaunchService: null,
            operationsDrillInService: _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await WaitForAsync(() => viewModel.RecentRuns.Count == 1, TimeSpan.FromSeconds(1));

        viewModel.SelectedWorkflow.Should().NotBeNull();
        viewModel.SelectedWorkflow!.Id.Should().Be(42);
        viewModel.RecentRuns[0].RunId.Should().Be(77);
        viewModel.RecentRuns[0].IsFocused.Should().BeTrue();
        viewModel.RunOutput.Should().Be("stored result");
        viewModel.RunResultContextText.Should().Contain("Showing stored run from");
        viewModel.HasFocusedWorkflowRunLanding.Should().BeTrue();
        viewModel.FocusedWorkflowRunSourceLabel.Should().Contain("Research Briefing");
        viewModel.StatusMessage.Should().Contain("Opened stored workflow run");
    }

    [Fact]
    public async Task DismissFocusedWorkflowRunLandingCommand_clears_banner_row_focus_and_status()
    {
        _workflowService.Setup(service => service.SeedBuiltInWorkflowsAsync())
            .Returns(Task.CompletedTask);
        _workflowService.Setup(service => service.GetAllWorkflowsAsync(It.IsAny<bool>()))
            .ReturnsAsync(
            [
                new WorkflowEntity
                {
                    Id = 42,
                    Name = "Research Briefing",
                    Category = "Research"
                }
            ]);
        _modelManager.Setup(service => service.GetAvailableModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());
        _workflowService.Setup(service => service.GetRecentRunsAsync(42, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkflowRunHistoryItem
                {
                    RunId = 77,
                    WorkflowId = 42,
                    Status = "completed",
                    StartedAt = new DateTime(2026, 4, 23, 17, 0, 0, DateTimeKind.Utc),
                    CompletedAt = new DateTime(2026, 4, 23, 17, 1, 0, DateTimeKind.Utc),
                    StepsCompleted = 2,
                    TotalSteps = 2,
                    FinalOutput = "stored result",
                    StepResults =
                    [
                        new WorkflowStepResult
                        {
                            StepName = "Analyze",
                            StepOrder = 1,
                            Output = "stored result",
                            Success = true
                        }
                    ]
                }
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingWorkflowRunRequest())
            .Returns(new OperationsWorkflowRunDrillInRequest(
                42,
                77,
                "Opened stored workflow run for \"Research Briefing\" from Operations"));

        var viewModel = new WorkflowBuilderViewModel(
            _workflowService.Object,
            _workflowEngine.Object,
            _modelManager.Object,
            _documentService.Object,
            exportService: null,
            workflowLaunchService: null,
            operationsDrillInService: _operationsDrillInService.Object);

        await viewModel.InitializeAsync();
        await WaitForAsync(() => viewModel.RecentRuns.Count == 1, TimeSpan.FromSeconds(1));

        viewModel.DismissFocusedWorkflowRunLandingCommand.Execute(null);

        viewModel.HasFocusedWorkflowRunLanding.Should().BeFalse();
        viewModel.FocusedWorkflowRunSourceLabel.Should().BeEmpty();
        viewModel.StatusMessage.Should().BeEmpty();
        viewModel.RecentRuns.Should().OnlyContain(run => !run.IsFocused);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met within the expected time.");
    }

    /// <summary>
    /// A single-threaded, FIFO-ordered <see cref="SynchronizationContext"/> that mirrors the
    /// WinUI dispatcher used in the running app. Callbacks posted by <see cref="System.Progress{T}"/>
    /// are executed synchronously in the exact order they are reported, so progress-driven state
    /// (step outputs, run progress) is observed deterministically rather than racing on the
    /// thread pool. Posting synchronously is safe here because workflow step results are reported
    /// synchronously by the test's workflow-engine stub.
    /// </summary>
    private sealed class OrderedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }
}
