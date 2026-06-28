using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class WorkflowServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task GetRecentRunsAsync_returns_newest_runs_with_parsed_step_results()
    {
        using var db = _dbFactory.CreateContext();

        var workflow = new WorkflowEntity
        {
            Name = "Research Brief",
            Category = "Research",
            CreatedAt = new DateTime(2026, 4, 23, 14, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 23, 14, 0, 0, DateTimeKind.Utc),
            IsBuiltIn = false,
            IsEnabled = true
        };

        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        db.WorkflowRuns.AddRange(
            new WorkflowRunEntity
            {
                WorkflowId = workflow.Id,
                Status = "completed",
                StartedAt = new DateTime(2026, 4, 23, 14, 10, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 4, 23, 14, 11, 0, DateTimeKind.Utc),
                StepsCompleted = 1,
                TotalSteps = 1,
                TotalTokensUsed = 80,
                FinalOutput = "older output",
                StepOutputsJson = SerializeSteps(
                [
                    new WorkflowStepResult
                    {
                        StepName = "Summarize",
                        StepOrder = 1,
                        Output = "older output",
                        TokensUsed = 80,
                        DurationMs = 60000,
                        Success = true
                    }
                ])
            },
            new WorkflowRunEntity
            {
                WorkflowId = workflow.Id,
                Status = "failed",
                StartedAt = new DateTime(2026, 4, 23, 14, 20, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 4, 23, 14, 20, 30, DateTimeKind.Utc),
                StepsCompleted = 1,
                TotalSteps = 2,
                TotalTokensUsed = 95,
                FinalOutput = "partial output",
                ErrorMessage = "Draft step timed out",
                StepOutputsJson = SerializeSteps(
                [
                    new WorkflowStepResult
                    {
                        StepName = "Analyze",
                        StepOrder = 1,
                        Output = "partial output",
                        TokensUsed = 95,
                        DurationMs = 30000,
                        Success = true
                    }
                ])
            });
        await db.SaveChangesAsync();

        var sut = new WorkflowService(db, Log.ForContext<WorkflowServiceTests>());

        var runs = await sut.GetRecentRunsAsync(workflow.Id, 2);

        runs.Should().HaveCount(2);
        runs[0].Status.Should().Be("failed");
        runs[0].FinalOutput.Should().Be("partial output");
        runs[0].DurationMs.Should().Be(30000);
        runs[0].StepResults.Should().ContainSingle();
        runs[0].StepResults[0].StepName.Should().Be("Analyze");
        runs[1].Status.Should().Be("completed");
    }

    [Fact]
    public async Task GetRecentRunsAsync_tolerates_invalid_step_output_json()
    {
        using var db = _dbFactory.CreateContext();

        var workflow = new WorkflowEntity
        {
            Name = "Analysis",
            Category = "Analysis",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsBuiltIn = false,
            IsEnabled = true
        };

        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        db.WorkflowRuns.Add(new WorkflowRunEntity
        {
            WorkflowId = workflow.Id,
            Status = "completed",
            StartedAt = new DateTime(2026, 4, 23, 14, 30, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 4, 23, 14, 31, 0, DateTimeKind.Utc),
            StepsCompleted = 1,
            TotalSteps = 1,
            StepOutputsJson = "{not-json}"
        });
        await db.SaveChangesAsync();

        var sut = new WorkflowService(db, Log.ForContext<WorkflowServiceTests>());

        var runs = await sut.GetRecentRunsAsync(workflow.Id);

        runs.Should().ContainSingle();
        runs[0].StepResults.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateWorkflowFromTemplateAsync_clones_steps_into_editable_copy()
    {
        using var db = _dbFactory.CreateContext();

        var template = new WorkflowEntity
        {
            Name = "Research Brief",
            Description = "Starter template",
            Icon = "\uE82D",
            Category = "Research",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsBuiltIn = true,
            IsEnabled = true,
            Steps =
            [
                new WorkflowStepEntity
                {
                    StepOrder = 0,
                    Name = "Analyze",
                    StepType = "AiPrompt",
                    PromptTemplate = "{{input}}"
                },
                new WorkflowStepEntity
                {
                    StepOrder = 1,
                    Name = "Draft",
                    StepType = "AiPrompt",
                    PromptTemplate = "{{previous_output}}",
                    TemperatureOverride = 0.7
                }
            ]
        };

        db.Workflows.Add(template);
        await db.SaveChangesAsync();

        var sut = new WorkflowService(db, Log.ForContext<WorkflowServiceTests>());

        var cloned = await sut.CreateWorkflowFromTemplateAsync(template.Id);

        cloned.Id.Should().NotBe(template.Id);
        cloned.Name.Should().Be("Research Brief Copy");
        cloned.IsBuiltIn.Should().BeFalse();
        cloned.RunCount.Should().Be(0);
        cloned.Steps.Should().HaveCount(2);
        var clonedSteps = cloned.Steps.OrderBy(step => step.StepOrder).ToArray();
        clonedSteps[1].Name.Should().Be("Draft");
        clonedSteps[1].TemperatureOverride.Should().Be(0.7);
    }

    [Fact]
    public async Task CreateWorkflowFromTemplateAsync_increments_copy_name_when_needed()
    {
        using var db = _dbFactory.CreateContext();

        db.Workflows.AddRange(
            new WorkflowEntity
            {
                Name = "Research Brief",
                Category = "Research",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsBuiltIn = true,
                IsEnabled = true
            },
            new WorkflowEntity
            {
                Name = "Research Brief Copy",
                Category = "Research",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsBuiltIn = false,
                IsEnabled = true
            });
        await db.SaveChangesAsync();

        var source = await db.Workflows.AsNoTracking().SingleAsync(workflow => workflow.IsBuiltIn);
        var sut = new WorkflowService(db, Log.ForContext<WorkflowServiceTests>());

        var cloned = await sut.CreateWorkflowFromTemplateAsync(source.Id);

        cloned.Name.Should().Be("Research Brief Copy 2");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CreateWorkflowAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkflowAsync_persists_trimmed_metadata()
    {
        using var db = _dbFactory.CreateContext();
        var sut = Sut(db);

        var created = await sut.CreateWorkflowAsync("  My Flow  ", "  does things  ", "  Research  ");

        created.Name.Should().Be("My Flow");
        created.Description.Should().Be("does things");
        created.Category.Should().Be("Research");
        created.IsBuiltIn.Should().BeFalse();
        created.IsEnabled.Should().BeTrue();
        created.RunCount.Should().Be(0);
        (await _dbFactory.CreateContext().Workflows.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateWorkflowAsync_rejects_blank_name(string name)
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).CreateWorkflowAsync(name, null, "Research");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateWorkflowAsync_rejects_blank_category(string category)
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).CreateWorkflowAsync("Name", null, category);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GetWorkflowAsync / GetAllWorkflowsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkflowAsync_returns_workflow_with_steps_ordered()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 2, Name = "Third", StepType = "AiPrompt", PromptTemplate = "x" });
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 0, Name = "First", StepType = "AiPrompt", PromptTemplate = "x" });
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 1, Name = "Second", StepType = "AiPrompt", PromptTemplate = "x" });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var loaded = await Sut(db).GetWorkflowAsync(workflow.Id);

        loaded.Should().NotBeNull();
        loaded!.Steps.OrderBy(s => s.StepOrder).Select(s => s.Name)
            .Should().ContainInOrder("First", "Second", "Third");
    }

    [Fact]
    public async Task GetWorkflowAsync_returns_null_when_missing()
    {
        using var db = _dbFactory.CreateContext();
        (await Sut(db).GetWorkflowAsync(99999)).Should().BeNull();
    }

    [Fact]
    public async Task GetAllWorkflowsAsync_orders_by_category_then_name()
    {
        using var db = _dbFactory.CreateContext();
        db.Workflows.AddRange(
            NewWorkflow("Bravo", "Analysis"),
            NewWorkflow("Alpha", "Analysis"),
            NewWorkflow("Gamma", "Research"));
        await db.SaveChangesAsync();

        var all = await Sut(db).GetAllWorkflowsAsync();

        all.Select(w => w.Name).Should().ContainInOrder("Alpha", "Bravo", "Gamma");
    }

    [Fact]
    public async Task GetAllWorkflowsAsync_can_exclude_built_in()
    {
        using var db = _dbFactory.CreateContext();
        db.Workflows.AddRange(
            NewWorkflow("Built", "Analysis", isBuiltIn: true),
            NewWorkflow("Custom", "Analysis", isBuiltIn: false));
        await db.SaveChangesAsync();

        var all = await Sut(db).GetAllWorkflowsAsync(includeBuiltIn: false);

        all.Should().ContainSingle().Which.Name.Should().Be("Custom");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GetRecentRunsAsync — argument guards, clamping, null step JSON
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetRecentRunsAsync_rejects_non_positive_workflow_id(long workflowId)
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).GetRecentRunsAsync(workflowId);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetRecentRunsAsync_clamps_count_to_bounds()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        for (var i = 0; i < 30; i++)
        {
            db.WorkflowRuns.Add(new WorkflowRunEntity
            {
                WorkflowId = workflow.Id,
                Status = "completed",
                StartedAt = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            });
        }
        await db.SaveChangesAsync();
        var sut = Sut(db);

        (await sut.GetRecentRunsAsync(workflow.Id, maxCount: 1000)).Should().HaveCount(25); // upper clamp
        (await sut.GetRecentRunsAsync(workflow.Id, maxCount: 0)).Should().HaveCount(1);      // lower clamp
    }

    [Fact]
    public async Task GetRecentRunsAsync_handles_null_step_output_json()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        db.WorkflowRuns.Add(new WorkflowRunEntity
        {
            WorkflowId = workflow.Id,
            Status = "running",
            StartedAt = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = null,
            StepOutputsJson = null,
        });
        await db.SaveChangesAsync();

        var runs = await Sut(db).GetRecentRunsAsync(workflow.Id);

        runs.Should().ContainSingle();
        runs[0].StepResults.Should().BeEmpty();
        runs[0].DurationMs.Should().BeNull(); // no CompletedAt
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CreateWorkflowFromTemplateAsync — guards and name override
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkflowFromTemplateAsync_rejects_non_positive_id()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).CreateWorkflowFromTemplateAsync(0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CreateWorkflowFromTemplateAsync_throws_when_source_missing()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).CreateWorkflowFromTemplateAsync(99999);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateWorkflowFromTemplateAsync_honours_name_override()
    {
        using var db = _dbFactory.CreateContext();
        var template = NewWorkflow("Original", "Research", isBuiltIn: true);
        db.Workflows.Add(template);
        await db.SaveChangesAsync();

        var cloned = await Sut(db).CreateWorkflowFromTemplateAsync(template.Id, "  Renamed  ");

        cloned.Name.Should().Be("Renamed");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UpdateWorkflowAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWorkflowAsync_rejects_null()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).UpdateWorkflowAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateWorkflowAsync_throws_when_missing()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).UpdateWorkflowAsync(NewWorkflow("Ghost", "Custom", id: 99999));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateWorkflowAsync_applies_changes()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Old", "Custom");
        workflow.UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        await Sut(db).UpdateWorkflowAsync(new WorkflowEntity
        {
            Id = workflow.Id,
            Name = "New Name",
            Description = "New Desc",
            Category = "Writing",
            Icon = "",
            IsEnabled = false,
        });

        var reloaded = await _dbFactory.CreateContext().Workflows.FirstAsync(w => w.Id == workflow.Id);
        reloaded.Name.Should().Be("New Name");
        reloaded.Category.Should().Be("Writing");
        reloaded.IsEnabled.Should().BeFalse();
        reloaded.UpdatedAt.Should().BeAfter(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DeleteWorkflowAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteWorkflowAsync_missing_is_no_op()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).DeleteWorkflowAsync(99999);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteWorkflowAsync_rejects_built_in()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Seeded", "Analysis", isBuiltIn: true);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var act = () => Sut(db).DeleteWorkflowAsync(workflow.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteWorkflowAsync_cascades_steps_and_runs()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Doomed", "Custom");
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 0, Name = "S", StepType = "AiPrompt", PromptTemplate = "x" });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        db.WorkflowRuns.Add(new WorkflowRunEntity
        {
            WorkflowId = workflow.Id,
            Status = "completed",
            StartedAt = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        await Sut(db).DeleteWorkflowAsync(workflow.Id);

        await using var verify = _dbFactory.CreateContext();
        (await verify.Workflows.CountAsync()).Should().Be(0);
        (await verify.WorkflowSteps.CountAsync()).Should().Be(0);
        (await verify.WorkflowRuns.CountAsync()).Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AddStepAsync / UpdateStepAsync / RemoveStepAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddStepAsync_rejects_null_step()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).AddStepAsync(1, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AddStepAsync_throws_when_workflow_missing()
    {
        using var db = _dbFactory.CreateContext();
        var step = new WorkflowStepEntity { Name = "S", StepType = "AiPrompt", PromptTemplate = "x" };
        var act = () => Sut(db).AddStepAsync(99999, step);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddStepAsync_auto_assigns_next_order()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 0, Name = "A", StepType = "AiPrompt", PromptTemplate = "x" });
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 1, Name = "B", StepType = "AiPrompt", PromptTemplate = "x" });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var added = new WorkflowStepEntity { StepOrder = 0, Name = "C", StepType = "AiPrompt", PromptTemplate = "x" };
        await Sut(db).AddStepAsync(workflow.Id, added);

        added.StepOrder.Should().Be(2);
    }

    [Fact]
    public async Task AddStepAsync_keeps_explicit_order()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 0, Name = "A", StepType = "AiPrompt", PromptTemplate = "x" });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var added = new WorkflowStepEntity { StepOrder = 5, Name = "C", StepType = "AiPrompt", PromptTemplate = "x" };
        await Sut(db).AddStepAsync(workflow.Id, added);

        added.StepOrder.Should().Be(5);
    }

    [Fact]
    public async Task UpdateStepAsync_rejects_null()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).UpdateStepAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateStepAsync_throws_when_missing()
    {
        using var db = _dbFactory.CreateContext();
        var step = new WorkflowStepEntity { Id = 99999, Name = "S", StepType = "AiPrompt", PromptTemplate = "x" };
        var act = () => Sut(db).UpdateStepAsync(step);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateStepAsync_applies_changes_and_touches_parent()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        workflow.UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var step = new WorkflowStepEntity { StepOrder = 0, Name = "Old", StepType = "AiPrompt", PromptTemplate = "old" };
        workflow.Steps.Add(step);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        await Sut(db).UpdateStepAsync(new WorkflowStepEntity
        {
            Id = step.Id,
            Name = "New",
            StepType = "TextTransform",
            PromptTemplate = "new",
            TemperatureOverride = 0.5,
            StepOrder = 3,
        });

        await using var verify = _dbFactory.CreateContext();
        var reloadedStep = await verify.WorkflowSteps.FirstAsync(s => s.Id == step.Id);
        reloadedStep.Name.Should().Be("New");
        reloadedStep.StepType.Should().Be("TextTransform");
        reloadedStep.StepOrder.Should().Be(3);
        var reloadedWorkflow = await verify.Workflows.FirstAsync(w => w.Id == workflow.Id);
        reloadedWorkflow.UpdatedAt.Should().BeAfter(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task RemoveStepAsync_missing_is_no_op()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).RemoveStepAsync(99999);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveStepAsync_removes_step()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        var step = new WorkflowStepEntity { StepOrder = 0, Name = "S", StepType = "AiPrompt", PromptTemplate = "x" };
        workflow.Steps.Add(step);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        await Sut(db).RemoveStepAsync(step.Id);

        (await _dbFactory.CreateContext().WorkflowSteps.CountAsync()).Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ReorderStepsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderStepsAsync_rejects_empty_list()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).ReorderStepsAsync(1, Array.Empty<long>());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReorderStepsAsync_no_steps_is_no_op()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var act = () => Sut(db).ReorderStepsAsync(workflow.Id, new long[] { 1, 2 });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReorderStepsAsync_rejects_foreign_step_id()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        var step = new WorkflowStepEntity { StepOrder = 0, Name = "S", StepType = "AiPrompt", PromptTemplate = "x" };
        workflow.Steps.Add(step);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var act = () => Sut(db).ReorderStepsAsync(workflow.Id, new[] { step.Id, 99999L });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReorderStepsAsync_assigns_order_by_position()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Flow", "Custom");
        var s1 = new WorkflowStepEntity { StepOrder = 0, Name = "S1", StepType = "AiPrompt", PromptTemplate = "x" };
        var s2 = new WorkflowStepEntity { StepOrder = 1, Name = "S2", StepType = "AiPrompt", PromptTemplate = "x" };
        var s3 = new WorkflowStepEntity { StepOrder = 2, Name = "S3", StepType = "AiPrompt", PromptTemplate = "x" };
        workflow.Steps.Add(s1);
        workflow.Steps.Add(s2);
        workflow.Steps.Add(s3);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        await Sut(db).ReorderStepsAsync(workflow.Id, new[] { s3.Id, s1.Id, s2.Id });

        await using var verify = _dbFactory.CreateContext();
        var steps = await verify.WorkflowSteps.Where(s => s.WorkflowId == workflow.Id).ToListAsync();
        steps.First(s => s.Id == s3.Id).StepOrder.Should().Be(0);
        steps.First(s => s.Id == s1.Id).StepOrder.Should().Be(1);
        steps.First(s => s.Id == s2.Id).StepOrder.Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Export / Import
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportWorkflowAsJsonAsync_throws_when_missing()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).ExportWorkflowAsJsonAsync(99999);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Export_then_import_round_trips_workflow_and_steps()
    {
        using var db = _dbFactory.CreateContext();
        var workflow = NewWorkflow("Roundtrip", "Research");
        workflow.Description = "desc";
        workflow.Icon = "";
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 0, Name = "Analyze", StepType = "AiPrompt", PromptTemplate = "{{input}}" });
        workflow.Steps.Add(new WorkflowStepEntity { StepOrder = 1, Name = "Draft", StepType = "AiPrompt", PromptTemplate = "{{previous_output}}", TemperatureOverride = 0.7 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        var sut = Sut(db);

        var json = await sut.ExportWorkflowAsJsonAsync(workflow.Id);
        json.Should().Contain("Analyze").And.Contain("Draft");

        var imported = await sut.ImportWorkflowFromJsonAsync(json);

        imported.Id.Should().NotBe(workflow.Id);
        imported.IsBuiltIn.Should().BeFalse();
        imported.Name.Should().Be("Roundtrip");
        imported.Category.Should().Be("Research");
        imported.Steps.Should().HaveCount(2);
        imported.Steps.OrderBy(s => s.StepOrder).Last().TemperatureOverride.Should().Be(0.7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportWorkflowFromJsonAsync_rejects_blank_json(string json)
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).ImportWorkflowFromJsonAsync(json);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ImportWorkflowFromJsonAsync_wraps_invalid_json()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).ImportWorkflowFromJsonAsync("{ not valid json");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ImportWorkflowFromJsonAsync_throws_on_null_payload()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).ImportWorkflowFromJsonAsync("null");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ImportWorkflowFromJsonAsync_requires_a_name()
    {
        using var db = _dbFactory.CreateContext();
        var act = () => Sut(db).ImportWorkflowFromJsonAsync("""{"name":"","steps":[]}""");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ImportWorkflowFromJsonAsync_applies_defaults_for_missing_fields()
    {
        using var db = _dbFactory.CreateContext();

        var imported = await Sut(db).ImportWorkflowFromJsonAsync(
            """{"name":"Imported Flow","steps":[{"stepOrder":0},{"stepOrder":1,"name":"Named Step"}]}""");

        imported.Category.Should().Be("Custom"); // default when category omitted
        imported.Steps.Should().HaveCount(2);
        var ordered = imported.Steps.OrderBy(s => s.StepOrder).ToArray();
        ordered[0].Name.Should().Be("Step 1");       // default name
        ordered[0].StepType.Should().Be("AiPrompt");  // default type
        ordered[0].PromptTemplate.Should().BeEmpty(); // default template
        ordered[1].Name.Should().Be("Named Step");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  SeedBuiltInWorkflowsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedBuiltInWorkflowsAsync_seeds_four_templates_when_empty()
    {
        using var db = _dbFactory.CreateContext();

        await Sut(db).SeedBuiltInWorkflowsAsync();

        await using var verify = _dbFactory.CreateContext();
        (await verify.Workflows.CountAsync(w => w.IsBuiltIn)).Should().Be(4);
        (await verify.WorkflowSteps.CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SeedBuiltInWorkflowsAsync_is_idempotent()
    {
        using var db = _dbFactory.CreateContext();
        db.Workflows.Add(NewWorkflow("Existing", "Analysis", isBuiltIn: true));
        await db.SaveChangesAsync();

        await Sut(db).SeedBuiltInWorkflowsAsync();

        (await _dbFactory.CreateContext().Workflows.CountAsync(w => w.IsBuiltIn)).Should().Be(1);
    }

    private WorkflowService Sut(AgentXDbContext db) => new(db, Log.ForContext<WorkflowServiceTests>());

    private static WorkflowEntity NewWorkflow(string name, string category, bool isBuiltIn = false, long id = 0) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        IsBuiltIn = isBuiltIn,
        IsEnabled = true,
        CreatedAt = new DateTime(2026, 4, 23, 14, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 4, 23, 14, 0, 0, DateTimeKind.Utc),
    };

    private static string SerializeSteps(IReadOnlyList<WorkflowStepResult> steps)
    {
        return JsonSerializer.Serialize(steps, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
