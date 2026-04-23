using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
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
            _modelManager.Object);

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
            _modelManager.Object);

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
            _modelManager.Object)
        {
            RunInput = "Review this document"
        };

        await viewModel.RunWorkflowCommand.ExecuteAsync(77L);

        viewModel.RunCompleted.Should().BeTrue();
        viewModel.RunFailed.Should().BeFalse();
        viewModel.RunOutput.Should().Be("final draft");
        viewModel.RunProgress.Should().Be(2);
        viewModel.StepOutputs.Should().HaveCount(2);
        viewModel.StepOutputs[0].StepName.Should().Be("Analyze");
        viewModel.StepOutputs[1].Output.Should().Be("final draft");
        viewModel.RunTotalTokens.Should().Be(110);
        viewModel.StatusMessage.Should().Be("Workflow completed in 300ms");
    }
}
