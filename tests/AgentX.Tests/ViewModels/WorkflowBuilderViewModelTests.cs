using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class WorkflowBuilderViewModelTests
{
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
    private readonly Mock<IModelManager> _modelManager = new();
    private readonly Mock<IDocumentService> _documentService = new();

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

        await Task.Delay(50);
        await viewModel.RunWorkflowCommand.ExecuteAsync(77L);

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
            _documentService.Object)
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
        importedText.Should().Contain("Workflow: Document Review");
        importedText.Should().Contain("Context: Showing latest execution result");
        importedText.Should().Contain("final draft");
        viewModel.StatusMessage.Should().Contain("Saved workflow result to Knowledge Vault");
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
            _documentService.Object)
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
}
