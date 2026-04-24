using AgentX.App.Services;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class OperationsDrillInServiceTests
{
    [Fact]
    public void Stage_and_consume_requests_are_one_shot_per_surface()
    {
        var service = new OperationsDrillInService();

        service.StageInboxRequest(new OperationsInboxDrillInRequest(7, "Inbox"));
        service.StageWorkflowRunRequest(new OperationsWorkflowRunDrillInRequest(42, 77, "Workflow"));
        service.StageSyncRequest(new OperationsSyncDrillInRequest(9, "Sync"));
        service.StagePluginRequest(new OperationsPluginDrillInRequest(15, "Plugins"));

        service.ConsumePendingInboxRequest()!.ItemId.Should().Be(7);
        service.ConsumePendingWorkflowRunRequest()!.RunId.Should().Be(77);
        service.ConsumePendingSyncRequest()!.SyncLogId.Should().Be(9);
        service.ConsumePendingPluginRequest()!.PluginId.Should().Be(15);

        service.ConsumePendingInboxRequest().Should().BeNull();
        service.ConsumePendingWorkflowRunRequest().Should().BeNull();
        service.ConsumePendingSyncRequest().Should().BeNull();
        service.ConsumePendingPluginRequest().Should().BeNull();
    }
}
