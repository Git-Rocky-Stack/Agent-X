using System.Text.Json;
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

    private static string SerializeSteps(IReadOnlyList<WorkflowStepResult> steps)
    {
        return JsonSerializer.Serialize(steps, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
