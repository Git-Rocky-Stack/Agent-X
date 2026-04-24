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
    public IReadOnlyList<OperationsConversationPreview> RecentConversationSummaries { get; init; } = Array.Empty<OperationsConversationPreview>();
    public IReadOnlyList<OperationsSyncPreview> RecentSyncPasses { get; init; } = Array.Empty<OperationsSyncPreview>();
    public IReadOnlyList<OperationsInboxPreview> PendingInboxItems { get; init; } = Array.Empty<OperationsInboxPreview>();
    public IReadOnlyList<OperationsImportedDocumentPreview> RecentImportedDocuments { get; init; } = Array.Empty<OperationsImportedDocumentPreview>();
    public IReadOnlyList<OperationsWorkflowRunPreview> RecentWorkflowRuns { get; init; } = Array.Empty<OperationsWorkflowRunPreview>();
    public IReadOnlyList<OperationsConnectorPreview> ConnectorPreviews { get; init; } = Array.Empty<OperationsConnectorPreview>();
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

public sealed record OperationsConversationPreview
{
    public long ConversationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record OperationsSyncPreview
{
    public long SyncLogId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record OperationsInboxPreview
{
    public long ItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record OperationsImportedDocumentPreview
{
    public long DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record OperationsWorkflowRunPreview
{
    public long WorkflowId { get; init; }
    public long RunId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record OperationsConnectorPreview
{
    public long PluginId { get; init; }
    public bool IsEnabled { get; init; }
    public bool CanEnableFromOperations { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
