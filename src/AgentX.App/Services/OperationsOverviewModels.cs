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
/// Which status an operations card or preview reports. The status text is written in the
/// user's language, so logic and status colors read this instead of the text.
/// </summary>
public enum OperationsStatusKind
{
    /// <summary>No status, or one taken from the data itself (a source name, an unrecognized run status).</summary>
    Other,

    // Conversation intelligence card
    RecallRefreshesPending,
    RecallStaleSummaries,
    RecallCurrent,
    RecallInactive,

    // Conversation summary previews
    SummaryRefreshError,
    SummaryStale,
    SummaryPending,
    SummaryCurrent,

    // Sync health card
    SyncNotConfigured,
    SyncRunning,
    SyncConflict,
    SyncError,
    SyncChangesPending,
    SyncStandingBy,

    // Sync history previews
    SyncPassFailed,
    SyncPassConflicts,
    SyncPassSucceeded,

    // Ingestion backlog card
    BacklogWaiting,
    BacklogClear,

    // Workflow activity card: its status, then its recent-activity line
    WorkflowSuccessRate,
    WorkflowRunsRecorded,
    WorkflowReadyToAutomate,
    WorkflowsActiveRecently,
    WorkflowsEnabled,
    WorkflowsNoRecentRuns,

    // Workflow run previews
    RunCompleted,
    RunFailed,
    RunCancelled,
    RunRunning,
    RunPending,
    RunRecorded,

    // Connectors card
    ConnectorsEnabled,
    PluginsEnabled,
    PluginsInstalled,
    NoPluginsInstalled,

    // Connector previews
    ConnectorEnabled,
    ConnectorDisabled,
    PluginInstalled,

    /// <summary>An inbox or imported item that names no source.</summary>
    SourcePending,

    /// <summary>The label an empty list shows in place of a status.</summary>
    Placeholder,
}

/// <summary>How far an imported document got on its way to being searchable.</summary>
public enum OperationsDocumentHealth
{
    None,
    Searchable,
    Processing,
    NeedsAttention,
}

/// <summary>
/// The status colors of the operations surfaces, as the tone tokens StatusToColorConverter reads
/// (the convention of OnboardingViewModel.ConnectionStatusTone). Each kind maps to a token that
/// resolves to the tone its English wording always had, so the colors are the same in every
/// language. A status taken from the data is its own token and resolves from its text as before.
/// </summary>
public static class OperationsStatusTones
{
    private const string SuccessToken = "success";
    private const string WarningToken = "pending";
    private const string DangerToken = "failed";
    private const string InfoToken = "running";
    private const string NeutralToken = "idle";

    public static string TokenFor(OperationsStatusKind kind, string status) => kind switch
    {
        OperationsStatusKind.RecallRefreshesPending
            or OperationsStatusKind.RecallStaleSummaries
            or OperationsStatusKind.SummaryStale
            or OperationsStatusKind.SummaryPending
            or OperationsStatusKind.SyncConflict
            or OperationsStatusKind.SyncChangesPending
            or OperationsStatusKind.SyncPassConflicts
            or OperationsStatusKind.RunCancelled
            or OperationsStatusKind.RunPending
            or OperationsStatusKind.SourcePending => WarningToken,

        OperationsStatusKind.RecallCurrent
            or OperationsStatusKind.SummaryCurrent
            or OperationsStatusKind.SyncPassSucceeded
            or OperationsStatusKind.WorkflowSuccessRate
            or OperationsStatusKind.WorkflowReadyToAutomate
            or OperationsStatusKind.WorkflowsActiveRecently
            or OperationsStatusKind.WorkflowsEnabled
            or OperationsStatusKind.RunCompleted
            or OperationsStatusKind.ConnectorsEnabled
            or OperationsStatusKind.PluginsEnabled
            or OperationsStatusKind.ConnectorEnabled => SuccessToken,

        OperationsStatusKind.SummaryRefreshError
            or OperationsStatusKind.SyncPassFailed
            or OperationsStatusKind.RunFailed => DangerToken,

        OperationsStatusKind.RunRunning => InfoToken,

        OperationsStatusKind.Other => status,

        _ => NeutralToken,
    };

    public static string TokenFor(OperationsDocumentHealth health, string healthStatus) => health switch
    {
        OperationsDocumentHealth.Searchable => SuccessToken,
        OperationsDocumentHealth.Processing => WarningToken,
        OperationsDocumentHealth.NeedsAttention => DangerToken,
        _ => healthStatus,
    };
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

    /// <summary>Which status <see cref="Status"/> words.</summary>
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>Which recent-activity line <see cref="SupportingPrimary"/> words (workflow card).</summary>
    public OperationsStatusKind SupportingPrimaryKind { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);
}

public sealed record OperationsConversationPreview
{
    public long ConversationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);
}

public sealed record OperationsSyncPreview
{
    public long SyncLogId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);
}

public sealed record OperationsInboxPreview
{
    public long ItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);
}

public sealed record OperationsImportedDocumentPreview
{
    public long DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string HealthStatus { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>Which health <see cref="HealthStatus"/> words.</summary>
    public OperationsDocumentHealth Health { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);

    /// <summary>The tone token the health badge is colored with.</summary>
    public string HealthToneToken => OperationsStatusTones.TokenFor(Health, HealthStatus);
    public bool HasHealthStatus => !string.IsNullOrWhiteSpace(HealthStatus);
    public bool CanRetryIndexingFromOperations =>
        DocumentId > 0 &&
        Health == OperationsDocumentHealth.NeedsAttention;
}

public sealed record OperationsWorkflowRunPreview
{
    public long WorkflowId { get; init; }
    public long RunId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);

    /// <summary>The run failed or was cancelled, so it is worth reviewing.</summary>
    public bool NeedsReview =>
        StatusKind is OperationsStatusKind.RunFailed or OperationsStatusKind.RunCancelled;
}

public sealed record OperationsConnectorPreview
{
    public long PluginId { get; init; }
    public bool IsEnabled { get; init; }
    public bool CanEnableFromOperations { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public OperationsStatusKind StatusKind { get; init; }

    /// <summary>The tone token the status badge is colored with.</summary>
    public string StatusToneToken => OperationsStatusTones.TokenFor(StatusKind, Status);
}
