using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Analytics;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class AnalyticsServiceWorkflowIntelligenceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsServiceWorkflowIntelligenceTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task GetWorkflowIntelligenceOverviewAsync_returns_join_backed_summary_top_workflows_and_recent_runs()
    {
        using var db = _dbFactory.CreateContext();
        var today = DateTime.UtcNow.Date;

        var researchWorkflow = new WorkflowEntity
        {
            Name = "Research Brief",
            Category = "Research",
            CreatedAt = today.AddDays(-3).AddHours(9),
            UpdatedAt = today.AddHours(14),
            IsBuiltIn = false,
            IsEnabled = true
        };

        var digestWorkflow = new WorkflowEntity
        {
            Name = "Weekly Digest",
            Category = "Operations",
            CreatedAt = today.AddDays(-3).AddHours(10),
            UpdatedAt = today.AddHours(14),
            IsBuiltIn = false,
            IsEnabled = true
        };

        db.Workflows.AddRange(researchWorkflow, digestWorkflow);
        await db.SaveChangesAsync();

        db.WorkflowRuns.AddRange(
            new WorkflowRunEntity
            {
                WorkflowId = researchWorkflow.Id,
                Status = "completed",
                StartedAt = today.AddHours(12),
                CompletedAt = today.AddHours(12).AddMinutes(1),
                FinalOutput = "Research brief completed successfully."
            },
            new WorkflowRunEntity
            {
                WorkflowId = researchWorkflow.Id,
                Status = "failed",
                StartedAt = today.AddHours(13),
                CompletedAt = today.AddHours(13).AddMinutes(1),
                ErrorMessage = "Summarizer timed out on the draft step."
            },
            new WorkflowRunEntity
            {
                WorkflowId = researchWorkflow.Id,
                Status = "cancelled",
                StartedAt = today.AddHours(14),
                CompletedAt = today.AddHours(14).AddSeconds(30),
                ErrorMessage = "User cancelled run before final review."
            },
            new WorkflowRunEntity
            {
                WorkflowId = digestWorkflow.Id,
                Status = "completed",
                StartedAt = today.AddHours(11),
                CompletedAt = today.AddHours(11).AddMinutes(2),
                FinalOutput = "Weekly digest generated."
            },
            new WorkflowRunEntity
            {
                WorkflowId = digestWorkflow.Id,
                Status = "completed",
                StartedAt = today.AddDays(-1).AddHours(10),
                CompletedAt = today.AddDays(-1).AddHours(10).AddMinutes(1),
                FinalOutput = "Prior digest generated."
            },
            new WorkflowRunEntity
            {
                WorkflowId = digestWorkflow.Id,
                Status = "completed",
                StartedAt = today.AddDays(-2).AddHours(9),
                CompletedAt = today.AddDays(-2).AddHours(9).AddMinutes(1),
                FinalOutput = "Earlier digest generated."
            });
        await db.SaveChangesAsync();

        var sut = new AnalyticsService(db, _logger);

        var overview = await sut.GetWorkflowIntelligenceOverviewAsync(maxRecentRuns: 4, maxTopWorkflows: 5, recentActivityDays: 30);

        overview.TotalRuns.Should().Be(6);
        overview.SuccessfulRuns.Should().Be(4);
        overview.FailedOrCancelledRuns.Should().Be(2);
        overview.SuccessRate.Should().Be(66.7);
        overview.AverageRunDurationMs.Should().Be(65000);
        overview.ActiveWorkflowsRecently.Should().Be(2);

        overview.TopWorkflows.Should().HaveCount(2);
        overview.TopWorkflows[0].WorkflowName.Should().Be("Weekly Digest");
        overview.TopWorkflows[0].RunCount.Should().Be(3);
        overview.TopWorkflows[0].SuccessRate.Should().Be(100.0);
        overview.TopWorkflows[1].WorkflowName.Should().Be("Research Brief");
        overview.TopWorkflows[1].RunCount.Should().Be(3);
        overview.TopWorkflows[1].SuccessRate.Should().Be(33.3);

        overview.RecentRuns.Should().HaveCount(4);
        overview.RecentRuns[0].Status.Should().Be("cancelled");
        overview.RecentRuns[0].PreviewText.Should().Contain("User cancelled run");
        overview.RecentRuns[1].Status.Should().Be("failed");
        overview.RecentRuns[1].HasErrorPreview.Should().BeTrue();
    }

    [Fact]
    public async Task GetDailyWorkflowRunMetricsAsync_fills_missing_days_with_zero_counts()
    {
        using var db = _dbFactory.CreateContext();
        var today = DateTime.UtcNow.Date;

        var workflow = new WorkflowEntity
        {
            Name = "Research Brief",
            Category = "Research",
            CreatedAt = today.AddDays(-10),
            UpdatedAt = today,
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
                StartedAt = today.AddDays(-2).AddHours(9),
                CompletedAt = today.AddDays(-2).AddHours(9).AddMinutes(1)
            },
            new WorkflowRunEntity
            {
                WorkflowId = workflow.Id,
                Status = "completed",
                StartedAt = today.AddHours(10),
                CompletedAt = today.AddHours(10).AddMinutes(1)
            },
            new WorkflowRunEntity
            {
                WorkflowId = workflow.Id,
                Status = "failed",
                StartedAt = today.AddHours(13),
                CompletedAt = today.AddHours(13).AddMinutes(1),
                ErrorMessage = "Prompt step failed."
            });
        await db.SaveChangesAsync();

        var sut = new AnalyticsService(db, _logger);

        var metrics = await sut.GetDailyWorkflowRunMetricsAsync(days: 3);

        metrics.Should().HaveCount(3);
        metrics[0].Date.Should().Be(today.AddDays(-2));
        metrics[0].Count.Should().Be(1);
        metrics[1].Date.Should().Be(today.AddDays(-1));
        metrics[1].Count.Should().Be(0);
        metrics[2].Date.Should().Be(today);
        metrics[2].Count.Should().Be(2);
    }
}
