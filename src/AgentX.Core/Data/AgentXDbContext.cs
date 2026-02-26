using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentX.Core.Data;

public class AgentXDbContext : DbContext
{
    // DbSets for all entities
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();
    public DbSet<CollectionEntity> Collections => Set<CollectionEntity>();
    public DbSet<DocumentCollectionEntity> DocumentCollections => Set<DocumentCollectionEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<DocumentTagEntity> DocumentTags => Set<DocumentTagEntity>();
    public DbSet<SearchHistoryEntity> SearchHistory => Set<SearchHistoryEntity>();
    public DbSet<SystemPromptEntity> SystemPrompts => Set<SystemPromptEntity>();
    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();
    public DbSet<WatchFolderEntity> WatchFolders => Set<WatchFolderEntity>();
    public DbSet<IndexingJobEntity> IndexingJobs => Set<IndexingJobEntity>();
    public DbSet<LicenseEntity> Licenses => Set<LicenseEntity>();

    private readonly string _dbPath;

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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureConversation(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureDocument(modelBuilder);
        ConfigureDocumentChunk(modelBuilder);
        ConfigureCollection(modelBuilder);
        ConfigureDocumentCollection(modelBuilder);
        ConfigureTag(modelBuilder);
        ConfigureDocumentTag(modelBuilder);
        ConfigureSearchHistory(modelBuilder);
        ConfigureSystemPrompt(modelBuilder);
        ConfigureUserSettings(modelBuilder);
        ConfigureWatchFolder(modelBuilder);
        ConfigureIndexingJob(modelBuilder);
        ConfigureLicense(modelBuilder);
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

            // Indexes
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UpdatedAt);
            entity.HasIndex(e => e.IsPinned);
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

            // Relationship: Message belongs to Conversation
            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => new { e.ConversationId, e.SortOrder });
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

    private static void ConfigureLicense(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LicenseEntity>(entity =>
        {
            entity.ToTable("licenses");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.LicenseKey).IsRequired();
            entity.Property(e => e.Tier).IsRequired().HasDefaultValue("starter");
        });
    }
}
