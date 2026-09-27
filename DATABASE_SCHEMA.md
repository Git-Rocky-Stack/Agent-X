# Agent-X Database Schema Documentation

## Overview

Agent-X keeps its data in one SQLite database file, managed by **Entity Framework Core 8.0.11**
(`Microsoft.EntityFrameworkCore.Sqlite.Core` over `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.7). The file
is optionally encrypted with SQLCipher. It lives at a fixed path:

```
%LocalAppData%\AgentX\agentx.db
```

The EF Core context always opens this path. The vector store opens `agentx.db` in the folder named
by the `StoragePath` setting, which defaults to the same folder (it can only be changed by editing
`settings.json`; Settings shows it read-only as Data Location). The vector store sets
`PRAGMA journal_mode=WAL` when it opens the file, and SQLite keeps that journal mode in the file,
so `agentx.db-wal` and `agentx.db-shm` appear next to it while the app runs.

This document describes the schema as the code defines it:

- the EF Core model: `src/AgentX.Core/Data/AgentXDbContext.cs` (fluent configuration in
  `OnModelCreating`), the entity classes in `src/AgentX.Core/Data/Entities/`, and the five
  Temporal Identity entities in `src/AgentX.Core/Services/TemporalIdentity/Models/TemporalIdentityModels.cs`;
- the migrations in `src/AgentX.Core/Data/Migrations/`;
- the schema repairs `MigrationRunner` (`src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs`)
  applies at every start;
- the tables that are created with raw SQL outside the EF model (full-text search and vectors).

### Conventions

- **37 entity types, one table each.** Table names are set with `ToTable(...)` in snake_case.
  Column names are the C# property names (PascalCase), as EF generates them.
- **Configuration** is fluent, in one `ConfigureXxx(ModelBuilder)` method per area. The only data
  annotations are three `[MaxLength]` attributes on `InboxItemEntity`.
- **Storage types.** `long`, `int` and `bool` are `INTEGER` (booleans as 0 or 1), `double` and
  `float` are `REAL`, `string` and `DateTime` are `TEXT`. The three enum properties of the Temporal
  Identity entities are written as their integer value.
- **Nullability.** Value-type columns are `NOT NULL` unless the property is nullable (`long?`,
  `DateTime?` and so on). String columns are `NOT NULL` only where the model calls `IsRequired()`.
- **Timestamps** are written by the services in UTC.
- **Primary keys** are `INTEGER` `AUTOINCREMENT` `Id` columns, except for the junction and 1:1
  tables noted below, which use composite or shared keys.
- **Embeddings.** Document chunk vectors live in the `vec_embeddings` table (below), not in an EF
  column. Message, summary and memory embeddings are stored as comma-separated floats in `TEXT`
  columns.

In the column tables below, "req" means `NOT NULL` and "null" means nullable.

---

## Relationships

Foreign keys and delete behavior declared by the model (arrow = "has rows in"):

```
conversations
    -> messages                          ConversationId          cascade
         -> feedback                     MessageId (unique)      cascade
    -> conversation_tags                 ConversationId          cascade
    -> conversation_summary_snapshots    ConversationId          cascade
    -> conversation_summary_states       ConversationId (1:1)    cascade
    -> conversation_theme_memberships    ConversationId (1:1)    cascade
    -> conversations (branches)          ParentConversationId    restrict

conversation_summary_snapshots
    -> conversation_summary_states       LatestSnapshotId        restrict
    -> conversation_theme_memberships    SnapshotId              restrict

conversation_theme_clusters
    -> conversation_theme_memberships    ClusterId               cascade
    -> conversation_theme_daily_metrics  ClusterId               cascade

documents
    -> document_chunks                   DocumentId              cascade
    -> document_collections              DocumentId              cascade
    -> document_tags                     DocumentId              cascade
    -> annotations                       DocumentId              cascade
    -> indexing_jobs                     DocumentId              cascade

collections
    -> document_collections              CollectionId            cascade
    -> collections (children)            ParentCollectionId      restrict
    -> watch_folders                     TargetCollectionId      set null

tags
    -> document_tags                     TagId                   cascade
    -> conversation_tags                 TagId                   cascade

memories -> memories                     LinkedMemoryId          restrict
workflows -> workflow_steps              WorkflowId              cascade
workflows -> workflow_runs               WorkflowId              cascade
temporal_beliefs -> belief_conflicts     BeliefId                cascade
```

Columns that point at other rows without a foreign key: `conversations.BranchPointMessageId`,
`annotations.ChunkId`, `inbox_items.DocumentId`, `inbox_items.WatchFolderId`,
`inbox_items.SuggestedCollectionId`, `memories.SourceConversationId` and
`insight_moments.SourceId`. A `belief_conflicts` table created by the startup compatibility
schema (see [Startup repairs](#startup-repairs)) has no foreign-key constraint either.

Tables with no relationships: `search_history`, `system_prompts`, `user_settings`,
`digest_reports`, `backups`, `inbox_items`, `workspace_profiles`, `sync_logs`, `plugins`,
`oauth_credentials`, `insight_moments`, `engagement_metrics`, `voice_profiles`.

---

## Conversations and Chat

### ConversationEntity
**Table:** `conversations`
**Purpose:** A chat conversation. A branch is a conversation with a parent.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `Title` | TEXT | req | Conversation title |
| `SystemPrompt` | TEXT | null | System prompt used by the conversation |
| `ModelId` | TEXT | req | Model id recorded for the conversation |
| `CreatedAt` | TEXT | req | Creation time |
| `UpdatedAt` | TEXT | req | Last change |
| `IsPinned` | INTEGER | req | Pinned in the sidebar |
| `IsArchived` | INTEGER | req | Archived (the local REST API lists non-archived conversations only) |
| `MessageCount` | INTEGER | req | Denormalized message count |
| `TokensUsed` | INTEGER | req | Denormalized token total |
| `FolderName` | TEXT | null | Optional folder or category |
| `ParentConversationId` | INTEGER | null | Parent of a branch (FK `conversations.Id`, restrict) |
| `BranchPointMessageId` | INTEGER | null | Message in the parent where the branch diverges (no FK) |
| `BranchLabel` | TEXT | null | Optional branch label |

**Indexes:** `CreatedAt`, `UpdatedAt`, `IsPinned`, `ParentConversationId`, `BranchPointMessageId`.

---

### MessageEntity
**Table:** `messages`
**Purpose:** One message in a conversation.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `ConversationId` | INTEGER | req | FK `conversations.Id` (cascade) |
| `Role` | TEXT | req | `user`, `assistant` or `system` |
| `Content` | TEXT | req | Message text |
| `Timestamp` | TEXT | req | When the message was written |
| `TokenCount` | INTEGER | req | Tokens counted for the message |
| `GenerationTimeMs` | REAL | null | Time taken to generate an answer |
| `ModelId` | TEXT | null | Model that wrote an answer |
| `CitationsJson` | TEXT | null | JSON array of the sources an answer cited (for example Research Mode web results) |
| `SortOrder` | INTEGER | req | Position in the conversation |
| `Embedding` | TEXT | null | Comma-separated floats used by conversation recall |
| `EmbeddingModel` | TEXT | null | Embedding model of `Embedding` |
| `EmbeddingDimensions` | INTEGER | null | Vector size of `Embedding` |
| `EmbeddedAt` | TEXT | null | When `Embedding` was written |

**Indexes:** `(ConversationId, SortOrder)`, `EmbeddedAt`, `EmbeddingModel`.

---

### ConversationTagEntity
**Table:** `conversation_tags`
**Purpose:** Links conversations to tags.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `ConversationId` | INTEGER | req | Key part, FK `conversations.Id` (cascade) |
| `TagId` | INTEGER | req | Key part, FK `tags.Id` (cascade) |
| `AssignedAt` | TEXT | req | Assignment time |

**Primary key:** `(ConversationId, TagId)`. **Index:** `TagId`.

---

### ConversationSummarySnapshotEntity
**Table:** `conversation_summary_snapshots`
**Purpose:** Versioned summaries of a conversation, used for durable recall and theme clustering.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `ConversationId` | INTEGER | req | FK `conversations.Id` (cascade) |
| `SnapshotVersion` | INTEGER | req | Version number within the conversation |
| `SummaryText` | TEXT | req | Full summary |
| `PreviewText` | TEXT | req | Short preview |
| `KeyPointsJson` | TEXT | req | JSON array of key points, default `'[]'` |
| `CoveredMessageCount` | INTEGER | req | Messages the summary covers |
| `GeneratedAt` | TEXT | req | Generation time |
| `SourceConversationUpdatedAt` | TEXT | req | The conversation's `UpdatedAt` when summarized |
| `IsIncremental` | INTEGER | req | Built on the previous snapshot |
| `Embedding` | TEXT | null | Comma-separated floats |
| `EmbeddingModel` | TEXT | null | Embedding model |
| `EmbeddedAt` | TEXT | null | Embedding time |

**Indexes:** `(ConversationId, SnapshotVersion)` unique, `ConversationId`, `GeneratedAt`, `EmbeddedAt`.

---

### ConversationSummaryStateEntity
**Table:** `conversation_summary_states`
**Purpose:** Summarization progress for one conversation (1:1 with `conversations`).

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `ConversationId` | INTEGER | req | Primary key and FK `conversations.Id` (cascade) |
| `LatestSnapshotId` | INTEGER | null | FK `conversation_summary_snapshots.Id` (restrict) |
| `LatestSnapshotVersion` | INTEGER | req | Current snapshot version |
| `LastCoveredMessageCount` | INTEGER | req | Messages covered by the latest snapshot |
| `PendingMessageCount` | INTEGER | req | Messages not yet summarized |
| `IsStale` | INTEGER | req | The summary needs a refresh |
| `LastRefreshRequestedAt` | TEXT | null | Last refresh request |
| `LastRefreshAttemptedAt` | TEXT | null | Last refresh attempt |
| `LastRefreshedAt` | TEXT | null | Last successful refresh |
| `LastError` | TEXT | null | Last refresh error |
| `ConsecutiveFailureCount` | INTEGER | req | Failed refreshes in a row |

**Indexes:** `IsStale`, `LastRefreshedAt`, `LatestSnapshotId`.

---

### ConversationThemeClusterEntity
**Table:** `conversation_theme_clusters`
**Purpose:** Materialized clusters of related conversations.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `Label` | TEXT | req | Cluster label |
| `PreviewText` | TEXT | req | Preview text |
| `KeyPointsJson` | TEXT | req | JSON array, default `'[]'` |
| `ConversationCount` | INTEGER | req | Member conversations |
| `ActiveConversationCount7d` | INTEGER | req | Members active in the last 7 days |
| `ActiveConversationCount30d` | INTEGER | req | Members active in the last 30 days |
| `FirstSeenAt` | TEXT | req | First activity |
| `LastActiveAt` | TEXT | req | Latest activity |
| `MaterializedAt` | TEXT | req | When the cluster was computed |

**Indexes:** `FirstSeenAt`, `LastActiveAt`, `MaterializedAt`.

---

### ConversationThemeMembershipEntity
**Table:** `conversation_theme_memberships`
**Purpose:** Assigns a conversation to one cluster (1:1 with `conversations`).

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `ConversationId` | INTEGER | req | Primary key and FK `conversations.Id` (cascade) |
| `SnapshotId` | INTEGER | req | FK `conversation_summary_snapshots.Id` (restrict) |
| `ClusterId` | INTEGER | req | FK `conversation_theme_clusters.Id` (cascade) |
| `SimilarityScore` | REAL | req | Similarity to the cluster |
| `AssignedAt` | TEXT | req | Assignment time |

**Indexes:** `ClusterId`, `SnapshotId`, `AssignedAt`.

---

### ConversationThemeDailyMetricEntity
**Table:** `conversation_theme_daily_metrics`
**Purpose:** Per-day activity of a cluster.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `ClusterId` | INTEGER | req | Key part, FK `conversation_theme_clusters.Id` (cascade) |
| `Date` | TEXT | req | Key part, the day |
| `ActiveConversationCount` | INTEGER | req | Conversations active that day |
| `NewConversationCount` | INTEGER | req | Conversations started that day |
| `SnapshotRefreshCount` | INTEGER | req | Summary refreshes that day |
| `MaterializedAt` | TEXT | req | Computation time |

**Primary key:** `(ClusterId, Date)`. **Indexes:** `Date`, `MaterializedAt`.

---

### MemoryEntity
**Table:** `memories`
**Purpose:** Facts chat extracts about the user, injected into later prompts.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Content` | TEXT | req | | The remembered fact |
| `Category` | TEXT | req | `'fact'` | Category label written by the memory services (for example `fact`, `preference`, `topic`, `instruction`) |
| `SourceConversationId` | INTEGER | null | | Conversation it came from (no FK) |
| `Importance` | REAL | req | `0.5` | Base importance, 0 to 1 |
| `DecayRate` | REAL | req | `0.01` | Decay applied by days since last use |
| `UsageCount` | INTEGER | req | | Times used in a prompt |
| `CreatedAt` | TEXT | req | | Creation time |
| `LastUsedAt` | TEXT | req | | Last use |
| `IsActive` | INTEGER | req | `1` | Soft-delete flag |
| `Embedding` | TEXT | null | | Comma-separated floats |
| `LinkedMemoryId` | INTEGER | null | | FK `memories.Id` (restrict), associative link |
| `Confidence` | REAL | req | `0.8` | Extraction confidence |
| `Tags` | TEXT | null | | Comma-separated tags |
| `EmbeddingModelVersion` | TEXT | null | | Embedding model version |
| `EmbeddingDimensions` | INTEGER | null | | Vector size |
| `EmbeddedAt` | TEXT | null | | Embedding time |

**Indexes:** `Category`, `IsActive`, `Importance`, `LinkedMemoryId`, `LastUsedAt`, `CreatedAt`,
`EmbeddingModelVersion`.

---

### SystemPromptEntity
**Table:** `system_prompts`
**Purpose:** Reusable system prompts.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Name` | TEXT | req | | Prompt name |
| `Content` | TEXT | req | | Prompt text |
| `Category` | TEXT | req | `'General'` | Category label |
| `IsBuiltIn` | INTEGER | req | | Seeded by the app |
| `IsFavorite` | INTEGER | req | | Marked as favorite |
| `CreatedAt` | TEXT | req | | Creation time |
| `UpdatedAt` | TEXT | req | | Last change |
| `UsageCount` | INTEGER | req | | Times used |

No indexes besides the primary key.

---

### FeedbackEntity
**Table:** `feedback`
**Purpose:** The user's rating of an answer. One row per message.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `MessageId` | INTEGER | req | | FK `messages.Id` (cascade), unique |
| `ConversationId` | INTEGER | req | | Denormalized conversation id (no FK) |
| `Rating` | TEXT | req | `'none'` | `positive`, `negative` or `none` |
| `PreferredResponse` | TEXT | null | | Answer the user preferred |
| `FeedbackNote` | TEXT | null | | Free-text note |
| `Category` | TEXT | null | | What the rating is about |
| `CreatedAt` | TEXT | req | | First submission |
| `UpdatedAt` | TEXT | req | | Last update |

**Indexes:** `MessageId` unique, `Rating`, `ConversationId`, `CreatedAt`.

---

## Documents and the Knowledge Vault

### DocumentEntity
**Table:** `documents`
**Purpose:** One imported document.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `FileName` | TEXT | req | | File name |
| `FilePath` | TEXT | req | | Full path of the source file (the file is not copied) |
| `FileType` | TEXT | req | | Lower-case extension without the dot (for example `pdf`), or a connector type such as `CalendarEvent` or `EmailMessage` |
| `MimeType` | TEXT | null | | MIME type |
| `FileSizeBytes` | INTEGER | req | | File size |
| `ContentHash` | TEXT | req | | SHA-256 of the file, used for duplicate detection |
| `ImportedAt` | TEXT | req | | Import time |
| `FileModifiedAt` | TEXT | req | | File's last write time |
| `LastIndexedAt` | TEXT | null | | Last completed indexing |
| `IndexingStatus` | TEXT | req | `'pending'` | `pending`, `processing`, `completed` or `failed` |
| `IndexingError` | TEXT | null | | Why extraction or indexing failed |
| `ChunkCount` | INTEGER | req | | Chunks produced by the last indexing |
| `PageCount` | INTEGER | req | | Pages |
| `WordCount` | INTEGER | req | | Words in the extracted text |
| `Summary` | TEXT | null | | Stored summary |
| `ExtractedTitle` | TEXT | null | | Title found in the content |
| `Language` | TEXT | null | | Detected language |
| `ThumbnailPath` | TEXT | null | | Thumbnail file |
| `MetadataJson` | TEXT | null | | Extra metadata as JSON |

**Indexes:** `ContentHash`, `FileType`, `IndexingStatus`, `ImportedAt`, `FileName`.

---

### DocumentChunkEntity
**Table:** `document_chunks`
**Purpose:** A chunk of a document's text, the unit of search and retrieval.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key; also the key of the chunk's row in `vec_embeddings` and `fts_chunks` |
| `DocumentId` | INTEGER | req | FK `documents.Id` (cascade) |
| `ChunkIndex` | INTEGER | req | Position in the document |
| `Content` | TEXT | req | Chunk text |
| `StartCharOffset` | INTEGER | req | Start in the extracted text |
| `EndCharOffset` | INTEGER | req | End in the extracted text |
| `PageNumber` | INTEGER | null | Page, when known |
| `SectionTitle` | TEXT | null | Section heading, when known |
| `TokenCount` | INTEGER | req | Tokens in the chunk |
| `IsEmbedded` | INTEGER | req | The chunk has a vector |
| `VectorRowId` | INTEGER | null | Row id returned by the vector store |
| `EmbeddingModelVersion` | TEXT | null | `provider:model:dimensions` of the embedding (for example `ollama:all-minilm:384`); null or the legacy value `all-minilm:1.0` marks chunks embedded before versioning, which the indexer re-embeds when it is idle |
| `EmbeddingDimensions` | INTEGER | null | Vector size |
| `EmbeddedAt` | TEXT | null | Embedding time |

**Indexes:** `(DocumentId, ChunkIndex)`, `VectorRowId`, `EmbeddingModelVersion`.

---

### CollectionEntity
**Table:** `collections`
**Purpose:** A named group of documents. Collections can nest.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `Name` | TEXT | req | Collection name |
| `Description` | TEXT | null | Description |
| `IconGlyph` | TEXT | null | Icon glyph |
| `ColorHex` | TEXT | null | Color |
| `ParentCollectionId` | INTEGER | null | FK `collections.Id` (restrict) |
| `CreatedAt` | TEXT | req | Creation time |
| `UpdatedAt` | TEXT | req | Last change |
| `DocumentCount` | INTEGER | req | Denormalized document count |
| `SortOrder` | INTEGER | req | Display order |

**Index:** `ParentCollectionId`.

---

### DocumentCollectionEntity
**Table:** `document_collections`
**Purpose:** Links documents to collections.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `DocumentId` | INTEGER | req | Key part, FK `documents.Id` (cascade) |
| `CollectionId` | INTEGER | req | Key part, FK `collections.Id` (cascade) |
| `AddedAt` | TEXT | req | When the document was added |

**Primary key:** `(DocumentId, CollectionId)`. **Index:** `CollectionId`.

---

### TagEntity
**Table:** `tags`
**Purpose:** Tags shared by documents and conversations.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `Name` | TEXT | req | Tag name, unique |
| `ColorHex` | TEXT | null | Color |
| `IsAutoGenerated` | INTEGER | req | Created by auto-tagging |
| `CreatedAt` | TEXT | req | Creation time |

**Index:** `Name` unique.

---

### DocumentTagEntity
**Table:** `document_tags`
**Purpose:** Links documents to tags.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `DocumentId` | INTEGER | req | Key part, FK `documents.Id` (cascade) |
| `TagId` | INTEGER | req | Key part, FK `tags.Id` (cascade) |
| `Confidence` | REAL | req | 0 to 1 for auto-generated tags |
| `AssignedAt` | TEXT | req | Assignment time |

**Primary key:** `(DocumentId, TagId)`. **Index:** `TagId`.

---

### AnnotationEntity
**Table:** `annotations`
**Purpose:** A highlight, with an optional note, in a document's text.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `DocumentId` | INTEGER | req | | FK `documents.Id` (cascade) |
| `ChunkId` | INTEGER | null | | Chunk the highlight is in (no FK) |
| `StartOffset` | INTEGER | req | | Start of the highlight |
| `EndOffset` | INTEGER | req | | End of the highlight (exclusive) |
| `HighlightedText` | TEXT | req | | The highlighted text |
| `NoteText` | TEXT | null | | Note |
| `Color` | TEXT | req | `'yellow'` | `yellow`, `green`, `blue`, `red` or `purple` |
| `CreatedAt` | TEXT | req | | Creation time |
| `UpdatedAt` | TEXT | req | | Last change |

**Indexes:** `DocumentId`, `ChunkId`, `Color`, `CreatedAt`.

---

### IndexingJobEntity
**Table:** `indexing_jobs`
**Purpose:** One indexing run of a document.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `DocumentId` | INTEGER | req | | FK `documents.Id` (cascade) |
| `Status` | TEXT | req | `'queued'` | `queued`, `processing`, `completed` or `failed` |
| `QueuedAt` | TEXT | req | | Queue time |
| `StartedAt` | TEXT | null | | Start time |
| `CompletedAt` | TEXT | null | | End time |
| `ErrorMessage` | TEXT | null | | Failure reason |
| `ChunksProcessed` | INTEGER | req | | Chunks written |
| `EmbeddingsGenerated` | INTEGER | req | | Vectors written |
| `ProcessingTimeMs` | REAL | null | | Duration |

**Indexes:** `DocumentId`, `Status`, `QueuedAt`.

At startup the indexing service sets documents left in `processing` back to `pending` and jobs left
in `processing` back to `queued`.

---

### WatchFolderEntity
**Table:** `watch_folders`
**Purpose:** A folder that is monitored for new and changed files.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `FolderPath` | TEXT | req | Monitored folder, unique |
| `IsEnabled` | INTEGER | req | Monitoring on or off |
| `IncludeSubfolders` | INTEGER | req | Watch subfolders too |
| `FileTypeFilter` | TEXT | null | Comma-separated extensions |
| `TargetCollectionId` | INTEGER | null | FK `collections.Id` (set null) |
| `CreatedAt` | TEXT | req | Creation time |
| `LastScanAt` | TEXT | null | Last scan |
| `FilesIndexed` | INTEGER | req | Files imported from the folder |

**Indexes:** `FolderPath` unique, `TargetCollectionId`.

---

### SearchHistoryEntity
**Table:** `search_history`
**Purpose:** Past searches and saved searches.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Query` | TEXT | req | | Query text |
| `SearchType` | TEXT | req | `'semantic'` | `semantic`, `keyword` or `hybrid` |
| `ResultCount` | INTEGER | req | | Results returned |
| `SearchedAt` | TEXT | req | | Search time |
| `IsSaved` | INTEGER | req | | Saved search |
| `CollectionFilter` | TEXT | null | | Comma-separated collection ids |
| `MinScore` | REAL | null | | Saved minimum score |
| `MaxResults` | INTEGER | null | | Saved result limit |
| `DateAfter` | TEXT | null | | Saved date filter |
| `DateBefore` | TEXT | null | | Saved date filter |
| `SortOrder` | TEXT | null | | Saved sort (`relevance`, `newest`, `oldest`, `name`) |

**Index:** `SearchedAt`.

---

### InboxItemEntity
**Table:** `inbox_items`
**Purpose:** An item waiting in the Smart Inbox: a browser clip, a file, or an item a connector
(calendar, email) produced.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `FilePath` | TEXT | req | | Content file on disk |
| `FileName` | TEXT | req | | Display name |
| `FileType` | TEXT | req | | Category label (for example `CalendarEvent`, `EmailMessage`) |
| `FileSizeBytes` | INTEGER | req | | Size |
| `Status` | TEXT | req | `'pending'` | `pending`, `accepted`, `rejected` or `deferred` |
| `Preview` | TEXT | null | | Content preview |
| `SuggestedCollectionId` | INTEGER | null | | Suggested collection (no FK) |
| `SuggestedCollectionName` | TEXT | null | | Its name |
| `SuggestedTags` | TEXT | null | | Comma-separated tags |
| `AddedAt` | TEXT | req | | Arrival time |
| `ProcessedAt` | TEXT | null | | Accept, reject or defer time |
| `WatchFolderId` | INTEGER | null | | Watch folder that found it (no FK) |
| `SourceType` | TEXT | null | | How it arrived (for example `calendar-connector`, `email-connector`) |
| `SourceUrl` | TEXT | null | | Source link |
| `SourcePluginId` | TEXT(50) | null | | Connector id (`com.agentx.calendar`, `com.agentx.email`) |
| `SourceCategory` | TEXT(50) | null | | Category within the connector |
| `ExternalId` | TEXT(500) | null | | Provider id of the item, the de-duplication key with `SourcePluginId` |
| `DocumentId` | INTEGER | null | | Vault document created on accept (no FK) |

**Indexes:** `Status`, `AddedAt`, `WatchFolderId`, `(ExternalId, SourcePluginId)`, `DocumentId`.

The `(50)` and `(500)` lengths come from `[MaxLength]` attributes; SQLite does not enforce them.

---

## Automation, Sync and Integrations

### WorkflowEntity
**Table:** `workflows`

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Name` | TEXT | req | | Workflow name |
| `Description` | TEXT | null | | Description |
| `Icon` | TEXT | null | | Icon glyph |
| `Category` | TEXT | req | `'Custom'` | Category label |
| `IsBuiltIn` | INTEGER | req | | Seeded template |
| `IsEnabled` | INTEGER | req | | Shown in the workflow list |
| `CreatedAt` | TEXT | req | | Creation time |
| `UpdatedAt` | TEXT | req | | Last change |
| `RunCount` | INTEGER | req | | Runs so far |

**Indexes:** `Category`, `IsBuiltIn`, `IsEnabled`.

---

### WorkflowStepEntity
**Table:** `workflow_steps`

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `WorkflowId` | INTEGER | req | | FK `workflows.Id` (cascade) |
| `StepOrder` | INTEGER | req | | Execution order |
| `Name` | TEXT | req | | Step name |
| `StepType` | TEXT | req | `'AiPrompt'` | `AiPrompt`, `DocumentLookup`, `TextTransform`, `ConditionalBranch` or `OutputFormat` |
| `PromptTemplate` | TEXT | req | | Template with `{{input}}` and `{{previous_output}}` placeholders |
| `ModelOverride` | TEXT | null | | Per-step model |
| `TemperatureOverride` | REAL | null | | Per-step temperature |
| `MaxTokensOverride` | INTEGER | null | | Per-step token limit |
| `ConfigJson` | TEXT | null | | Step settings as JSON (for example `{"transform":"uppercase"}`) |

**Index:** `(WorkflowId, StepOrder)`.

---

### WorkflowRunEntity
**Table:** `workflow_runs`

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `WorkflowId` | INTEGER | req | | FK `workflows.Id` (cascade) |
| `Status` | TEXT | req | `'pending'` | `pending`, `running`, `completed`, `failed` or `cancelled` |
| `InitialInput` | TEXT | null | | Input text |
| `FinalOutput` | TEXT | null | | Output of the last step |
| `ErrorMessage` | TEXT | null | | Failure reason |
| `StartedAt` | TEXT | req | | Start time |
| `CompletedAt` | TEXT | null | | End time |
| `StepsCompleted` | INTEGER | req | | Steps finished |
| `TotalSteps` | INTEGER | req | | Steps in the workflow |
| `StepOutputsJson` | TEXT | null | | JSON array of step results |
| `TotalTokensUsed` | INTEGER | req | | Tokens used by the run |

**Indexes:** `Status`, `StartedAt`, `WorkflowId`.

At startup, runs left `pending` or `running` by a previous session are marked `failed` with the
message "Interrupted: Agent-X closed before this run finished."

---

### SyncLogEntity
**Table:** `sync_logs`
**Purpose:** One Collaborative Sync pass.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `SyncedAt` | TEXT | req | When the pass ended |
| `Direction` | TEXT | req | `export` or `import` |
| `ChangesApplied` | INTEGER | req | Changes applied |
| `ConflictsDetected` | INTEGER | req | Conflicts found |
| `ConflictsResolved` | INTEGER | req | Conflicts resolved |
| `DurationMs` | REAL | req | Duration |
| `ErrorMessage` | TEXT | null | Failure reason |
| `IsSuccess` | INTEGER | req | The pass completed |

**Indexes:** `SyncedAt`, `Direction`, `IsSuccess`.

---

### PluginEntity
**Table:** `plugins`
**Purpose:** An installed plugin package.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `PluginId` | TEXT | req | | Manifest `id`, unique |
| `Name` | TEXT | req | | Manifest `name` |
| `Version` | TEXT | req | | Manifest `version` |
| `Author` | TEXT | req | | Manifest `author` |
| `Description` | TEXT | req | | Manifest `description` |
| `PluginType` | TEXT | req | `'Custom'` | `PluginType` enum member name |
| `InstallPath` | TEXT | req | | `%LocalAppData%\AgentX\Plugins\{PluginId}\` |
| `IsEnabled` | INTEGER | req | `0` | Activated at startup when set |
| `InstalledAt` | TEXT | req | | Install time |
| `LastActivatedAt` | TEXT | null | | Last activation |
| `SettingsJson` | TEXT | null | | Plugin settings |
| `ReadmeContent` | TEXT | null | | README from the package |

**Indexes:** `PluginId` unique, `Name`, `PluginType`, `IsEnabled`, `InstalledAt`.

---

### OAuthCredentialEntity
**Table:** `oauth_credentials`
**Purpose:** Tokens for a connected Google or Microsoft account.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `ProviderId` | TEXT(50) | req | `google` or `microsoft`, unique |
| `AccessToken` | TEXT | req | DPAPI-encrypted access token |
| `RefreshToken` | TEXT | req | DPAPI-encrypted refresh token (empty when the provider issued none) |
| `TokenExpiry` | TEXT | req | Access token expiry |
| `Scopes` | TEXT(500) | req | Granted scopes |
| `UserId` | TEXT | req | Provider user id |
| `CreatedAt` | TEXT | req | First stored |
| `UpdatedAt` | TEXT | req | Last refresh |

**Index:** `ProviderId` unique.

---

## System and Configuration

### UserSettingsEntity
**Table:** `user_settings`
**Purpose:** A key-value store in the database. Application settings are not kept here: they are in
`settings.json` (see [Data outside the database](#data-outside-the-database)).

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Key` | TEXT | req | | Key, unique |
| `Value` | TEXT | req | | Value |
| `ValueType` | TEXT | req | `'string'` | Value type label |
| `UpdatedAt` | TEXT | req | | Last change |

**Index:** `Key` unique.

Keys in use: `feature_flag:<name>` (feature flag overrides, `FeatureFlagService`), and
`SyncConfiguration`, `SyncDeviceId` and `SyncState` (Collaborative Sync, `SyncService`).

---

### WorkspaceProfileEntity
**Table:** `workspace_profiles`
**Purpose:** A saved workspace profile. Its values are stored only: selecting a profile does not
switch models or collections, and nothing loads the default profile at start.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Name` | TEXT | req | | Profile name |
| `Description` | TEXT | null | | Description |
| `ActiveModelId` | TEXT | null | | Model id saved with the profile |
| `ActiveCollectionIds` | TEXT | null | | Comma-separated collection ids |
| `CustomSettings` | TEXT | null | | Free-form text |
| `IsDefault` | INTEGER | req | `0` | Marked as default |
| `CreatedAt` | TEXT | req | | Creation time |
| `UpdatedAt` | TEXT | req | | Last change |

**Indexes:** `IsDefault`, `CreatedAt`.

---

### BackupEntity
**Table:** `backups`
**Purpose:** The list of backup archives (`.agentxbak` files).

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `FileName` | TEXT | req | | Archive name, for example `agentx-backup-2026-03-07-120000.agentxbak` |
| `FilePath` | TEXT | req | | Archive path |
| `BackupType` | TEXT | req | `'manual'` | `manual` or `scheduled` |
| `SizeMB` | REAL | req | | Archive size |
| `CreatedAt` | TEXT | req | | Creation time |
| `Notes` | TEXT | null | | Notes |
| `IsValid` | INTEGER | req | `1` | Passed validation after creation |

**Indexes:** `CreatedAt`, `BackupType`, `IsValid`.

---

### DigestReportEntity
**Table:** `digest_reports`
**Purpose:** A generated Weekly Digest.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `GeneratedAt` | TEXT | req | Generation time |
| `PeriodStart` | TEXT | req | Start of the period |
| `PeriodEnd` | TEXT | req | End of the period |
| `NewDocumentsCount` | INTEGER | req | Documents imported in the period |
| `NewConversationsCount` | INTEGER | req | Conversations started in the period |
| `TotalSearches` | INTEGER | req | Searches in the period |
| `TotalTokensUsed` | INTEGER | req | Tokens used in the period |
| `StorageDeltaBytes` | INTEGER | req | Change in stored bytes |
| `TopSearchesJson` | TEXT | null | JSON array of top searches |
| `TopCollectionsJson` | TEXT | null | JSON array of top collections |
| `FileTypeBreakdownJson` | TEXT | null | JSON array of file types |
| `HighlightsJson` | TEXT | null | JSON array of conversation highlights |
| `IsRead` | INTEGER | req | The report was opened |

**Indexes:** `GeneratedAt`, `IsRead`.

---

## Temporal Identity

These five tables back Past Self, Draft As Me and the Dashboard's belief card. The entities are
defined in `src/AgentX.Core/Services/TemporalIdentity/Models/TemporalIdentityModels.cs`.

### TemporalBeliefEntity
**Table:** `temporal_beliefs`
**Purpose:** The user's recorded stance on one topic.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Topic` | TEXT | req | | Topic, unique; the Past Self lookup matches it exactly first, then case-insensitively (SQLite `NOCASE`) |
| `SentimentScore` | REAL | req | | -1 to 1 |
| `ConfidenceLevel` | REAL | req | | 0 to 1 |
| `CurrentStance` | TEXT | req | | Current stance |
| `EvidenceJson` | TEXT | req | `'[]'` | JSON array of evidence |
| `HasEvolved` | INTEGER | req | | The stance changed |
| `PreviousStance` | TEXT | null | | Stance before the change |
| `StanceChangedAt` | TEXT | null | | When it changed |
| `FirstDetectedAt` | TEXT | req | | First recorded |
| `LastObservedAt` | TEXT | req | | Last recorded |
| `CreatedAt` | TEXT | req | | Row creation |
| `UpdatedAt` | TEXT | req | | Last change |

**Indexes:** `Topic` unique, `LastObservedAt`, `HasEvolved`.

---

### InsightMomentEntity
**Table:** `insight_moments`
**Purpose:** An insight taken from a message or an annotation.

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `Topic` | TEXT | req | | Topic |
| `InsightText` | TEXT | req | | The insight |
| `SignificanceScore` | REAL | req | | Significance |
| `SourceType` | INTEGER | req | | `InsightSource` enum (`ConversationMessage`, `DocumentAnnotation`, `SearchBreakthrough`, `WorkflowSuccess`, `UserExplicitSave`) |
| `SourceId` | INTEGER | null | | Id of the source row (no FK) |
| `CapturedAt` | TEXT | req | | Capture time |
| `HasBeenResurfaced` | INTEGER | req | | Shown to the user again |
| `LastResurfacedAt` | TEXT | null | | Last resurfaced |
| `ResurfaceCount` | INTEGER | req | | Times resurfaced |
| `RelatedTopicsJson` | TEXT | req | `'[]'` | JSON array of topics |
| `CreatedAt` | TEXT | req | | Row creation |
| `UpdatedAt` | TEXT | req | | Last change |

**Indexes:** `Topic`, `SignificanceScore`, `CapturedAt`, `HasBeenResurfaced`.

The `AddTemporalIdentity` migration also created a nullable `ContextJson` column that the model
does not map.

---

### EngagementMetricsEntity
**Table:** `engagement_metrics`
**Purpose:** Time spent with one item (a conversation on screen in chat, a document open in the
vault preview).

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `TargetType` | INTEGER | req | | `EngagementTargetType` enum (`Document`, `Conversation`, `Annotation`, `WorkflowRun`, `WebClip`) |
| `TargetId` | INTEGER | req | | Id of the item |
| `FirstEngagedAt` | TEXT | req | | First view |
| `LastEngagedAt` | TEXT | req | | Latest view |
| `TotalSecondsSpent` | INTEGER | req | | Accumulated seconds |
| `RevisitCount` | INTEGER | req | | Returns to the item |
| `Depth` | INTEGER | req | | `EngagementDepth` enum (`Skimmed`, `Read`, `Engaged`, `Deep`, `Core`) |
| `SentimentShifted` | INTEGER | req | | Sentiment changed |
| `CurrentSentiment` | REAL | req | | Current sentiment |
| `TopicsJson` | TEXT | req | `'[]'` | JSON array of topics |
| `CreatedAt` | TEXT | req | | Row creation |
| `UpdatedAt` | TEXT | req | | Last change |

**Indexes:** `(TargetType, TargetId)` unique, `LastEngagedAt`, `Depth`.

The `AddTemporalIdentity` migration declared `TargetType` and `Depth` as `TEXT`; the model writes
the enum's number into them.

---

### BeliefConflictEntity
**Table:** `belief_conflicts`
**Purpose:** A recorded change of stance ("then X, now Y").

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `Id` | INTEGER | req | Primary key |
| `BeliefId` | INTEGER | req | FK `temporal_beliefs.Id` (cascade) |
| `Topic` | TEXT | req | Copy of the belief's topic |
| `PreviousStance` | TEXT | req | Earlier stance |
| `CurrentStance` | TEXT | req | Newer stance |
| `PreviousStancePeriod` | TEXT | req | When the earlier stance held |
| `StanceChangedAt` | TEXT | req | When it changed |
| `ConflictMagnitude` | REAL | req | Size of the change |
| `DetectedAt` | TEXT | req | Detection time |
| `HasBeenAcknowledged` | INTEGER | req | Acknowledged by the user |
| `AcknowledgedAt` | TEXT | null | Acknowledgement time |
| `CreatedAt` | TEXT | req | Row creation |
| `UpdatedAt` | TEXT | req | Last change |

**Indexes:** `BeliefId`, `Topic`, `DetectedAt`, `HasBeenAcknowledged`, `ConflictMagnitude`.

---

### VoiceProfileEntity
**Table:** `voice_profiles`
**Purpose:** The writing-style profile learned from the user's chat messages (a single row).

| Column | Type | Null | Default | Notes |
|--------|------|------|---------|-------|
| `Id` | INTEGER | req | | Primary key |
| `FirstSampleAt` | TEXT | req | | First sample |
| `LastSampleAt` | TEXT | req | | Latest sample |
| `SampleCount` | INTEGER | req | | Messages sampled |
| `AvgSentenceLength` | REAL | req | | Words per sentence |
| `AvgParagraphLength` | REAL | req | | Sentences per paragraph |
| `FormalityScore` | REAL | req | | 0 (casual) to 1 (formal) |
| `CharacteristicPhrasesJson` | TEXT | req | `'[]'` | JSON array |
| `SentencePatternsJson` | TEXT | req | `'[]'` | JSON array |
| `BookendsJson` | TEXT | req | `'{}'` | JSON object |
| `StylisticTraitsJson` | TEXT | req | `'{}'` | JSON object |
| `PronounPatterns` | TEXT | req | `''` | Pronoun usage |
| `CreatedAt` | TEXT | req | | Row creation |
| `UpdatedAt` | TEXT | req | | Last change |

---

## Tables Outside the EF Model

These are created with raw SQL and are not part of `AgentXDbContext`.

### `fts_chunks` (FTS5)

Created by `KeywordSearchService.InitializeFtsAsync()` at startup, after the migration gate:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
    content,
    document_id UNINDEXED,
    chunk_id UNINDEXED,
    file_name UNINDEXED,
    file_path UNINDEXED,
    file_type UNINDEXED,
    page_number UNINDEXED,
    chunk_index UNINDEXED,
    tokenize='porter unicode61'
);
```

Only `content` is indexed. The indexer replaces a document's rows (delete, then insert) inside
one transaction each time it indexes the document, and deleting a document removes its rows.
Searches use `MATCH` and order by FTS5's `rank` (BM25). FTS5 keeps its own shadow tables
(`fts_chunks_data`, `fts_chunks_idx` and so on) next to it.

### `vec_embeddings`

Created by the vector store (`SqliteVecStore` or `HnswVectorStore`) when it initializes:

```sql
CREATE TABLE IF NOT EXISTS vec_embeddings (
    chunk_id  INTEGER PRIMARY KEY,
    embedding BLOB NOT NULL,
    magnitude REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_vec_chunk ON vec_embeddings(chunk_id);
```

`chunk_id` is `document_chunks.Id`. `embedding` holds the float32 vector (4 bytes per
dimension); `magnitude` is its precomputed L2 norm for cosine similarity. The vector store keeps
its own connection to `agentx.db`, opened through `IEncryptedConnectionFactory` so the database
key applies. `HnswVectorStore` also keeps an in-memory HNSW index; see
[Data outside the database](#data-outside-the-database) for its files.

### `__EFMigrationsHistory`

EF Core's list of applied migrations (`MigrationId`, `ProductVersion`). `MigrationRunner` creates it
itself when it adopts a database that has none, and stamps rows with product version `8.0.11`.

---

## Migrations

The migrations live in `src/AgentX.Core/Data/Migrations/`; the model snapshot is
`AgentXDbContextModelSnapshot.cs`.

| Migration | What it does |
|-----------|--------------|
| `20260417011607_InitialBaseline` | Creates the 28 baseline tables: `annotations`, `backups`, `collections`, `conversation_tags`, `conversations`, `digest_reports`, `document_chunks`, `document_collections`, `document_tags`, `documents`, `feedback`, `inbox_items`, `indexing_jobs`, `licenses`, `memories`, `messages`, `oauth_credentials`, `plugins`, `search_history`, `sync_logs`, `system_prompts`, `tags`, `user_settings`, `watch_folders`, `workflow_runs`, `workflow_steps`, `workflows`, `workspace_profiles` |
| `20260418013814_AddEncryptionColumns` | Adds four encryption columns to `user_settings` |
| `20260418041030_RemoveEncryptionColumns` | Drops them again: encryption state moved to `encryption.info.json` outside the database |
| `20260422120000_AddSemanticMemoryColumns` | Adds `Embedding`, `LinkedMemoryId`, `DecayRate`, `Confidence` and `Tags` to `memories` |
| `20260422153000_AddConversationSummaryPersistence` | Creates `conversation_summary_snapshots` and `conversation_summary_states` |
| `20260423093000_AddMessageRecallEmbeddings` | Adds `Embedding`, `EmbeddedAt` and `EmbeddingModel` to `messages` |
| `20260423153000_AddConversationThemeClustering` | Adds embedding columns to `conversation_summary_snapshots`; creates `conversation_theme_clusters` and `conversation_theme_memberships` |
| `20260423170000_AddConversationThemeDailyMetrics` | Creates `conversation_theme_daily_metrics` |
| `20260430000000_AddTemporalIdentity` | Creates `temporal_beliefs`, `insight_moments`, `engagement_metrics`, `belief_conflicts` and `voice_profiles` |
| `20260503000000_AddEmbeddingModelVersioning` | Adds `EmbeddingModelVersion`, `EmbeddingDimensions` and `EmbeddedAt` to `document_chunks` and `memories`, and `EmbeddingDimensions` to `messages` |
| `20260528120000_DropLicensesTable` | Drops `licenses`: Agent-X has no license tiers |

Only the first three migrations have `.Designer.cs` files; the later ones carry their
`[DbContext]` and `[Migration]` attributes in the migration file itself.

### Adding a migration

The repository pins `dotnet-ef` 8.0.11 in `.config/dotnet-tools.json`. From the repository root:

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> `
  --project src/AgentX.Core `
  --startup-project src/AgentX.Core `
  --output-dir Data/Migrations
```

`AgentXDbContextFactory` (the design-time factory) points EF at a throwaway `agentx.design.db`,
which is git-ignored and never encrypted. The next launch applies the new migration through
`MigrationRunner`. See [docs/DEVELOPER-GUIDE.md](docs/DEVELOPER-GUIDE.md) for the full workflow.

---

## Startup Repairs

`MigrationRunner.RunAsync()` runs on every launch, awaited, before anything else reads data (see
the startup sequence in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)). One run at a time holds its
lock. In order:

1. **Prior state.** The runner decides whether a database already existed from its schema (an
   `__EFMigrationsHistory` table or any application table), not from the file: the key-apply step
   before it creates an empty file on a fresh install.
2. **Baseline adoption** (application tables but no history table, from builds that used
   `EnsureCreated`). The runner creates `__EFMigrationsHistory`, makes sure all 28 baseline tables
   exist (a missing one is created from `InitialBaseline`'s own operations through EF's SQL
   generator, then brought forward with the later migrations' column and index additions), and
   stamps `InitialBaseline`. It then stamps each later migration whose schema is already present,
   so EF does not replay it. If a baseline table is still missing after the repair it throws
   `BaselineSchemaIncompleteException` instead of stamping.
3. **Stamped-baseline repair** (a history table that stamps `InitialBaseline` while baseline tables
   are missing). The missing tables are recreated the same way and brought forward through the
   migrations already stamped as applied.
4. **Reconciliation.** A history row with the old placeholder id
   `20260430XXXXXX_AddTemporalIdentity` is renamed to the real id, and
   `AddSemanticMemoryColumns` is stamped when the `memories` table already has its columns.
5. **Pending migrations** are applied with `MigrateAsync()`.
6. **Idempotent schema repairs**, on every run:
   - `plugins`, `sync_logs`, `workflows`, `workflow_runs` and `workflow_steps` with their indexes
     are created if missing;
   - **Temporal Identity columns.** The `AddTemporalIdentity` migration created its tables without
     columns the entities map, so every write failed on a migrated database. The runner adds each
     missing column (with a default, as SQLite requires for `ADD COLUMN`) to the tables that exist:
     `CreatedAt` on all five tables; `HasBeenResurfaced`, `LastResurfacedAt` and `ResurfaceCount`
     on `insight_moments`; `SentimentShifted` and `CurrentSentiment` on `engagement_metrics`;
     `Topic` and `UpdatedAt` on `belief_conflicts`; `AvgParagraphLength` and `PronounPatterns` on
     `voice_profiles`;
   - a compatibility schema: `inbox_items` and `belief_conflicts` (with their indexes) are created
     if missing, and `belief_conflicts` gets `BeliefId`, `PreviousStancePeriod` and
     `StanceChangedAt` if missing;
   - `conversations` gets `FolderName`, `ParentConversationId`, `BranchPointMessageId` and
     `BranchLabel` if missing, and the `IX_conversations_ParentConversationId` index;
   - four indexes the model declares but no migration created:
     `IX_insight_moments_HasBeenResurfaced`, `IX_engagement_metrics_Depth`,
     `IX_memories_LastUsedAt` and `IX_memories_CreatedAt`.

The result (`MigrationResult`) reports whether the database was created, the migrations applied or
adopted, the migrations already applied, and the database path. If the runner throws, startup
enters a recovery state: it shows a dialog (naming missing tables when there are any), starts no
data-backed feature, and exits.

---

## Access From Code

One `AgentXDbContext` instance is registered as a singleton and shared by the UI thread and
background work (the indexing loop, the local REST API, status polling, scheduled backups and
sync, connector timers). An EF Core context is not thread-safe, so this one serializes itself:

- **`SerializingConcurrencyDetector`** replaces EF's concurrency detector: an overlapping operation
  waits for the one in flight instead of throwing "A second operation was started on this
  context instance". The gate is re-entrant within one async flow.
- **`SerializingQueryCompiler`** holds the same gate for whole scalar query executions and while
  query enumerators are disposed.
- **`SaveChanges` and `SaveChangesAsync`** run under the gate. When a save fails, the changes it
  tried to write are discarded (added entities detached, modified and deleted ones reverted), so
  a rejected change is not replayed by every later save.
- **Raw ADO.NET** on `Database.GetDbConnection()` is invisible to EF. It must hold
  `db.EnterDatabaseGate()` for its whole duration, including any transaction:

  ```csharp
  using (db.EnterDatabaseGate())
  {
      // commands, readers and transactions on db.Database.GetDbConnection()
  }
  ```

  `KeywordSearchService`, `DuplicateDetectionService` and `MigrationRunner` do this. Restore and
  encryption hold the gate from closing the connection until it is reopened.

The change tracker is still shared: adding or removing tracked entities on one thread while another
thread saves is not made safe by the gate. Background services such as Temporal Identity therefore
read with `AsNoTracking()`, update with `ExecuteUpdate`, and detach new rows after saving them.

Because every caller waits on the same gate, keep each database section short and do slow work
(embedding, model calls, file I/O) outside it.

---

## Encryption

Encryption is off by default and is turned on from Settings. The database is then encrypted with
SQLCipher 4 through `SQLitePCLRaw.bundle_e_sqlcipher`.

- **Key storage.** New encryptions use `KeyStorageMode.DpapiWrapped`: 32 random bytes, wrapped with
  DPAPI for the current Windows user. A `UserPassphrase` mode (PBKDF2-HMAC-SHA256, 600,000
  iterations, 16-byte salt) is still unlocked for databases encrypted by older builds.
- **Key state lives outside the database**, in `%LocalAppData%\AgentX\encryption.info.json`
  (`version`, `storageMode`, `enabledAt`, `dpapiWrappedKey`, `saltBase64`). Nothing about the key is
  stored inside the encrypted file.
- **Applying the key.** Every production connection is opened through `IEncryptedConnectionFactory`
  (`OpenKeyed(path)` or `ApplyKey(connection)`), which issues `PRAGMA key = "x'<hex>'"` with the raw
  key right after opening, never through the connection string's `Password=` (which would derive a
  different key). At startup, `AgentXDbContext.EnsureKeyApplied()` applies it to the shared
  connection before the migration runner runs.
- **Turning it on.** `DatabaseEncryptionManager` exports the plaintext database with
  `sqlcipher_export` into `agentx.db.enc.tmp`, moves the plaintext file to `agentx.db.plain.bak`,
  installs the encrypted file, verifies it, and writes the marker file last. It runs with the vector
  store suspended and under the database gate.
- **Crash recovery.** `IDatabaseEncryptionMigrator.RecoverIfNeeded` runs first at every start and
  finishes or undoes an interrupted change, so the marker and the file agree.

The design-time factory used by `dotnet ef` is exempt: it writes an unencrypted throwaway database.

---

## Data Outside the Database

Other stores in `%LocalAppData%\AgentX\`. The HNSW index files and the built-in model folder are
placed under `StoragePath`, which defaults to this folder; the speech-to-text model always lives
under `%LocalAppData%\AgentX\Models\Whisper\`.

| Path | Contents |
|------|----------|
| `settings.json` | Application settings (`AppSettings`, camelCase JSON). API keys, the local API token, OAuth client secrets and the backup password are DPAPI-encrypted |
| `encryption.info.json` | Encryption key state (present only when encryption is on) |
| `usage-history.json` | Cost tracking history: 90 days, at most 20,000 records, with the totals of older records carried forward |
| `hnsw-index.bin`, `hnsw-index.json`, `hnsw-stale-ids.json` | Persisted HNSW index; not written when the database is encrypted (the index is then rebuilt from the database at start) |
| `Models\` | Built-in GGUF model (`llama-3.2-3b-instruct-q4_k_m.gguf` by default); `Models\Whisper\ggml-base.bin` is the speech-to-text model installed from the Model Manager page |
| `Plugins\{PluginId}\` | Installed plugins; each has a `data\` folder. The built-in connectors keep `calendar-sync-settings.json`, `calendar-delta-tokens.json`, `email-sync-settings.json` and `email-delta-tokens.json` in `Plugins\com.agentx.calendar\data\` and `Plugins\com.agentx.email\data\` |
| `Clips\` | Pages clipped by the browser extension |
| `Inbox\External\`, `Inbox\Accepted\` | Content files of connector items, and copies of accepted inbox items |
| `Logs\` | Serilog log files (`agentx-yyyyMMdd.log`, 7 days kept) |

---

**Maintained with the code:** when an entity, its configuration or a migration changes, update the
matching section here.
