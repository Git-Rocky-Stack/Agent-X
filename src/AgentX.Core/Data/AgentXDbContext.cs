using System.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.TemporalIdentity.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgentX.Core.Data;

public class AgentXDbContext : DbContext
{
    // DbSets for all entities
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<ConversationSummarySnapshotEntity> ConversationSummarySnapshots => Set<ConversationSummarySnapshotEntity>();
    public DbSet<ConversationSummaryStateEntity> ConversationSummaryStates => Set<ConversationSummaryStateEntity>();
    public DbSet<ConversationThemeClusterEntity> ConversationThemeClusters => Set<ConversationThemeClusterEntity>();
    public DbSet<ConversationThemeDailyMetricEntity> ConversationThemeDailyMetrics => Set<ConversationThemeDailyMetricEntity>();
    public DbSet<ConversationThemeMembershipEntity> ConversationThemeMemberships => Set<ConversationThemeMembershipEntity>();
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();
    public DbSet<CollectionEntity> Collections => Set<CollectionEntity>();
    public DbSet<DocumentCollectionEntity> DocumentCollections => Set<DocumentCollectionEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<DocumentTagEntity> DocumentTags => Set<DocumentTagEntity>();
    public DbSet<ConversationTagEntity> ConversationTags => Set<ConversationTagEntity>();
    public DbSet<SearchHistoryEntity> SearchHistory => Set<SearchHistoryEntity>();
    public DbSet<SystemPromptEntity> SystemPrompts => Set<SystemPromptEntity>();
    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();
    public DbSet<WatchFolderEntity> WatchFolders => Set<WatchFolderEntity>();
    public DbSet<IndexingJobEntity> IndexingJobs => Set<IndexingJobEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    public DbSet<DigestReportEntity> DigestReports => Set<DigestReportEntity>();
    public DbSet<WorkflowEntity> Workflows => Set<WorkflowEntity>();
    public DbSet<WorkflowStepEntity> WorkflowSteps => Set<WorkflowStepEntity>();
    public DbSet<WorkflowRunEntity> WorkflowRuns => Set<WorkflowRunEntity>();
    public DbSet<AnnotationEntity> Annotations => Set<AnnotationEntity>();
    public DbSet<BackupEntity> Backups => Set<BackupEntity>();
    public DbSet<InboxItemEntity> InboxItems => Set<InboxItemEntity>();
    public DbSet<WorkspaceProfileEntity> WorkspaceProfiles => Set<WorkspaceProfileEntity>();
    public DbSet<SyncLogEntity> SyncLogs => Set<SyncLogEntity>();
    public DbSet<PluginEntity> Plugins => Set<PluginEntity>();
    public DbSet<FeedbackEntity> Feedbacks => Set<FeedbackEntity>();
    public DbSet<OAuthCredentialEntity> OAuthCredentials => Set<OAuthCredentialEntity>();

    // Temporal Identity — tracks belief evolution, insights, and voice
    public DbSet<TemporalBeliefEntity> TemporalBeliefs => Set<TemporalBeliefEntity>();
    public DbSet<InsightMomentEntity> InsightMoments => Set<InsightMomentEntity>();
    public DbSet<EngagementMetricsEntity> EngagementMetrics => Set<EngagementMetricsEntity>();
    public DbSet<BeliefConflictEntity> BeliefConflicts => Set<BeliefConflictEntity>();
    public DbSet<VoiceProfileEntity> VoiceProfiles => Set<VoiceProfileEntity>();

    private readonly string _dbPath;
    private readonly IEncryptedConnectionFactory? _connectionFactory;

    public AgentXDbContext()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDataDir = Path.Combine(localAppData, "AgentX");
        Directory.CreateDirectory(appDataDir);
        _dbPath = Path.Combine(appDataDir, "agentx.db");
    }

    public AgentXDbContext(DbContextOptions<AgentXDbContext> options)
        : base(options)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDataDir = Path.Combine(localAppData, "AgentX");
        Directory.CreateDirectory(appDataDir);
        _dbPath = Path.Combine(appDataDir, "agentx.db");
    }

    /// <summary>
    /// DI-preferred constructor: injects the encrypted connection factory so PRAGMA key
    /// can be applied to the underlying connection via <see cref="EnsureKeyApplied"/>
    /// once the unlock flow has loaded key material.
    /// </summary>
    public AgentXDbContext(DbContextOptions<AgentXDbContext> options, IEncryptedConnectionFactory connectionFactory)
        : this(options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    /// <summary>
    /// Opens the underlying connection (if closed) and applies the current database key
    /// via PRAGMA key. Called from startup once after the unlock flow and before any
    /// migrations or queries run. Idempotent — safe to call multiple times.
    /// No-op when no factory was injected (EF tooling path) or when no key is loaded.
    /// </summary>
    public void EnsureKeyApplied()
    {
        if (_connectionFactory is null) return;
        var conn = Database.GetDbConnection();
        if (conn.State == ConnectionState.Closed)
            conn.Open();
        _connectionFactory.ApplyKey((SqliteConnection)conn);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureConversation(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureConversationSummarySnapshot(modelBuilder);
        ConfigureConversationSummaryState(modelBuilder);
        ConfigureConversationThemeCluster(modelBuilder);
        ConfigureConversationThemeDailyMetric(modelBuilder);
        ConfigureConversationThemeMembership(modelBuilder);
        ConfigureDocument(modelBuilder);
        ConfigureDocumentChunk(modelBuilder);
        ConfigureCollection(modelBuilder);
        ConfigureDocumentCollection(modelBuilder);
        ConfigureTag(modelBuilder);
        ConfigureDocumentTag(modelBuilder);
        ConfigureConversationTag(modelBuilder);
        ConfigureSearchHistory(modelBuilder);
        ConfigureSystemPrompt(modelBuilder);
        ConfigureUserSettings(modelBuilder);
        ConfigureWatchFolder(modelBuilder);
        ConfigureIndexingJob(modelBuilder);
        ConfigureMemory(modelBuilder);
        ConfigureDigestReport(modelBuilder);
        ConfigureWorkflow(modelBuilder);
        ConfigureWorkflowStep(modelBuilder);
        ConfigureWorkflowRun(modelBuilder);
        ConfigureAnnotation(modelBuilder);
        ConfigureBackup(modelBuilder);
        ConfigureInboxItem(modelBuilder);
        ConfigureWorkspaceProfile(modelBuilder);
        ConfigureSyncLog(modelBuilder);
        ConfigurePlugin(modelBuilder);
        ConfigureFeedback(modelBuilder);
        ConfigureOAuthCredential(modelBuilder);
        ConfigureTemporalIdentity(modelBuilder);
    }

    private static void ConfigureTemporalIdentity(ModelBuilder modelBuilder)
    {
        // TemporalBeliefEntity
        modelBuilder.Entity<TemporalBeliefEntity>(entity =>
        {
            entity.ToTable("temporal_beliefs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Topic).IsRequired();
            entity.Property(e => e.SentimentScore).IsRequired();
            entity.Property(e => e.ConfidenceLevel).IsRequired();
            entity.Property(e => e.CurrentStance).IsRequired();
            entity.Property(e => e.EvidenceJson).IsRequired().HasDefaultValue("[]");
            entity.Property(e => e.FirstDetectedAt).IsRequired();
            entity.Property(e => e.LastObservedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => e.Topic);
            entity.HasIndex(e => e.LastObservedAt);
            entity.HasIndex(e => e.HasEvolved);
        });

        // InsightMomentEntity
        modelBuilder.Entity<InsightMomentEntity>(entity =>
        {
            entity.ToTable("insight_moments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Topic).IsRequired();
            entity.Property(e => e.InsightText).IsRequired();
            entity.Property(e => e.SignificanceScore).IsRequired();
            entity.Property(e => e.CapturedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.RelatedTopicsJson).IsRequired().HasDefaultValue("[]");

            entity.HasIndex(e => e.SignificanceScore);
            entity.HasIndex(e => e.CapturedAt);
            entity.HasIndex(e => e.HasBeenResurfaced);
        });

        // EngagementMetricsEntity
        modelBuilder.Entity<EngagementMetricsEntity>(entity =>
        {
            entity.ToTable("engagement_metrics");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TargetType).IsRequired();
            entity.Property(e => e.TargetId).IsRequired();
            entity.Property(e => e.FirstEngagedAt).IsRequired();
            entity.Property(e => e.LastEngagedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.TotalSecondsSpent).IsRequired();
            entity.Property(e => e.RevisitCount).IsRequired();
            entity.Property(e => e.Depth).IsRequired();
            entity.Property(e => e.TopicsJson).IsRequired().HasDefaultValue("[]");

            entity.HasIndex(e => new { e.TargetType, e.TargetId });
            entity.HasIndex(e => e.LastEngagedAt);
            entity.HasIndex(e => e.Depth);
        });

        // BeliefConflictEntity
        modelBuilder.Entity<BeliefConflictEntity>(entity =>
        {
            entity.ToTable("belief_conflicts");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.BeliefId).IsRequired();
            entity.Property(e => e.PreviousStance).IsRequired();
            entity.Property(e => e.CurrentStance).IsRequired();
            entity.Property(e => e.DetectedAt).IsRequired();
            entity.Property(e => e.ConflictMagnitude).IsRequired();

            entity.HasOne(e => e.Belief)
                .WithMany()
                .HasForeignKey(e => e.BeliefId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DetectedAt);
            entity.HasIndex(e => e.HasBeenAcknowledged);
            entity.HasIndex(e => e.ConflictMagnitude);
        });

        // VoiceProfileEntity (singleton pattern)
        modelBuilder.Entity<VoiceProfileEntity>(entity =>
        {
            entity.ToTable("voice_profiles");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FirstSampleAt).IsRequired();
            entity.Property(e => e.LastSampleAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.SampleCount).IsRequired();
            entity.Property(e => e.AvgSentenceLength).IsRequired();
            entity.Property(e => e.FormalityScore).IsRequired();
            entity.Property(e => e.CharacteristicPhrasesJson).IsRequired().HasDefaultValue("[]");
            entity.Property(e => e.SentencePatternsJson).IsRequired().HasDefaultValue("[]");
            entity.Property(e => e.BookendsJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.StylisticTraitsJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.PronounPatterns).IsRequired().HasDefaultValue("");
        });
    }

    private static void ConfigureConversation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.ModelId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Self-referencing relationship: Conversation can have a parent and child branches
            entity.HasOne(e => e.ParentConversation)
                .WithMany(e => e.Branches)
                .HasForeignKey(e => e.ParentConversationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Indexes
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UpdatedAt);
            entity.HasIndex(e => e.IsPinned);
            entity.HasIndex(e => e.ParentConversationId);
            entity.HasIndex(e => e.BranchPointMessageId);
        });
    }

    private static void ConfigureMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ConversationId).IsRequired();
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Embedding).IsRequired(false);
            entity.Property(e => e.EmbeddingModel).IsRequired(false);
            entity.Property(e => e.EmbeddedAt).IsRequired(false);

            // Relationship: Message belongs to Conversation
            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => new { e.ConversationId, e.SortOrder });
            entity.HasIndex(e => e.EmbeddedAt);
        });
    }

    private static void ConfigureConversationSummarySnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationSummarySnapshotEntity>(entity =>
        {
            entity.ToTable("conversation_summary_snapshots");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ConversationId).IsRequired();
            entity.Property(e => e.SnapshotVersion).IsRequired();
            entity.Property(e => e.SummaryText).IsRequired();
            entity.Property(e => e.PreviewText).IsRequired();
            entity.Property(e => e.KeyPointsJson).IsRequired().HasDefaultValue("[]");
            entity.Property(e => e.CoveredMessageCount).IsRequired();
            entity.Property(e => e.GeneratedAt).IsRequired();
            entity.Property(e => e.SourceConversationUpdatedAt).IsRequired();
            entity.Property(e => e.IsIncremental).IsRequired();
            entity.Property(e => e.Embedding).IsRequired(false);
            entity.Property(e => e.EmbeddingModel).IsRequired(false);
            entity.Property(e => e.EmbeddedAt).IsRequired(false);

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.SummarySnapshots)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ConversationId, e.SnapshotVersion }).IsUnique();
            entity.HasIndex(e => e.GeneratedAt);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.EmbeddedAt);
        });
    }

    private static void ConfigureConversationSummaryState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationSummaryStateEntity>(entity =>
        {
            entity.ToTable("conversation_summary_states");
            entity.HasKey(e => e.ConversationId);

            entity.Property(e => e.ConversationId).IsRequired();
            entity.Property(e => e.LatestSnapshotVersion).IsRequired();
            entity.Property(e => e.LastCoveredMessageCount).IsRequired();
            entity.Property(e => e.PendingMessageCount).IsRequired();
            entity.Property(e => e.IsStale).IsRequired();
            entity.Property(e => e.ConsecutiveFailureCount).IsRequired();

            entity.HasOne(e => e.Conversation)
                .WithOne(c => c.SummaryState)
                .HasForeignKey<ConversationSummaryStateEntity>(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LatestSnapshot)
                .WithMany()
                .HasForeignKey(e => e.LatestSnapshotId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasIndex(e => e.IsStale);
            entity.HasIndex(e => e.LastRefreshedAt);
            entity.HasIndex(e => e.LatestSnapshotId);
        });
    }

    private static void ConfigureConversationThemeCluster(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationThemeClusterEntity>(entity =>
        {
            entity.ToTable("conversation_theme_clusters");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Label).IsRequired();
            entity.Property(e => e.PreviewText).IsRequired();
            entity.Property(e => e.KeyPointsJson).IsRequired().HasDefaultValue("[]");
            entity.Property(e => e.ConversationCount).IsRequired();
            entity.Property(e => e.ActiveConversationCount7d).IsRequired();
            entity.Property(e => e.ActiveConversationCount30d).IsRequired();
            entity.Property(e => e.FirstSeenAt).IsRequired();
            entity.Property(e => e.LastActiveAt).IsRequired();
            entity.Property(e => e.MaterializedAt).IsRequired();

            entity.HasIndex(e => e.LastActiveAt);
            entity.HasIndex(e => e.MaterializedAt);
            entity.HasIndex(e => e.FirstSeenAt);
        });
    }

    private static void ConfigureConversationThemeDailyMetric(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationThemeDailyMetricEntity>(entity =>
        {
            entity.ToTable("conversation_theme_daily_metrics");
            entity.HasKey(e => new { e.ClusterId, e.Date });

            entity.Property(e => e.ClusterId).IsRequired();
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.ActiveConversationCount).IsRequired();
            entity.Property(e => e.NewConversationCount).IsRequired();
            entity.Property(e => e.SnapshotRefreshCount).IsRequired();
            entity.Property(e => e.MaterializedAt).IsRequired();

            entity.HasOne(e => e.Cluster)
                .WithMany(c => c.DailyMetrics)
                .HasForeignKey(e => e.ClusterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.MaterializedAt);
        });
    }

    private static void ConfigureConversationThemeMembership(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationThemeMembershipEntity>(entity =>
        {
            entity.ToTable("conversation_theme_memberships");
            entity.HasKey(e => e.ConversationId);

            entity.Property(e => e.ConversationId).IsRequired();
            entity.Property(e => e.SnapshotId).IsRequired();
            entity.Property(e => e.ClusterId).IsRequired();
            entity.Property(e => e.SimilarityScore).IsRequired();
            entity.Property(e => e.AssignedAt).IsRequired();

            entity.HasOne(e => e.Conversation)
                .WithOne(c => c.ThemeMembership)
                .HasForeignKey<ConversationThemeMembershipEntity>(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Snapshot)
                .WithMany(s => s.ThemeMemberships)
                .HasForeignKey(e => e.SnapshotId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Cluster)
                .WithMany(c => c.Memberships)
                .HasForeignKey(e => e.ClusterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ClusterId);
            entity.HasIndex(e => e.SnapshotId);
            entity.HasIndex(e => e.AssignedAt);
        });
    }

    private static void ConfigureDocument(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FileName).IsRequired();
            entity.Property(e => e.FilePath).IsRequired();
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.ContentHash).IsRequired();
            entity.Property(e => e.ImportedAt).IsRequired();
            entity.Property(e => e.FileModifiedAt).IsRequired();
            entity.Property(e => e.IndexingStatus).IsRequired().HasDefaultValue("pending");

            // Indexes
            entity.HasIndex(e => e.ContentHash);
            entity.HasIndex(e => e.FileType);
            entity.HasIndex(e => e.IndexingStatus);
            entity.HasIndex(e => e.ImportedAt);
            entity.HasIndex(e => e.FileName);
        });
    }

    private static void ConfigureDocumentChunk(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentChunkEntity>(entity =>
        {
            entity.ToTable("document_chunks");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DocumentId).IsRequired();
            entity.Property(e => e.ChunkIndex).IsRequired();
            entity.Property(e => e.Content).IsRequired();

            // Relationship: Chunk belongs to Document
            entity.HasOne(e => e.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex });
            entity.HasIndex(e => e.VectorRowId);
        });
    }

    private static void ConfigureCollection(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionEntity>(entity =>
        {
            entity.ToTable("collections");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Self-referencing relationship: Collection can have a parent and children
            entity.HasOne(e => e.ParentCollection)
                .WithMany(e => e.ChildCollections)
                .HasForeignKey(e => e.ParentCollectionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Indexes
            entity.HasIndex(e => e.ParentCollectionId);
        });
    }

    private static void ConfigureDocumentCollection(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentCollectionEntity>(entity =>
        {
            entity.ToTable("document_collections");

            // Composite primary key
            entity.HasKey(e => new { e.DocumentId, e.CollectionId });

            entity.Property(e => e.AddedAt).IsRequired();

            // Relationship: DocumentCollection -> Document
            entity.HasOne(e => e.Document)
                .WithMany(d => d.DocumentCollections)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relationship: DocumentCollection -> Collection
            entity.HasOne(e => e.Collection)
                .WithMany(c => c.DocumentCollections)
                .HasForeignKey(e => e.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTag(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TagEntity>(entity =>
        {
            entity.ToTable("tags");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Indexes
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }

    private static void ConfigureDocumentTag(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentTagEntity>(entity =>
        {
            entity.ToTable("document_tags");

            // Composite primary key
            entity.HasKey(e => new { e.DocumentId, e.TagId });

            entity.Property(e => e.Confidence).IsRequired();
            entity.Property(e => e.AssignedAt).IsRequired();

            // Relationship: DocumentTag -> Document
            entity.HasOne(e => e.Document)
                .WithMany(d => d.DocumentTags)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relationship: DocumentTag -> Tag
            entity.HasOne(e => e.Tag)
                .WithMany(t => t.DocumentTags)
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureConversationTag(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationTagEntity>(entity =>
        {
            entity.ToTable("conversation_tags");
            entity.HasKey(e => new { e.ConversationId, e.TagId });
            entity.Property(e => e.AssignedAt).IsRequired();

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.ConversationTags)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tag)
                .WithMany(t => t.ConversationTags)
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSearchHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchHistoryEntity>(entity =>
        {
            entity.ToTable("search_history");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Query).IsRequired();
            entity.Property(e => e.SearchType).IsRequired().HasDefaultValue("semantic");
            entity.Property(e => e.SearchedAt).IsRequired();

            // Indexes
            entity.HasIndex(e => e.SearchedAt);
        });
    }

    private static void ConfigureSystemPrompt(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemPromptEntity>(entity =>
        {
            entity.ToTable("system_prompts");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Category).IsRequired().HasDefaultValue("General");
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });
    }

    private static void ConfigureUserSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSettingsEntity>(entity =>
        {
            entity.ToTable("user_settings");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.ValueType).IsRequired().HasDefaultValue("string");
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Indexes
            entity.HasIndex(e => e.Key).IsUnique();
        });
    }

    private static void ConfigureWatchFolder(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WatchFolderEntity>(entity =>
        {
            entity.ToTable("watch_folders");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FolderPath).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Relationship: WatchFolder optionally targets a Collection
            entity.HasOne(e => e.TargetCollection)
                .WithMany()
                .HasForeignKey(e => e.TargetCollectionId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            // Indexes
            entity.HasIndex(e => e.FolderPath).IsUnique();
        });
    }

    private static void ConfigureIndexingJob(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndexingJobEntity>(entity =>
        {
            entity.ToTable("indexing_jobs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DocumentId).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasDefaultValue("queued");
            entity.Property(e => e.QueuedAt).IsRequired();

            // Relationship: IndexingJob belongs to Document
            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.QueuedAt);
        });
    }

    private static void ConfigureDigestReport(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DigestReportEntity>(entity =>
        {
            entity.ToTable("digest_reports");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.GeneratedAt).IsRequired();
            entity.Property(e => e.PeriodStart).IsRequired();
            entity.Property(e => e.PeriodEnd).IsRequired();

            // Indexes for efficient querying
            entity.HasIndex(e => e.GeneratedAt);
            entity.HasIndex(e => e.IsRead);
        });
    }

    private static void ConfigureMemory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryEntity>(entity =>
        {
            entity.ToTable("memories");
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Content).IsRequired();
            entity.Property(m => m.Category).HasDefaultValue("fact");
            entity.Property(m => m.Importance).HasDefaultValue(0.5);
            entity.Property(m => m.IsActive).HasDefaultValue(true);

            // Semantic Memory 2.0 properties
            entity.Property(m => m.Embedding).IsRequired(false);
            entity.Property(m => m.DecayRate).HasDefaultValue(0.01);
            entity.Property(m => m.Confidence).HasDefaultValue(0.8);
            entity.Property(m => m.Tags).IsRequired(false);

            // Self-referencing relationship for associative memory links
            entity.HasOne(m => m.LinkedMemory)
                .WithMany()
                .HasForeignKey(m => m.LinkedMemoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Indexes for efficient querying
            entity.HasIndex(m => m.Category);
            entity.HasIndex(m => m.IsActive);
            entity.HasIndex(m => m.Importance);
            entity.HasIndex(m => m.LinkedMemoryId);
            entity.HasIndex(m => m.LastUsedAt);
            entity.HasIndex(m => m.CreatedAt);
        });
    }

    private static void ConfigureWorkflow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowEntity>(entity =>
        {
            entity.ToTable("workflows");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Category).IsRequired().HasDefaultValue("Custom");
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Indexes
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.IsBuiltIn);
            entity.HasIndex(e => e.IsEnabled);
        });
    }

    private static void ConfigureWorkflowStep(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowStepEntity>(entity =>
        {
            entity.ToTable("workflow_steps");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.WorkflowId).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.StepType).IsRequired().HasDefaultValue("AiPrompt");
            entity.Property(e => e.PromptTemplate).IsRequired();

            // Relationship: Step belongs to Workflow
            entity.HasOne(e => e.Workflow)
                .WithMany(w => w.Steps)
                .HasForeignKey(e => e.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => new { e.WorkflowId, e.StepOrder });
        });
    }

    private static void ConfigureWorkflowRun(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowRunEntity>(entity =>
        {
            entity.ToTable("workflow_runs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.WorkflowId).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasDefaultValue("pending");
            entity.Property(e => e.StartedAt).IsRequired();

            // Relationship: Run belongs to Workflow
            entity.HasOne(e => e.Workflow)
                .WithMany(w => w.Runs)
                .HasForeignKey(e => e.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.WorkflowId);
        });
    }

    private static void ConfigureAnnotation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnnotationEntity>(entity =>
        {
            entity.ToTable("annotations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DocumentId).IsRequired();
            entity.Property(e => e.StartOffset).IsRequired();
            entity.Property(e => e.EndOffset).IsRequired();
            entity.Property(e => e.HighlightedText).IsRequired();
            entity.Property(e => e.Color).IsRequired().HasDefaultValue("yellow");
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Relationship: Annotation belongs to Document (cascade delete)
            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkId);
            entity.HasIndex(e => e.Color);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    private static void ConfigureBackup(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackupEntity>(entity =>
        {
            entity.ToTable("backups");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FileName).IsRequired();
            entity.Property(e => e.FilePath).IsRequired();
            entity.Property(e => e.BackupType).IsRequired().HasDefaultValue("manual");
            entity.Property(e => e.SizeMB).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.IsValid).IsRequired().HasDefaultValue(true);

            // Indexes
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.BackupType);
            entity.HasIndex(e => e.IsValid);
        });
    }

    private static void ConfigureInboxItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxItemEntity>(entity =>
        {
            entity.ToTable("inbox_items");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FilePath).IsRequired();
            entity.Property(e => e.FileName).IsRequired();
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.FileSizeBytes).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasDefaultValue("pending");
            entity.Property(e => e.AddedAt).IsRequired();

            // Nullable columns — no IsRequired() call needed; EF infers nullable from the CLR type.
            entity.Property(e => e.Preview);
            entity.Property(e => e.SuggestedCollectionId);
            entity.Property(e => e.SuggestedCollectionName);
            entity.Property(e => e.SuggestedTags);
            entity.Property(e => e.ProcessedAt);
            entity.Property(e => e.WatchFolderId);
            entity.Property(e => e.SourceType);
            entity.Property(e => e.SourceUrl);
            entity.Property(e => e.SourcePluginId);
            entity.Property(e => e.SourceCategory);
            entity.Property(e => e.ExternalId);
            entity.Property(e => e.DocumentId);

            // Indexes to support the most common query patterns:
            //   - GetPendingItemsAsync / GetPendingCountAsync filter on Status
            //   - GetAllItemsAsync orders by AddedAt
            //   - Lookup by source watch folder
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.AddedAt);
            entity.HasIndex(e => e.WatchFolderId);
            entity.HasIndex(e => new { e.ExternalId, e.SourcePluginId });
            entity.HasIndex(e => e.DocumentId);
        });
    }

    private static void ConfigureSyncLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncLogEntity>(entity =>
        {
            entity.ToTable("sync_logs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.SyncedAt).IsRequired();
            entity.Property(e => e.Direction).IsRequired();
            entity.Property(e => e.ChangesApplied).IsRequired();
            entity.Property(e => e.ConflictsDetected).IsRequired();
            entity.Property(e => e.ConflictsResolved).IsRequired();
            entity.Property(e => e.DurationMs).IsRequired();
            entity.Property(e => e.IsSuccess).IsRequired();
            entity.Property(e => e.ErrorMessage);

            // Indexes to support history queries (newest first) and failure filtering
            entity.HasIndex(e => e.SyncedAt);
            entity.HasIndex(e => e.Direction);
            entity.HasIndex(e => e.IsSuccess);
        });
    }

    private static void ConfigurePlugin(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PluginEntity>(entity =>
        {
            entity.ToTable("plugins");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.PluginId).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Version).IsRequired();
            entity.Property(e => e.Author).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.PluginType).IsRequired().HasDefaultValue("Custom");
            entity.Property(e => e.InstallPath).IsRequired();
            entity.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.InstalledAt).IsRequired();
            entity.Property(e => e.LastActivatedAt);
            entity.Property(e => e.SettingsJson);
            entity.Property(e => e.ReadmeContent);

            // PluginId must be unique — one row per installed plugin identity.
            entity.HasIndex(e => e.PluginId).IsUnique();

            // Common query patterns: list by name, filter by type, filter by enabled state.
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.PluginType);
            entity.HasIndex(e => e.IsEnabled);
            entity.HasIndex(e => e.InstalledAt);
        });
    }

    private static void ConfigureWorkspaceProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceProfileEntity>(entity =>
        {
            entity.ToTable("workspace_profiles");
            entity.HasKey(e => e.Id);

            // Name is required; all other content columns are optional.
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Description);
            entity.Property(e => e.ActiveModelId);
            entity.Property(e => e.ActiveCollectionIds);
            entity.Property(e => e.CustomSettings);
            entity.Property(e => e.IsDefault).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Indexes:
            //   - GetDefaultProfileAsync filters on IsDefault
            //   - GetAllProfilesAsync orders by CreatedAt
            entity.HasIndex(e => e.IsDefault);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    private static void ConfigureFeedback(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeedbackEntity>(entity =>
        {
            entity.ToTable("feedback");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Rating).IsRequired().HasDefaultValue("none");
            entity.Property(e => e.ConversationId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.PreferredResponse);
            entity.Property(e => e.FeedbackNote);
            entity.Property(e => e.Category);

            entity.HasOne(e => e.Message)
                .WithMany()
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => e.Rating);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    private static void ConfigureOAuthCredential(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthCredentialEntity>(entity =>
        {
            entity.ToTable("oauth_credentials");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProviderId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AccessToken).IsRequired();
            entity.Property(e => e.RefreshToken).IsRequired();
            entity.Property(e => e.TokenExpiry).IsRequired();
            entity.Property(e => e.Scopes).IsRequired().HasMaxLength(500);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // ProviderId must be unique — one credential row per OAuth provider.
            entity.HasIndex(e => e.ProviderId).IsUnique();
        });
    }
}
