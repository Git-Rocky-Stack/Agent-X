using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentX.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    BackupType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "manual"),
                    SizeMB = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IconGlyph = table.Column<string>(type: "TEXT", nullable: true),
                    ColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    ParentCollectionId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collections_collections_ParentCollectionId",
                        column: x => x.ParentCollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TokensUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", nullable: true),
                    ParentConversationId = table.Column<long>(type: "INTEGER", nullable: true),
                    BranchPointMessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    BranchLabel = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversations_conversations_ParentConversationId",
                        column: x => x.ParentConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "digest_reports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NewDocumentsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NewConversationsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSearches = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTokensUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    StorageDeltaBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    TopSearchesJson = table.Column<string>(type: "TEXT", nullable: true),
                    TopCollectionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    FileTypeBreakdownJson = table.Column<string>(type: "TEXT", nullable: true),
                    HighlightsJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digest_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileType = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastIndexedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IndexingStatus = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    IndexingError = table.Column<string>(type: "TEXT", nullable: true),
                    ChunkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WordCount = table.Column<long>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    ExtractedTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_items",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileType = table.Column<string>(type: "TEXT", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    Preview = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedCollectionId = table.Column<long>(type: "INTEGER", nullable: true),
                    SuggestedCollectionName = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedTags = table.Column<string>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WatchFolderId = table.Column<long>(type: "INTEGER", nullable: true),
                    SourceType = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePluginId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SourceCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "licenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LicenseKey = table.Column<string>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", nullable: true),
                    Tier = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "starter"),
                    IsActivated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CustomerEmail = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_licenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "memories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "fact"),
                    SourceConversationId = table.Column<long>(type: "INTEGER", nullable: true),
                    Importance = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.5),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_credentials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshToken = table.Column<string>(type: "TEXT", nullable: false),
                    TokenExpiry = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plugins",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PluginId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PluginType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Custom"),
                    InstallPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    InstalledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ReadmeContent = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "search_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    SearchType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "semantic"),
                    ResultCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSaved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CollectionFilter = table.Column<string>(type: "TEXT", nullable: true),
                    MinScore = table.Column<double>(type: "REAL", nullable: true),
                    MaxResults = table.Column<int>(type: "INTEGER", nullable: true),
                    DateAfter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateBefore = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sync_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    ChangesApplied = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictsDetected = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictsResolved = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<double>(type: "REAL", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_prompts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "General"),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_prompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    IsAutoGenerated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ValueType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "string"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Icon = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Custom"),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RunCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_profiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ActiveModelId = table.Column<string>(type: "TEXT", nullable: true),
                    ActiveCollectionIds = table.Column<string>(type: "TEXT", nullable: true),
                    CustomSettings = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "watch_folders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeSubfolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    FileTypeFilter = table.Column<string>(type: "TEXT", nullable: true),
                    TargetCollectionId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScanAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilesIndexed = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watch_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_watch_folders_collections_TargetCollectionId",
                        column: x => x.TargetCollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GenerationTimeMs = table.Column<double>(type: "REAL", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    CitationsJson = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messages_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "annotations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChunkId = table.Column<long>(type: "INTEGER", nullable: true),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    HighlightedText = table.Column<string>(type: "TEXT", nullable: false),
                    NoteText = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "yellow"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_annotations_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    StartCharOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndCharOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    SectionTitle = table.Column<string>(type: "TEXT", nullable: true),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEmbedded = table.Column<bool>(type: "INTEGER", nullable: false),
                    VectorRowId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_chunks_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_collections",
                columns: table => new
                {
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_collections", x => new { x.DocumentId, x.CollectionId });
                    table.ForeignKey(
                        name: "FK_document_collections_collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_collections_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexing_jobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "queued"),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ChunksProcessed = table.Column<int>(type: "INTEGER", nullable: false),
                    EmbeddingsGenerated = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessingTimeMs = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexing_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexing_jobs_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_tags",
                columns: table => new
                {
                    ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                    TagId = table.Column<long>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_tags", x => new { x.ConversationId, x.TagId });
                    table.ForeignKey(
                        name: "FK_conversation_tags_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_tags",
                columns: table => new
                {
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    TagId = table.Column<long>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_tags", x => new { x.DocumentId, x.TagId });
                    table.ForeignKey(
                        name: "FK_document_tags_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    InitialInput = table.Column<string>(type: "TEXT", nullable: true),
                    FinalOutput = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StepsCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSteps = table.Column<int>(type: "INTEGER", nullable: false),
                    StepOutputsJson = table.Column<string>(type: "TEXT", nullable: true),
                    TotalTokensUsed = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_runs_workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowId = table.Column<long>(type: "INTEGER", nullable: false),
                    StepOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    StepType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "AiPrompt"),
                    PromptTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    ModelOverride = table.Column<string>(type: "TEXT", nullable: true),
                    TemperatureOverride = table.Column<double>(type: "REAL", nullable: true),
                    MaxTokensOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_steps_workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Rating = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "none"),
                    PreferredResponse = table.Column<string>(type: "TEXT", nullable: true),
                    FeedbackNote = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_feedback_messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_annotations_ChunkId",
                table: "annotations",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_annotations_Color",
                table: "annotations",
                column: "Color");

            migrationBuilder.CreateIndex(
                name: "IX_annotations_CreatedAt",
                table: "annotations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_annotations_DocumentId",
                table: "annotations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_backups_BackupType",
                table: "backups",
                column: "BackupType");

            migrationBuilder.CreateIndex(
                name: "IX_backups_CreatedAt",
                table: "backups",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_backups_IsValid",
                table: "backups",
                column: "IsValid");

            migrationBuilder.CreateIndex(
                name: "IX_collections_ParentCollectionId",
                table: "collections",
                column: "ParentCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_tags_TagId",
                table: "conversation_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_BranchPointMessageId",
                table: "conversations",
                column: "BranchPointMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_CreatedAt",
                table: "conversations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_IsPinned",
                table: "conversations",
                column: "IsPinned");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_ParentConversationId",
                table: "conversations",
                column: "ParentConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_UpdatedAt",
                table: "conversations",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_digest_reports_GeneratedAt",
                table: "digest_reports",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_digest_reports_IsRead",
                table: "digest_reports",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_DocumentId_ChunkIndex",
                table: "document_chunks",
                columns: new[] { "DocumentId", "ChunkIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_VectorRowId",
                table: "document_chunks",
                column: "VectorRowId");

            migrationBuilder.CreateIndex(
                name: "IX_document_collections_CollectionId",
                table: "document_collections",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_tags_TagId",
                table: "document_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_ContentHash",
                table: "documents",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_documents_FileName",
                table: "documents",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_documents_FileType",
                table: "documents",
                column: "FileType");

            migrationBuilder.CreateIndex(
                name: "IX_documents_ImportedAt",
                table: "documents",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_documents_IndexingStatus",
                table: "documents",
                column: "IndexingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_ConversationId",
                table: "feedback",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_CreatedAt",
                table: "feedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_MessageId",
                table: "feedback",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_Rating",
                table: "feedback",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_items_AddedAt",
                table: "inbox_items",
                column: "AddedAt");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_items_DocumentId",
                table: "inbox_items",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_items_ExternalId_SourcePluginId",
                table: "inbox_items",
                columns: new[] { "ExternalId", "SourcePluginId" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_items_Status",
                table: "inbox_items",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_items_WatchFolderId",
                table: "inbox_items",
                column: "WatchFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_indexing_jobs_DocumentId",
                table: "indexing_jobs",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_indexing_jobs_QueuedAt",
                table: "indexing_jobs",
                column: "QueuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_indexing_jobs_Status",
                table: "indexing_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_memories_Category",
                table: "memories",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_memories_Importance",
                table: "memories",
                column: "Importance");

            migrationBuilder.CreateIndex(
                name: "IX_memories_IsActive",
                table: "memories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_messages_ConversationId_SortOrder",
                table: "messages",
                columns: new[] { "ConversationId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_credentials_ProviderId",
                table: "oauth_credentials",
                column: "ProviderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugins_InstalledAt",
                table: "plugins",
                column: "InstalledAt");

            migrationBuilder.CreateIndex(
                name: "IX_plugins_IsEnabled",
                table: "plugins",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_plugins_Name",
                table: "plugins",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_plugins_PluginId",
                table: "plugins",
                column: "PluginId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugins_PluginType",
                table: "plugins",
                column: "PluginType");

            migrationBuilder.CreateIndex(
                name: "IX_search_history_SearchedAt",
                table: "search_history",
                column: "SearchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_sync_logs_Direction",
                table: "sync_logs",
                column: "Direction");

            migrationBuilder.CreateIndex(
                name: "IX_sync_logs_IsSuccess",
                table: "sync_logs",
                column: "IsSuccess");

            migrationBuilder.CreateIndex(
                name: "IX_sync_logs_SyncedAt",
                table: "sync_logs",
                column: "SyncedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Name",
                table: "tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_settings_Key",
                table: "user_settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_watch_folders_FolderPath",
                table: "watch_folders",
                column: "FolderPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_watch_folders_TargetCollectionId",
                table: "watch_folders",
                column: "TargetCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_StartedAt",
                table: "workflow_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_Status",
                table: "workflow_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_WorkflowId",
                table: "workflow_runs",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_steps_WorkflowId_StepOrder",
                table: "workflow_steps",
                columns: new[] { "WorkflowId", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_workflows_Category",
                table: "workflows",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_workflows_IsBuiltIn",
                table: "workflows",
                column: "IsBuiltIn");

            migrationBuilder.CreateIndex(
                name: "IX_workflows_IsEnabled",
                table: "workflows",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_profiles_CreatedAt",
                table: "workspace_profiles",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_profiles_IsDefault",
                table: "workspace_profiles",
                column: "IsDefault");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annotations");

            migrationBuilder.DropTable(
                name: "backups");

            migrationBuilder.DropTable(
                name: "conversation_tags");

            migrationBuilder.DropTable(
                name: "digest_reports");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "document_collections");

            migrationBuilder.DropTable(
                name: "document_tags");

            migrationBuilder.DropTable(
                name: "feedback");

            migrationBuilder.DropTable(
                name: "inbox_items");

            migrationBuilder.DropTable(
                name: "indexing_jobs");

            migrationBuilder.DropTable(
                name: "licenses");

            migrationBuilder.DropTable(
                name: "memories");

            migrationBuilder.DropTable(
                name: "oauth_credentials");

            migrationBuilder.DropTable(
                name: "plugins");

            migrationBuilder.DropTable(
                name: "search_history");

            migrationBuilder.DropTable(
                name: "sync_logs");

            migrationBuilder.DropTable(
                name: "system_prompts");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "watch_folders");

            migrationBuilder.DropTable(
                name: "workflow_runs");

            migrationBuilder.DropTable(
                name: "workflow_steps");

            migrationBuilder.DropTable(
                name: "workspace_profiles");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "collections");

            migrationBuilder.DropTable(
                name: "workflows");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
