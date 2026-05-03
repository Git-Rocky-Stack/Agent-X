# Agent-X Database Schema Documentation

## Overview

Agent-X uses **SQLite with SQLCipher encryption** for local data storage, managed by **Entity Framework Core 8.0**. The database is stored at:

```
%LocalAppData%\AgentX\agentx.db
```

All tables support **cascade delete** relationships where appropriate, and key columns are indexed for query performance.

---

## Entity Relationship Diagram

```
┌─────────────────────┐
│  Conversations      │
│  ─────────────────  │
│  Id (PK)            │───┬──► Messages (1:N, cascade)
│  Title              │   │
│  ModelId            │   ├──► SummarySnapshots (1:N, cascade)
│  CreatedAt          │   │
│  UpdatedAt          │   ├──► SummaryState (1:1, cascade)
│  ParentId (FK)      │   │
│  IsPinned           │   └──► Tags (N:M via ConversationTags, cascade)
└─────────────────────┘
         │
         │ 1:N
         ▼
┌─────────────────────┐
│  Messages           │
│  ─────────────────  │
│  Id (PK)            │
│  ConversationId (FK)│
│  Role               │
│  Content            │
│  Timestamp          │
│  Embedding          │
└─────────────────────┘

┌─────────────────────┐         ┌─────────────────────┐
│  Documents          │◄────────│  DocumentChunks     │
│  ─────────────────  │  1:N    │  ─────────────────  │
│  Id (PK)            │         │  Id (PK)            │
│  FileName           │         │  DocumentId (FK)    │
│  FilePath           │         │  ChunkIndex         │
│  FileType           │         │  Content            │
│  ContentHash        │         │  VectorRowId        │
│  IndexingStatus     │         └─────────────────────┘
└─────────────────────┘
         │
         ├─► Collections (N:M via DocumentCollections)
         ├─► Tags (N:M via DocumentTags)
         └─► Annotations (1:N)

┌─────────────────────┐         ┌─────────────────────┐
│  Collections        │◄────────│  WatchFolders       │
│  ─────────────────  │  1:N    │  ─────────────────  │
│  Id (PK)            │         │  Id (PK)            │
│  Name               │         │  FolderPath         │
│  ParentId (FK)      │         │  TargetCollectionId │
└─────────────────────┘         └─────────────────────┘
         │
         └─► Child Collections (self-referencing 1:N)

┌─────────────────────┐         ┌─────────────────────┐
│  Tags               │◄────────│  DocumentTags       │
│  ─────────────────  │  1:N    │  ─────────────────  │
│  Id (PK)            │         │  DocumentId (FK)    │
│  Name               │         │  TagId (FK)         │
│  CreatedAt          │         │  Confidence         │
└─────────────────────┘         │  AssignedAt         │
         │                      └─────────────────────┘
         └─► ConversationTags
```

---

## Core Entities

### ConversationEntity
**Table:** `conversations`  
**Purpose:** Represents a chat conversation with branching support.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Title` | `string` | NOT NULL | Conversation title |
| `ModelId` | `string` | NOT NULL | AI model used |
| `CreatedAt` | `datetime` | NOT NULL, Indexed | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL, Indexed | Last update timestamp |
| `IsPinned` | `bool` | NOT NULL, Indexed | Pin status |
| `ParentConversationId` | `int?` | FK, Indexed | Parent conversation (for branching) |
| `BranchPointMessageId` | `long?` | FK | Message where branch was created |

**Relationships:**
- `Messages` → One-to-Many, Cascade Delete
- `SummarySnapshots` → One-to-Many, Cascade Delete
- `SummaryState` → One-to-One, Cascade Delete
- `Branches` → Self-referencing One-to-Many
- `ThemeMembership` → One-to-One, Cascade Delete
- `Tags` → Many-to-Many via `ConversationTagEntity`

**Indexes:**
- `CreatedAt` - For chronological queries
- `UpdatedAt` - For recent conversations
- `IsPinned` - For pinned-only queries
- `ParentConversationId` - For branch traversal

---

### MessageEntity
**Table:** `messages`  
**Purpose:** Individual messages within a conversation.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `long` | PK, Auto-increment | Unique identifier |
| `ConversationId` | `int` | NOT NULL, FK | Parent conversation |
| `SortOrder` | `int` | NOT NULL | Message sequence |
| `Role` | `string` | NOT NULL | "user", "assistant", "system", "tool" |
| `Content` | `string` | NOT NULL | Message text |
| `Timestamp` | `datetime` | NOT NULL | When message was created |
| `Embedding` | `float[]` | NULL | Vector embedding for semantic search |
| `EmbeddingModel` | `string` | NULL | Model used for embedding |
| `EmbeddedAt` | `datetime?` | NULL, Indexed | When embedding was generated |

**Relationships:**
- `Conversation` → Many-to-One, Cascade Delete

**Indexes:**
- `(ConversationId, SortOrder)` - Composite for ordering
- `EmbeddedAt` - For embedding queries

---

### DocumentEntity
**Table:** `documents`  
**Purpose:** Represents an indexed document.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `FileName` | `string` | NOT NULL, Indexed | File name |
| `FilePath` | `string` | NOT NULL | Full file path |
| `FileType` | `string` | NOT NULL, Indexed | File extension |
| `ContentHash` | `string` | NOT NULL, Indexed | SHA-256 hash (deduplication) |
| `ImportedAt` | `datetime` | NOT NULL, Indexed | Import timestamp |
| `FileModifiedAt` | `datetime` | NOT NULL | File modification time |
| `IndexingStatus` | `string` | NOT NULL, Indexed | "pending", "indexing", "complete", "failed" |
| `FileSizeBytes` | `long` | NULL | File size |
| `Preview` | `string` | NULL | First N characters |
| `ChunkCount` | `int` | NULL | Number of chunks |

**Relationships:**
- `Chunks` → One-to-Many, Cascade Delete
- `DocumentCollections` → One-to-Many, Cascade Delete
- `DocumentTags` → One-to-Many, Cascade Delete
- `Annotations` → One-to-Many, Cascade Delete
- `IndexingJob` → One-to-One (via `IndexingJobEntity.DocumentId`)

**Indexes:**
- `ContentHash` - For deduplication
- `FileType` - For type filtering
- `IndexingStatus` - For pending/failed queries
- `ImportedAt` - For chronological queries
- `FileName` - For name search

---

### DocumentChunkEntity
**Table:** `document_chunks`  
**Purpose:** Split document fragments for vector search.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `long` | PK, Auto-increment | Unique identifier |
| `DocumentId` | `int` | NOT NULL, FK | Parent document |
| `ChunkIndex` | `int` | NOT NULL | Position in document |
| `Content` | `string` | NOT NULL | Chunk text |
| `VectorRowId` | `long?` | NOT NULL, Indexed | HNSW vector store row ID |

**Relationships:**
- `Document` → Many-to-One, Cascade Delete
- `Annotations` → One-to-Many

**Indexes:**
- `(DocumentId, ChunkIndex)` - Composite for ordering
- `VectorRowId` - For vector lookup

---

### CollectionEntity
**Table:** `collections`  
**Purpose:** Organizes documents hierarchically.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Name` | `string` | NOT NULL | Collection name |
| `Description` | `string` | NULL | Collection description |
| `Color` | `string` | NULL | Hex color code |
| `Icon` | `string` | NULL | Icon identifier |
| `CreatedAt` | `datetime` | NOT NULL | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |
| `ParentCollectionId` | `int?` | FK, Indexed | Parent collection (hierarchy) |

**Relationships:**
- `ChildCollections` → Self-referencing One-to-Many
- `ParentCollection` → Self-referencing Many-to-One
- `DocumentCollections` → One-to-Many, Cascade Delete
- `WatchFolders` → One-to-Many

**Indexes:**
- `ParentCollectionId` - For hierarchy traversal

---

### TagEntity
**Table:** `tags`  
**Purpose:** Categorization labels for documents and conversations.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Name` | `string` | NOT NULL, UNIQUE | Tag name |
| `Description` | `string` | NULL | Tag description |
| `Color` | `string` | NULL | Display color |
| `CreatedAt` | `datetime` | NOT NULL | Creation timestamp |

**Relationships:**
- `DocumentTags` → One-to-Many, Cascade Delete
- `ConversationTags` → One-to-Many, Cascade Delete

**Indexes:**
- `Name` - UNIQUE for lookups

---

## Junction Tables (Many-to-Many)

### DocumentCollectionEntity
**Table:** `document_collections`  
**Purpose:** Links documents to collections.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `DocumentId` | `int` | NOT NULL, FK (PK part) | Document reference |
| `CollectionId` | `int` | NOT NULL, FK (PK part) | Collection reference |
| `AddedAt` | `datetime` | NOT NULL | Association timestamp |

**Primary Key:** `(DocumentId, CollectionId)` composite

---

### DocumentTagEntity
**Table:** `document_tags`  
**Purpose:** Links documents to tags.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `DocumentId` | `int` | NOT NULL, FK (PK part) | Document reference |
| `TagId` | `int` | NOT NULL, FK (PK part) | Tag reference |
| `Confidence` | `float` | NOT NULL | Auto-tag confidence (0-1) |
| `AssignedAt` | `datetime` | NOT NULL | Assignment timestamp |

**Primary Key:** `(DocumentId, TagId)` composite

---

### ConversationTagEntity
**Table:** `conversation_tags`  
**Purpose:** Links conversations to tags.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `ConversationId` | `int` | NOT NULL, FK (PK part) | Conversation reference |
| `TagId` | `int` | NOT NULL, FK (PK part) | Tag reference |
| `AssignedAt` | `datetime` | NOT NULL | Assignment timestamp |

**Primary Key:** `(ConversationId, TagId)` composite

---

## Conversation Memory & Summarization

### ConversationSummarySnapshotEntity
**Table:** `conversation_summary_snapshots`  
**Purpose:** Incremental conversation summaries.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `ConversationId` | `int` | NOT NULL, FK | Parent conversation |
| `SnapshotVersion` | `int` | NOT NULL | Incremental version number |
| `SummaryText` | `string` | NOT NULL | Full summary |
| `PreviewText` | `string` | NOT NULL | Short preview |
| `KeyPointsJson` | `string` | NOT NULL | JSON array of key points |
| `CoveredMessageCount` | `int` | NOT NULL | Messages summarized |
| `GeneratedAt` | `datetime` | NOT NULL, Indexed | Generation timestamp |
| `SourceConversationUpdatedAt` | `datetime` | NOT NULL | Conversation state |
| `IsIncremental` | `bool` | NOT NULL | Incremental vs full |
| `Embedding` | `float[]` | NULL | Vector embedding |
| `EmbeddingModel` | `string` | NULL | Embedding model |
| `EmbeddedAt` | `datetime?` | NULL, Indexed | Embedding timestamp |

**Relationships:**
- `Conversation` → Many-to-One, Cascade Delete
- `ThemeMemberships` → One-to-Many

**Indexes:**
- `(ConversationId, SnapshotVersion)` - UNIQUE composite
- `GeneratedAt` - For time-based queries
- `ConversationId` - For conversation lookups
- `EmbeddedAt` - For embedding queries

---

### ConversationSummaryStateEntity
**Table:** `conversation_summary_states`  
**Purpose:** Tracks summarization progress per conversation.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `ConversationId` | `int` | PK, NOT NULL, FK | Reference to conversation |
| `LatestSnapshotId` | `int?` | FK, Indexed | Latest snapshot reference |
| `LatestSnapshotVersion` | `int` | NOT NULL | Current version |
| `LastCoveredMessageCount` | `int` | NOT NULL | Messages in latest summary |
| `PendingMessageCount` | `int` | NOT NULL | Unsummarized messages |
| `IsStale` | `bool` | NOT NULL, Indexed | Needs refresh |
| `LastRefreshedAt` | `datetime?` | NULL, Indexed | Last refresh time |
| `ConsecutiveFailureCount` | `int` | NOT NULL | Failure retry counter |

**Relationships:**
- `Conversation` → One-to-One, Cascade Delete
- `LatestSnapshot` → Many-to-One, No Action

**Indexes:**
- `IsStale` - For stale detection queries
- `LastRefreshedAt` - For refresh scheduling
- `LatestSnapshotId` - For snapshot lookups

---

## Conversation Theme Clustering

### ConversationThemeClusterEntity
**Table:** `conversation_theme_clusters`  
**Purpose:** Materialized conversation topic clusters.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Label` | `string` | NOT NULL | Cluster label |
| `PreviewText` | `string` | NOT NULL | Preview of content |
| `KeyPointsJson` | `string` | NOT NULL | JSON array of points |
| `ConversationCount` | `int` | NOT NULL | Total conversations |
| `ActiveConversationCount7d` | `int` | NOT NULL | Active in 7 days |
| `ActiveConversationCount30d` | `int` | NOT NULL | Active in 30 days |
| `FirstSeenAt` | `datetime` | NOT NULL, Indexed | First occurrence |
| `LastActiveAt` | `datetime` | NOT NULL, Indexed | Last activity |
| `MaterializedAt` | `datetime` | NOT NULL, Indexed | When cluster was created |

**Relationships:**
- `Memberships` → One-to-Many, Cascade Delete
- `DailyMetrics` → One-to-Many, Cascade Delete

---

### ConversationThemeMembershipEntity
**Table:** `conversation_theme_memberships`  
**Purpose:** Links conversations to theme clusters.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `ConversationId` | `int` | PK, NOT NULL, FK | Conversation reference |
| `SnapshotId` | `int` | NOT NULL, FK | Summary snapshot reference |
| `ClusterId` | `int` | NOT NULL, FK | Cluster reference |
| `SimilarityScore` | `float` | NOT NULL | Semantic similarity |
| `AssignedAt` | `datetime` | NOT NULL, Indexed | Assignment timestamp |

**Relationships:**
- `Conversation` → One-to-One, Cascade Delete
- `Snapshot` → Many-to-One, No Action
- `Cluster` → Many-to-One, Cascade Delete

---

## Intelligence Features

### MemoryEntity
**Table:** `memories`  
**Purpose:** Long-term semantic memory with associative links.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Content` | `string` | NOT NULL | Memory content |
| `Category` | `string` | NOT NULL, Indexed | "fact", "insight", "preference" |
| `Importance` | `float` | NOT NULL, Indexed | 0-1 importance score |
| `IsActive` | `bool` | NOT NULL, Indexed | Active status |
| `Embedding` | `float[]` | NULL | Vector embedding |
| `DecayRate` | `float` | NOT NULL | Memory decay per day |
| `Confidence` | `float` | NOT NULL | Confidence score |
| `Tags` | `string` | NULL | Comma-separated tags |
| `LinkedMemoryId` | `int?` | FK, Indexed | Associative link |
| `LastUsedAt` | `datetime?` | NULL, Indexed | Last retrieval |
| `CreatedAt` | `datetime` | NOT NULL, Indexed | Creation timestamp |

**Relationships:**
- `LinkedMemory` → Self-referencing Many-to-One, Restrict

**Indexes:**
- `Category` - For category filtering
- `IsActive` - For active queries
- `Importance` - For importance-based retrieval
- `LinkedMemoryId` - For associative traversal
- `LastUsedAt` - For recency queries
- `CreatedAt` - For chronological queries

---

### DigestReportEntity
**Table:** `digest_reports`  
**Purpose:** Periodic intelligence digests.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `GeneratedAt` | `datetime` | NOT NULL, Indexed | Generation timestamp |
| `PeriodStart` | `datetime` | NOT NULL | Digest period start |
| `PeriodEnd` | `datetime` | NOT NULL | Digest period end |
| `Summary` | `string` | NOT NULL | Digest content |
| `KeyTopicsJson` | `string` | NULL | JSON array of topics |
| `ConversationCount` | `int` | NULL | Conversations analyzed |
| `DocumentCount` | `int` | NULL | Documents imported |
| `IsRead` | `bool` | NOT NULL, Indexed | Read status |

**Indexes:**
- `GeneratedAt` - For chronological queries
- `IsRead` - For unread filtering

---

### KnowledgeGraphEntity
**Table:** `knowledge_graph_nodes` / `knowledge_graph_edges`  
**Purpose:** Entity relationships and knowledge mapping (separate tables, not in main context).

---

## Workflow System

### WorkflowEntity
**Table:** `workflows`  
**Purpose:** Automated workflow definitions.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Name` | `string` | NOT NULL | Workflow name |
| `Description` | `string` | NULL | Description |
| `Category` | `string` | NOT NULL, Indexed | "Custom", "Analysis", "Export" |
| `IsBuiltIn` | `bool` | NOT NULL, Indexed | Built-in vs user-created |
| `IsEnabled` | `bool` | NOT NULL, Indexed | Active status |
| `CreatedAt` | `datetime` | NOT NULL | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

**Relationships:**
- `Steps` → One-to-Many, Cascade Delete
- `Runs` → One-to-Many, Cascade Delete

---

### WorkflowStepEntity
**Table:** `workflow_steps`  
**Purpose:** Individual workflow steps.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `WorkflowId` | `int` | NOT NULL, FK | Parent workflow |
| `Name` | `string` | NOT NULL | Step name |
| `StepOrder` | `int` | NOT NULL | Execution order |
| `StepType` | `string` | NOT NULL | "AiPrompt", "WebSearch", "Export" |
| `PromptTemplate` | `string` | NOT NULL | Template/content |
| `SettingsJson` | `string` | NULL | Step configuration |

**Relationships:**
- `Workflow` → Many-to-One, Cascade Delete

**Indexes:**
- `(WorkflowId, StepOrder)` - Composite for execution order

---

### WorkflowRunEntity
**Table:** `workflow_runs`  
**Purpose:** Workflow execution history.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `WorkflowId` | `int` | NOT NULL, FK | Workflow definition |
| `Status` | `string` | NOT NULL, Indexed | "pending", "running", "completed" |
| `StartedAt` | `datetime` | NOT NULL, Indexed | Start timestamp |
| `CompletedAt` | `datetime?` | NULL | Completion timestamp |
| `ResultJson` | `string` | NULL | Execution result |
| `ErrorMessage` | `string` | NULL | Error details |

**Relationships:**
- `Workflow` → Many-to-One, Cascade Delete

**Indexes:**
- `Status` - For filtering by status
- `StartedAt` - For chronological queries
- `WorkflowId` - For workflow history

---

## System & Configuration

### SystemPromptEntity
**Table:** `system_prompts`  
**Purpose:** Reusable AI system prompts.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Name` | `string` | NOT NULL | Prompt name |
| `Content` | `string` | NOT NULL | Prompt text |
| `Category` | `string` | NOT NULL | "General", "Coding", "Analysis" |
| `CreatedAt` | `datetime` | NOT NULL | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

---

### UserSettingsEntity
**Table:** `user_settings`  
**Purpose:** Key-value application settings.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Key` | `string` | NOT NULL, UNIQUE | Setting key |
| `Value` | `string` | NOT NULL | Setting value |
| `ValueType` | `string` | NOT NULL | "string", "int", "bool", "encrypted" |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

**Common Keys:**
- `AiProvider_DefaultModel` - Default model ID
- `Search_ResultsCount` - Number of search results
- `Theme_AppMode` - "light", "dark", "system"
- `Encryption_Mode` - "none", "dpapi", "passphrase"

---

### WorkspaceProfileEntity
**Table:** `workspace_profiles`  
**Purpose:** User workspace configurations.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Name` | `string` | NOT NULL | Profile name |
| `Description` | `string` | NULL | Profile description |
| `ActiveModelId` | `string` | NULL | Default model |
| `ActiveCollectionIds` | `string` | NULL | JSON array of collection IDs |
| `CustomSettings` | `string` | NULL | JSON settings override |
| `IsDefault` | `bool` | NOT NULL, Indexed | Default profile flag |
| `CreatedAt` | `datetime` | NOT NULL, Indexed | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

**Indexes:**
- `IsDefault` - For default profile lookup
- `CreatedAt` - For chronological queries

---

## Indexing & File Management

### WatchFolderEntity
**Table:** `watch_folders`  
**Purpose:** Auto-import folder monitoring.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `FolderPath` | `string` | NOT NULL, UNIQUE | Monitored path |
| `TargetCollectionId` | `int?` | FK | Destination collection |
| `Recursive` | `bool` | NOT NULL | Include subdirectories |
| `CreatedAt` | `datetime` | NOT NULL | Creation timestamp |

**Relationships:**
- `TargetCollection` → Many-to-One, SetNull

---

### IndexingJobEntity
**Table:** `indexing_jobs`  
**Purpose:** Document indexing queue.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `DocumentId` | `int` | NOT NULL, FK | Target document |
| `Status` | `string` | NOT NULL, Indexed | "queued", "processing", "completed" |
| `QueuedAt` | `datetime` | NOT NULL, Indexed | Queue time |
| `StartedAt` | `datetime?` | NULL | Start time |
| `CompletedAt` | `datetime?` | NULL | Completion time |
| `ErrorMessage` | `string` | NULL | Error details |

**Relationships:**
- `Document` → Many-to-One, Cascade Delete

**Indexes:**
- `Status` - For filtering by status
- `QueuedAt` - For FIFO processing

---

### InboxItemEntity
**Table:** `inbox_items`  
**Purpose:** Smart triage queue for unprocessed files.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `FilePath` | `string` | NOT NULL | Source file path |
| `FileName` | `string` | NOT NULL | File name |
| `FileType` | `string` | NOT NULL | File extension |
| `FileSizeBytes` | `long` | NOT NULL | File size |
| `Status` | `string` | NOT NULL, Indexed | "pending", "approved", "rejected" |
| `AddedAt` | `datetime` | NOT NULL, Indexed | Addition timestamp |
| `Preview` | `string` | NULL | Content preview |
| `SuggestedCollectionId` | `int?` | FK | AI-suggested collection |
| `SuggestedCollectionName` | `string` | NULL | Suggested name |
| `SuggestedTags` | `string` | NULL | Suggested tags |
| `ProcessedAt` | `datetime?` | NULL | Processing timestamp |
| `WatchFolderId` | `int?` | FK, Indexed | Source watch folder |
| `SourceType` | `string` | NULL | Import source |
| `SourceUrl` | `string` | NULL | Web import URL |
| `SourcePluginId` | `string` | NULL | Plugin source |
| `SourceCategory` | `string` | NULL | Source category |
| `ExternalId` | `string` | NULL | External reference |
| `DocumentId` | `int?` | FK, Indexed | Created document |

**Indexes:**
- `Status` - For pending queries
- `AddedAt` - For chronological display
- `WatchFolderId` - For source filtering
- `(ExternalId, SourcePluginId)` - For deduplication
- `DocumentId` - For reverse lookup

---

## Collaboration & Sync

### SyncLogEntity
**Table:** `sync_logs`  
**Purpose:** Synchronization operation history.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `SyncedAt` | `datetime` | NOT NULL, Indexed | Sync timestamp |
| `Direction` | `string` | NOT NULL, Indexed | "push", "pull", "bidirectional" |
| `ChangesApplied` | `int` | NOT NULL | Number of changes |
| `ConflictsDetected` | `int` | NOT NULL | Conflicts found |
| `ConflictsResolved` | `int` | NOT NULL | Conflicts resolved |
| `DurationMs` | `int` | NOT NULL | Sync duration |
| `IsSuccess` | `bool` | NOT NULL, Indexed | Success flag |
| `ErrorMessage` | `string` | NULL | Error details |

**Indexes:**
- `SyncedAt` - For history queries (newest first)
- `Direction` - For filtering by direction
- `IsSuccess` - For failure filtering

---

### OAuthCredentialEntity
**Table:** `oauth_credentials`  
**Purpose:** OAuth token storage (encrypted).

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `ProviderId` | `string` | NOT NULL, UNIQUE | OAuth provider ID |
| `AccessToken` | `string` | NOT NULL | Encrypted access token |
| `RefreshToken` | `string` | NOT NULL | Encrypted refresh token |
| `TokenExpiry` | `datetime` | NOT NULL | Token expiration |
| `Scopes` | `string` | NOT NULL | Granted scopes |
| `UserId` | `string` | NOT NULL | Provider user ID |
| `CreatedAt` | `datetime` | NOT NULL | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

---

## Plugin System

### PluginEntity
**Table:** `plugins`  
**Purpose:** Installed plugin registry.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `PluginId` | `string` | NOT NULL, UNIQUE | Plugin identifier |
| `Name` | `string` | NOT NULL, Indexed | Display name |
| `Version` | `string` | NOT NULL | Semantic version |
| `Author` | `string` | NOT NULL | Plugin author |
| `Description` | `string` | NOT NULL | Plugin description |
| `PluginType` | `string` | NOT NULL, Indexed | "Custom", "DataConnector" |
| `InstallPath` | `string` | NOT NULL | Installation directory |
| `IsEnabled` | `bool` | NOT NULL, Indexed | Enabled status |
| `InstalledAt` | `datetime` | NOT NULL, Indexed | Install timestamp |
| `LastActivatedAt` | `datetime?` | NULL | Last activation |
| `SettingsJson` | `string` | NULL | Plugin settings |
| `ReadmeContent` | `string` | NULL | README text |

**Indexes:**
- `PluginId` - UNIQUE for identity
- `Name` - For name search
- `PluginType` - For type filtering
- `IsEnabled` - For enabled-only queries
- `InstalledAt` - For chronological display

---

## Annotations & Feedback

### AnnotationEntity
**Table:** `annotations`  
**Purpose:** Document highlights and notes.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `DocumentId` | `int` | NOT NULL, FK | Source document |
| `ChunkId` | `long?` | FK, Indexed | Source chunk |
| `StartOffset` | `int` | NOT NULL | Highlight start |
| `EndOffset` | `int` | NOT NULL | Highlight end |
| `HighlightedText` | `string` | NOT NULL | Highlighted content |
| `Color` | `string` | NOT NULL, Indexed | "yellow", "blue", "green" |
| `Note` | `string` | NULL | User note |
| `CreatedAt` | `datetime` | NOT NULL, Indexed | Creation timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

**Relationships:**
- `Document` → Many-to-One, Cascade Delete

**Indexes:**
- `DocumentId` - For document queries
- `ChunkId` - For chunk lookups
- `Color` - For color filtering
- `CreatedAt` - For chronological queries

---

### FeedbackEntity
**Table:** `feedback`  
**Purpose:** User feedback on AI responses.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `MessageId` | `long` | NOT NULL, UNIQUE, FK | Target message |
| `ConversationId` | `int` | NOT NULL, FK | Parent conversation |
| `Rating` | `string` | NOT NULL, Indexed | "positive", "negative", "neutral" |
| `PreferredResponse` | `string` | NULL | What user wanted |
| `FeedbackNote` | `string` | NULL | User comments |
| `Category` | `string` | NULL | Feedback category |
| `CreatedAt` | `datetime` | NOT NULL, Indexed | Submission timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |

**Relationships:**
- `Message` → Many-to-One, Cascade Delete

**Indexes:**
- `MessageId` - UNIQUE for one feedback per message
- `Rating` - For sentiment analysis
- `ConversationId` - For conversation feedback
- `CreatedAt` - For chronological queries

---

## Backup & Audit

### BackupEntity
**Table:** `backups`  
**Purpose:** Backup manifest.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `FileName` | `string` | NOT NULL | Backup file name |
| `FilePath` | `string` | NOT NULL | Full backup path |
| `BackupType` | `string` | NOT NULL, Indexed | "manual", "automatic" |
| `SizeMB` | `float` | NOT NULL | File size |
| `CreatedAt` | `datetime` | NOT NULL, Indexed | Creation timestamp |
| `IsValid` | `bool` | NOT NULL, Indexed | Validation status |

**Indexes:**
- `CreatedAt` - For chronological queries
- `BackupType` - For type filtering
- `IsValid` - For valid backups

---

### SearchHistoryEntity
**Table:** `search_history`  
**Purpose:** User search query history.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Query` | `string` | NOT NULL | Search query |
| `SearchType` | `string` | NOT NULL | "semantic", "keyword", "hybrid" |
| `ResultCount` | `int` | NULL | Number of results |
| `SearchedAt` | `datetime` | NOT NULL, Indexed | Search timestamp |

**Indexes:**
- `SearchedAt` - For history display (newest first)

---

## Temporal Identity (Belief Tracking)

### TemporalBeliefEntity
**Table:** `temporal_beliefs`  
**Purpose:** Tracks evolution of user beliefs/opinions over time.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Topic` | `string` | NOT NULL, Indexed | Belief subject |
| `CurrentStance` | `string` | NOT NULL | Current position |
| `SentimentScore` | `float` | NOT NULL | Sentiment polarity (-1 to 1) |
| `ConfidenceLevel` | `float` | NOT NULL | Confidence (0-1) |
| `EvidenceJson` | `string` | NOT NULL | JSON evidence array |
| `FirstDetectedAt` | `datetime` | NOT NULL, Indexed | First observation |
| `LastObservedAt` | `datetime` | NOT NULL, Indexed | Last update |
| `UpdatedAt` | `datetime` | NOT NULL | Record update |
| `HasEvolved` | `bool` | NOT NULL, Indexed | Stance changed flag |

**Relationships:**
- `Conflicts` → One-to-Many via `BeliefConflictEntity`

**Indexes:**
- `Topic` - For topic-based queries
- `LastObservedAt` - For recency filtering
- `HasEvolved` - For evolved belief filtering

---

### InsightMomentEntity
**Table:** `insight_moments`  
**Purpose:** Captures user insights and breakthrough moments.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier |
| `Topic` | `string` | NOT NULL | Insight subject |
| `InsightText` | `string` | NOT NULL | Insight description |
| `SignificanceScore` | `float` | NOT NULL, Indexed | Impact score (0-1) |
| `CapturedAt` | `datetime` | NOT NULL, Indexed | Capture timestamp |
| `UpdatedAt` | `datetime` | NOT NULL | Last update |
| `RelatedTopicsJson` | `string` | NOT NULL | JSON related topics |
| `HasBeenResurfaced` | `bool` | NOT NULL, Indexed | Shown to user again |

**Indexes:**
- `SignificanceScore` - For importance filtering
- `CapturedAt` - For chronological queries
- `HasBeenResurfaced` - For resurface tracking

---

### VoiceProfileEntity
**Table:** `voice_profiles`  
**Purpose:** Singleton tracking user writing/speaking voice characteristics.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, Auto-increment | Unique identifier (always 1) |
| `FirstSampleAt` | `datetime` | NOT NULL | First voice sample |
| `LastSampleAt` | `datetime` | NOT NULL | Most recent sample |
| `UpdatedAt` | `datetime` | NOT NULL | Last profile update |
| `SampleCount` | `int` | NOT NULL | Number of samples |
| `AvgSentenceLength` | `float` | NOT NULL | Average sentence length |
| `FormalityScore` | `float` | NOT NULL | Formality (0-1) |
| `CharacteristicPhrasesJson` | `string` | NOT NULL | JSON phrases array |
| `SentencePatternsJson` | `string` | NOT NULL | JSON patterns |
| `BookendsJson` | `string` | NOT NULL | JSON opening/closing patterns |
| `StylisticTraitsJson` | `string` | NOT NULL | JSON style traits |
| `PronounPatterns` | `string` | NOT NULL | Pronoun usage pattern |

---

## Database Initialization

### Migration System

Agent-X uses **EF Core Migrations** with a custom runner:

```csharp
public class AgentXDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure all entities
        ConfigureConversation(modelBuilder);
        ConfigureMessage(modelBuilder);
        // ... etc
    }
}
```

**Running Migrations:**
```bash
# Create new migration
dotnet ef migrations add AddNewFeature --project src/AgentX.Core

# Apply migrations
dotnet ef database update --project src/AgentX.Core

# Rollback
dotnet ef database update [previous-migration] --project src/AgentX.Core
```

---

## Encryption

### SQLCipher Integration

The database supports **SQLCipher** for at-rest encryption:

```csharp
// PRAGMA key applied on startup
public void EnsureKeyApplied()
{
    var conn = Database.GetDbConnection();
    if (conn.State == ConnectionState.Closed)
        conn.Open();
    _connectionFactory.ApplyKey((SqliteConnection)conn);
}
```

**Key Storage Modes:**
- `DpapiWrapped`: Key encrypted with Windows DPAPI
- `UserPassphrase`: User-provided passphrase (PBKDF2-derived)

---

## Performance Considerations

### Indexed Columns

Key columns are indexed for query performance:

| Entity | Indexed Columns |
|--------|----------------|
| `Conversations` | `CreatedAt`, `UpdatedAt`, `IsPinned` |
| `Messages` | `(ConversationId, SortOrder)`, `EmbeddedAt` |
| `Documents` | `ContentHash`, `FileType`, `IndexingStatus` |
| `DocumentChunks` | `(DocumentId, ChunkIndex)`, `VectorRowId` |
| `Collections` | `ParentCollectionId` |
| `Tags` | `Name` (UNIQUE) |
| `InboxItems` | `Status`, `AddedAt`, `WatchFolderId` |

### Query Optimization Tips

1. **Use projection** to avoid loading unnecessary columns:
   ```csharp
   await _dbContext.Documents
       .Where(d => d.IndexingStatus == "complete")
       .Select(d => new { d.Id, d.FileName, d.FilePath })
       .ToListAsync();
   ```

2. **Use AsNoTracking** for read-only queries:
   ```csharp
   await _dbContext.Conversations
       .AsNoTracking()
       .OrderByDescending(c => c.UpdatedAt)
       .ToListAsync();
   ```

3. **Batch operations** for bulk inserts:
   ```csharp
   await _dbContext.DocumentChunks.AddRangeAsync(chunks);
   await _dbContext.SaveChangesAsync();
   ```

---

## Schema Version

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2024-01 | Initial schema (conversations, documents, collections) |
| 1.1 | 2024-03 | Added conversation summarization |
| 1.2 | 2024-05 | Added theme clustering |
| 1.3 | 2024-07 | Added temporal identity tracking |
| 1.4 | 2024-09 | Added workflow system |
| 1.5 | 2024-11 | Added plugin system |
| 1.6 | 2025-01 | Added encryption support |

---

**Document Version:** 1.0  
**Last Updated:** 2025-01-03  
**Maintained By:** Agent-X Development Team
