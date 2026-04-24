namespace AgentX.App.Services;

/// <summary>
/// Shared app-layer operations snapshot used to connect dashboard-level operational
/// surfaces to real status data from inbox, sync, workflows, plugins, and analytics.
/// </summary>
public sealed record OperationsOverviewSnapshot
{
    public OperationsCardSnapshot ConversationIntelligence { get; init; } = new();
    public OperationsCardSnapshot SyncHealth { get; init; } = new();
    public OperationsCardSnapshot IngestionBacklog { get; init; } = new();
    public OperationsCardSnapshot WorkflowActivity { get; init; } = new();
    public OperationsCardSnapshot Connectors { get; init; } = new();
}

/// <summary>
/// UI-ready summary of one operations signal.
/// </summary>
public sealed record OperationsCardSnapshot
{
    public string Headline { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SupportingPrimary { get; init; } = string.Empty;
    public string SupportingSecondary { get; init; } = string.Empty;
}
