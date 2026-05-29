# IMPLEMENTATION-PLAN.md

# Agent-X: Local-First AI Personal Intelligence Hub
## Comprehensive Implementation Plan

**Product:** Agent-X (Codename) -- Private AI Command Center for Windows
**Version:** 1.0.0
**Developer:** Rocky Elsalaymeh / Strategia
**Tech Stack:** .NET 8.0 / WinUI 3 / MVVM / SQLite / Ollama / LLamaSharp
**Date:** February 25, 2026
**Timeline:** 26 weeks (6 phases)
**Pricing:** Free and open-source (MIT License)

---

> **Historical note (relicensing):** This document is the original implementation plan and
> describes a proprietary, tiered-license model (a `LicenseService`, `LicenseTier`, license
> pages, a Cloudflare license worker, and pricing) that was **subsequently removed**. Agent-X
> is now 100% free and open-source under the MIT License — there are no tiers, activation,
> quotas, or feature gates, and every capability is unconditionally available to every user.
> The license-related sections below are retained only as a historical record of the original
> design; they no longer reflect the shipping product.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Complete Project Structure](#2-complete-project-structure)
3. [Architecture Diagram](#3-architecture-diagram)
4. [Database Schema](#4-database-schema)
5. [Service Interfaces](#5-service-interfaces)
6. [NuGet Package List](#6-nuget-package-list)
7. [Design System Specification](#7-design-system-specification)
8. [Phase 1: Foundation and Shell (Weeks 1-4)](#8-phase-1-foundation--shell-weeks-1-4)
9. [Phase 2: AI Chat Core (Weeks 5-8)](#9-phase-2-ai-chat-core-weeks-5-8)
10. [Phase 3: Knowledge Vault (Weeks 9-14)](#10-phase-3-knowledge-vault-weeks-9-14)
11. [Phase 4: Search and RAG (Weeks 15-18)](#11-phase-4-search--rag-weeks-15-18)
12. [Phase 5: Dashboard and Intelligence (Weeks 19-22)](#12-phase-5-dashboard--intelligence-weeks-19-22)
13. [Phase 6: Polish and Launch (Weeks 23-26)](#13-phase-6-polish--launch-weeks-23-26)
14. [Testing Strategy](#14-testing-strategy)
15. [Risk Register](#15-risk-register)
16. [Distribution Plan](#16-distribution-plan)
17. [Critical Path Analysis](#17-critical-path-analysis)

---

## 1. Architecture Overview

Agent-X follows the exact same proven architecture used in sysmonitor-windows (v2.2.2) and ElementForge (v1.1.0):

- **Three-project solution**: `AgentX.App` (WinUI 3 frontend), `AgentX.Core` (business logic), `AgentX.Tests` (unit tests)
- **MVVM pattern**: CommunityToolkit.Mvvm 8.2.2 with `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection + Hosting (matching sysmonitor-windows pattern)
- **Data layer**: Entity Framework Core 8 + SQLite for metadata; sqlite-vec for vector embeddings
- **Logging**: Serilog with file sink (rolling daily, 7-day retention)
- **Exception handling**: 3-tier global handlers (AppDomain, UnobservedTask, WinUI UnhandledException)
- **Navigation**: NavigationView shell with Frame-based page navigation and Dictionary page mapping
- **Theme**: AMOLED dark theme with brand accent colors

### Layer Separation

| Layer | Project | Responsibility |
|-------|---------|---------------|
| **Presentation** | AgentX.App | XAML Views, ViewModels, Converters, Styles, Navigation |
| **Business Logic** | AgentX.Core | Services, Models, Entities, AI Providers, Document Processing, Vector DB |
| **Testing** | AgentX.Tests | Unit tests for Core services and ViewModels |

### Key Architectural Decisions

1. **AI abstraction via Microsoft.Extensions.AI `IChatClient`**: Enables swapping between Ollama (OllamaSharp) and in-process LLamaSharp without changing any consumer code. Future-proofs for Microsoft Foundry Local NPU support.

2. **Single SQLite database with sqlite-vec extension**: Vector embeddings live in the same database as metadata, avoiding sync complexity. Loaded via `Microsoft.Data.Sqlite` raw ADO.NET for vector operations, while EF Core handles all relational data.

3. **Document processing pipeline**: Modular processors implementing `IDocumentProcessor` interface. Each processor (PDF, DOCX, TXT, images, code) registers independently. The chunking pipeline is separate from extraction, allowing tuning.

4. **Local-first with zero cloud dependency**: No telemetry, no cloud sync, no external API calls except for license validation. All AI inference runs on the user's hardware via Ollama or LLamaSharp.

---

## 2. Complete Project Structure

```
Agent-X/
|-- AgentX.sln
|-- Directory.Build.props
|-- README.md
|-- IMPLEMENTATION-PLAN.md
|-- CLAUDE.md
|-- .gitignore
|-- .editorconfig
|
|-- src/
|   |-- AgentX.App/
|   |   |-- AgentX.App.csproj
|   |   |-- App.xaml
|   |   |-- App.xaml.cs
|   |   |-- MainWindow.xaml
|   |   |-- MainWindow.xaml.cs
|   |   |-- Package.appxmanifest
|   |   |-- app.manifest
|   |   |-- app.ico
|   |   |
|   |   |-- Assets/
|   |   |   |-- AgentX_Logo.png
|   |   |   |-- Header_Main.png
|   |   |   |-- Onboarding/
|   |   |   |   |-- Step1_Welcome.png
|   |   |   |   |-- Step2_Ollama.png
|   |   |   |   |-- Step3_FirstModel.png
|   |   |   |   |-- Step4_Ready.png
|   |   |   |-- StoreLogo/
|   |   |       |-- StoreLogo.png
|   |   |       |-- SplashScreen.png
|   |   |       |-- Square44x44Logo.png
|   |   |       |-- Square150x150Logo.png
|   |   |       |-- Square310x310Logo.png
|   |   |       |-- Wide310x150Logo.png
|   |   |
|   |   |-- Converters/
|   |   |   |-- BoolConverters.cs
|   |   |   |-- StringConverters.cs
|   |   |   |-- NumericConverters.cs
|   |   |   |-- StatusConverters.cs
|   |   |   |-- CollectionConverters.cs
|   |   |
|   |   |-- Controls/
|   |   |   |-- ChatBubble.xaml
|   |   |   |-- ChatBubble.xaml.cs
|   |   |   |-- MarkdownRenderer.xaml
|   |   |   |-- MarkdownRenderer.xaml.cs
|   |   |   |-- TokenStreamingText.xaml
|   |   |   |-- TokenStreamingText.xaml.cs
|   |   |   |-- DocumentCard.xaml
|   |   |   |-- DocumentCard.xaml.cs
|   |   |   |-- SearchResultCard.xaml
|   |   |   |-- SearchResultCard.xaml.cs
|   |   |   |-- ModelStatusBadge.xaml
|   |   |   |-- ModelStatusBadge.xaml.cs
|   |   |   |-- CircularGauge.xaml
|   |   |   |-- CircularGauge.xaml.cs
|   |   |   |-- CommandPalette.xaml
|   |   |   |-- CommandPalette.xaml.cs
|   |   |   |-- DropZone.xaml
|   |   |   |-- DropZone.xaml.cs
|   |   |   |-- CollectionTreeView.xaml
|   |   |   |-- CollectionTreeView.xaml.cs
|   |   |   |-- CitationLink.xaml
|   |   |   |-- CitationLink.xaml.cs
|   |   |
|   |   |-- Helpers/
|   |   |   |-- DispatcherQueueExtensions.cs
|   |   |   |-- NavigationHelper.cs
|   |   |   |-- WindowHelper.cs
|   |   |   |-- ClipboardHelper.cs
|   |   |
|   |   |-- Services/
|   |   |   |-- IDialogService.cs
|   |   |   |-- DialogService.cs
|   |   |   |-- IThemeService.cs
|   |   |   |-- ThemeService.cs
|   |   |   |-- INavigationService.cs
|   |   |   |-- NavigationService.cs
|   |   |   |-- INotificationService.cs
|   |   |   |-- NotificationService.cs
|   |   |   |-- KeyboardShortcutService.cs
|   |   |
|   |   |-- Styles/
|   |   |   |-- Colors.xaml
|   |   |   |-- Typography.xaml
|   |   |   |-- Controls.xaml
|   |   |   |-- Cards.xaml
|   |   |   |-- Buttons.xaml
|   |   |   |-- Chat.xaml
|   |   |   |-- Navigation.xaml
|   |   |
|   |   |-- ViewModels/
|   |   |   |-- DashboardViewModel.cs
|   |   |   |-- ChatViewModel.cs
|   |   |   |-- ConversationListViewModel.cs
|   |   |   |-- ModelManagerViewModel.cs
|   |   |   |-- KnowledgeVaultViewModel.cs
|   |   |   |-- DocumentDetailViewModel.cs
|   |   |   |-- CollectionManagerViewModel.cs
|   |   |   |-- SearchViewModel.cs
|   |   |   |-- AskFilesViewModel.cs
|   |   |   |-- SettingsViewModel.cs
|   |   |   |-- OnboardingViewModel.cs
|   |   |   |-- LicenseViewModel.cs
|   |   |   |-- QuickActionsViewModel.cs
|   |   |   |-- HardwareAdvisorViewModel.cs
|   |   |   |-- SystemPromptLibraryViewModel.cs
|   |   |
|   |   |-- Views/
|   |   |   |-- DashboardPage.xaml
|   |   |   |-- DashboardPage.xaml.cs
|   |   |   |-- ChatPage.xaml
|   |   |   |-- ChatPage.xaml.cs
|   |   |   |-- ConversationListPage.xaml
|   |   |   |-- ConversationListPage.xaml.cs
|   |   |   |-- ModelManagerPage.xaml
|   |   |   |-- ModelManagerPage.xaml.cs
|   |   |   |-- KnowledgeVaultPage.xaml
|   |   |   |-- KnowledgeVaultPage.xaml.cs
|   |   |   |-- DocumentDetailPage.xaml
|   |   |   |-- DocumentDetailPage.xaml.cs
|   |   |   |-- CollectionManagerPage.xaml
|   |   |   |-- CollectionManagerPage.xaml.cs
|   |   |   |-- SearchPage.xaml
|   |   |   |-- SearchPage.xaml.cs
|   |   |   |-- AskFilesPage.xaml
|   |   |   |-- AskFilesPage.xaml.cs
|   |   |   |-- SettingsPage.xaml
|   |   |   |-- SettingsPage.xaml.cs
|   |   |   |-- OnboardingPage.xaml
|   |   |   |-- OnboardingPage.xaml.cs
|   |   |   |-- LicensePage.xaml
|   |   |   |-- LicensePage.xaml.cs
|   |   |   |-- QuickActionsPage.xaml
|   |   |   |-- QuickActionsPage.xaml.cs
|   |   |   |-- HardwareAdvisorPage.xaml
|   |   |   |-- HardwareAdvisorPage.xaml.cs
|   |   |   |-- SystemPromptLibraryPage.xaml
|   |   |   |-- SystemPromptLibraryPage.xaml.cs
|   |   |
|   |   |-- Properties/
|   |       |-- PublishProfiles/
|   |           |-- win-x64.pubxml
|   |           |-- win-x86.pubxml
|   |           |-- win-arm64.pubxml
|   |
|   |-- AgentX.Core/
|       |-- AgentX.Core.csproj
|       |
|       |-- AI/
|       |   |-- IAiProvider.cs
|       |   |-- IAiService.cs
|       |   |-- AiService.cs
|       |   |-- Providers/
|       |   |   |-- OllamaProvider.cs
|       |   |   |-- LLamaSharpProvider.cs
|       |   |-- IModelManager.cs
|       |   |-- ModelManager.cs
|       |   |-- IEmbeddingService.cs
|       |   |-- EmbeddingService.cs
|       |   |-- IHardwareDetector.cs
|       |   |-- HardwareDetector.cs
|       |   |-- Models/
|       |       |-- AiModel.cs
|       |       |-- ModelDownloadProgress.cs
|       |       |-- ChatMessage.cs
|       |       |-- ChatOptions.cs
|       |       |-- EmbeddingResult.cs
|       |       |-- HardwareCapability.cs
|       |
|       |-- Data/
|       |   |-- AgentXDbContext.cs
|       |   |-- Migrations/
|       |   |-- Entities/
|       |   |   |-- ConversationEntity.cs
|       |   |   |-- MessageEntity.cs
|       |   |   |-- DocumentEntity.cs
|       |   |   |-- DocumentChunkEntity.cs
|       |   |   |-- CollectionEntity.cs
|       |   |   |-- DocumentCollectionEntity.cs
|       |   |   |-- TagEntity.cs
|       |   |   |-- DocumentTagEntity.cs
|       |   |   |-- SearchHistoryEntity.cs
|       |   |   |-- SystemPromptEntity.cs
|       |   |   |-- UserSettingsEntity.cs
|       |   |   |-- WatchFolderEntity.cs
|       |   |   |-- IndexingJobEntity.cs
|       |   |   |-- LicenseEntity.cs
|       |   |-- VectorDb/
|       |       |-- IVectorStore.cs
|       |       |-- SqliteVecStore.cs
|       |       |-- VectorSearchResult.cs
|       |
|       |-- Documents/
|       |   |-- IDocumentProcessor.cs
|       |   |-- IDocumentService.cs
|       |   |-- DocumentService.cs
|       |   |-- IChunkingService.cs
|       |   |-- ChunkingService.cs
|       |   |-- Processors/
|       |   |   |-- PdfProcessor.cs
|       |   |   |-- DocxProcessor.cs
|       |   |   |-- TextProcessor.cs
|       |   |   |-- MarkdownProcessor.cs
|       |   |   |-- ImageProcessor.cs
|       |   |   |-- CodeFileProcessor.cs
|       |   |-- Models/
|       |       |-- ProcessedDocument.cs
|       |       |-- DocumentChunk.cs
|       |       |-- DocumentMetadata.cs
|       |       |-- SupportedFileTypes.cs
|       |
|       |-- Search/
|       |   |-- ISemanticSearchService.cs
|       |   |-- SemanticSearchService.cs
|       |   |-- IRagPipeline.cs
|       |   |-- RagPipeline.cs
|       |   |-- ICitationService.cs
|       |   |-- CitationService.cs
|       |   |-- Models/
|       |       |-- SearchQuery.cs
|       |       |-- SearchResult.cs
|       |       |-- RagResponse.cs
|       |       |-- Citation.cs
|       |
|       |-- Services/
|       |   |-- Chat/
|       |   |   |-- IChatService.cs
|       |   |   |-- ChatService.cs
|       |   |   |-- IConversationService.cs
|       |   |   |-- ConversationService.cs
|       |   |   |-- ISystemPromptService.cs
|       |   |   |-- SystemPromptService.cs
|       |   |-- Collections/
|       |   |   |-- ICollectionService.cs
|       |   |   |-- CollectionService.cs
|       |   |-- Indexing/
|       |   |   |-- IIndexingService.cs
|       |   |   |-- IndexingService.cs
|       |   |   |-- IFileWatcherService.cs
|       |   |   |-- FileWatcherService.cs
|       |   |   |-- IIndexingQueueService.cs
|       |   |   |-- IndexingQueueService.cs
|       |   |-- Tagging/
|       |   |   |-- IAutoTagService.cs
|       |   |   |-- AutoTagService.cs
|       |   |-- Intelligence/
|       |   |   |-- IDuplicateDetectionService.cs
|       |   |   |-- DuplicateDetectionService.cs
|       |   |   |-- IOrganizationSuggestionService.cs
|       |   |   |-- OrganizationSuggestionService.cs
|       |   |   |-- ISummaryService.cs
|       |   |   |-- SummaryService.cs
|       |   |-- License/
|       |   |   |-- ILicenseService.cs
|       |   |   |-- LicenseService.cs
|       |   |   |-- LicenseTier.cs
|       |   |-- Settings/
|       |       |-- ISettingsService.cs
|       |       |-- SettingsService.cs
|       |       |-- AppSettings.cs
|       |
|       |-- Helpers/
|           |-- FormatHelper.cs
|           |-- PathHelper.cs
|           |-- HashHelper.cs
|           |-- FileTypeHelper.cs
|
|-- tests/
|   |-- AgentX.Tests/
|       |-- AgentX.Tests.csproj
|       |-- AI/
|       |   |-- AiServiceTests.cs
|       |   |-- EmbeddingServiceTests.cs
|       |   |-- HardwareDetectorTests.cs
|       |-- Documents/
|       |   |-- ChunkingServiceTests.cs
|       |   |-- TextProcessorTests.cs
|       |   |-- MarkdownProcessorTests.cs
|       |   |-- CodeFileProcessorTests.cs
|       |-- Search/
|       |   |-- SemanticSearchServiceTests.cs
|       |   |-- RagPipelineTests.cs
|       |   |-- CitationServiceTests.cs
|       |-- Services/
|       |   |-- ChatServiceTests.cs
|       |   |-- ConversationServiceTests.cs
|       |   |-- CollectionServiceTests.cs
|       |   |-- IndexingServiceTests.cs
|       |   |-- AutoTagServiceTests.cs
|       |   |-- DuplicateDetectionServiceTests.cs
|       |   |-- LicenseServiceTests.cs
|       |   |-- SettingsServiceTests.cs
|       |-- Data/
|       |   |-- SqliteVecStoreTests.cs
|       |   |-- AgentXDbContextTests.cs
|       |-- Helpers/
|           |-- HashHelperTests.cs
|           |-- FormatHelperTests.cs
|           |-- FileTypeHelperTests.cs
|
|-- server/
|   |-- cloudflare-license-worker/
|       |-- worker.js
|       |-- wrangler.toml
|
|-- installer/
|   |-- AgentX.iss
|
|-- Build-Release.ps1
```

---

## 3. Architecture Diagram

```
+==============================================================================+
|                          AGENT-X ARCHITECTURE                                  |
+==============================================================================+

  +---------------------------------------------------------------------------+
  | PRESENTATION LAYER (AgentX.App)                                            |
  |                                                                             |
  |  +-------------------+  +-------------------+  +-------------------+        |
  |  |   Views (XAML)    |  |    ViewModels     |  |   Controls       |        |
  |  |   - Dashboard     |  | - ObservableObject|  | - ChatBubble     |        |
  |  |   - Chat          |  | - [ObservableProperty]| - MarkdownRenderer|     |
  |  |   - Knowledge     |  | - [RelayCommand]  |  | - CommandPalette |        |
  |  |   - Search        |  | - DI Constructor  |  | - DropZone       |        |
  |  |   - Settings      |  |   Injection       |  | - DocumentCard   |        |
  |  +-------------------+  +-------------------+  +-------------------+        |
  |                                                                             |
  |  +-------------------+  +-------------------+  +-------------------+        |
  |  |    Styles/        |  |   Converters/     |  |   Services/      |        |
  |  |  Colors.xaml      |  | BoolConverters    |  | DialogService    |        |
  |  |  Typography.xaml  |  | StringConverters  |  | ThemeService     |        |
  |  |  Cards.xaml       |  | StatusConverters  |  | NavigationService|        |
  |  |  Chat.xaml        |  |                   |  | KeyboardShortcuts|        |
  |  +-------------------+  +-------------------+  +-------------------+        |
  +---------------------------------------------------------------------------+
                                    |
                          DI Container (IHost)
                                    |
  +---------------------------------------------------------------------------+
  | BUSINESS LOGIC LAYER (AgentX.Core)                                         |
  |                                                                             |
  |  +-----------------------------------------------------------------------+ |
  |  | AI LAYER                                                               | |
  |  |                                                                         | |
  |  |  +------------------+      +------------------+                         | |
  |  |  | IAiService       |      | IModelManager    |                         | |
  |  |  | (orchestrator)   |      | - ListModels     |                         | |
  |  |  |                  |      | - DownloadModel   |                         | |
  |  |  +--------+---------+      | - DeleteModel     |                         | |
  |  |           |                +------------------+                         | |
  |  |  +--------v---------+                                                   | |
  |  |  | IChatClient      | <--- Microsoft.Extensions.AI abstraction          | |
  |  |  | (M.E.AI)         |                                                   | |
  |  |  +--------+---------+                                                   | |
  |  |           |                                                             | |
  |  |    +------+------+                                                      | |
  |  |    |             |                                                      | |
  |  |  +-v----------+ +-v-----------+                                         | |
  |  |  | Ollama     | | LLamaSharp  |   (Future: Foundry Local)               | |
  |  |  | Provider   | | Provider    |                                         | |
  |  |  | (OllamaSharp)| (in-process)|                                         | |
  |  |  +------------+ +-------------+                                         | |
  |  +-----------------------------------------------------------------------+ |
  |                                                                             |
  |  +-----------------------------------------------------------------------+ |
  |  | DOCUMENT PROCESSING PIPELINE                                           | |
  |  |                                                                         | |
  |  |  Import -> IDocumentProcessor -> IChunkingService -> IEmbeddingService  | |
  |  |                                                                         | |
  |  |  Processors:                                                            | |
  |  |  +-------+ +-------+ +-------+ +-------+ +-------+ +-------+           | |
  |  |  | PDF   | | DOCX  | | TXT   | | MD    | | Image | | Code  |           | |
  |  |  |PDFsharp| |OpenXml| |.NET IO| |Custom | | OCR   | |Custom |           | |
  |  |  +-------+ +-------+ +-------+ +-------+ +-------+ +-------+           | |
  |  +-----------------------------------------------------------------------+ |
  |                                                                             |
  |  +-----------------------------------------------------------------------+ |
  |  | SEARCH & RAG LAYER                                                     | |
  |  |                                                                         | |
  |  |  Query -> IEmbeddingService -> IVectorStore -> IRagPipeline             | |
  |  |                                    |                                    | |
  |  |                              sqlite-vec                                 | |
  |  |                          (same SQLite DB)                               | |
  |  +-----------------------------------------------------------------------+ |
  |                                                                             |
  |  +-----------------------------------------------------------------------+ |
  |  | SERVICES                                                               | |
  |  |                                                                         | |
  |  |  +-------------+ +-------------+ +-------------+ +--------------+       | |
  |  |  | ChatService | | Collection  | | Indexing     | | License      |       | |
  |  |  |             | | Service     | | Service      | | Service      |       | |
  |  |  +-------------+ +-------------+ +-------------+ +--------------+       | |
  |  |  +-------------+ +-------------+ +-------------+ +--------------+       | |
  |  |  | AutoTag     | | Duplicate   | | Organization| | Settings     |       | |
  |  |  | Service     | | Detection   | | Suggestion  | | Service      |       | |
  |  |  +-------------+ +-------------+ +-------------+ +--------------+       | |
  |  +-----------------------------------------------------------------------+ |
  +---------------------------------------------------------------------------+
                                    |
  +---------------------------------------------------------------------------+
  | DATA LAYER                                                                 |
  |                                                                             |
  |  +---------------------------+  +---------------------------+               |
  |  | EF Core (AgentXDbContext) |  | sqlite-vec (IVectorStore) |               |
  |  | - Conversations           |  | - Embedding vectors       |               |
  |  | - Messages                |  | - Similarity search       |               |
  |  | - Documents               |  | - KNN queries             |               |
  |  | - Collections             |  |                           |               |
  |  | - Tags                    |  +---------------------------+               |
  |  | - Settings                |         |                                    |
  |  | - SearchHistory           |         |                                    |
  |  +------------+--------------+         |                                    |
  |               |                        |                                    |
  |               +-------+--------+-------+                                    |
  |                       |                                                     |
  |                 [agentx.db]                                                 |
  |           %LOCALAPPDATA%/AgentX/                                            |
  +---------------------------------------------------------------------------+
                                    |
  +---------------------------------------------------------------------------+
  | EXTERNAL DEPENDENCIES                                                      |
  |                                                                             |
  |  +------------------+  +------------------+  +------------------+           |
  |  | Ollama (local)   |  | File System      |  | Cloudflare Worker|           |
  |  | http://localhost |  | FileSystemWatcher|  | (license only)   |           |
  |  | :11434           |  | Watch Folders    |  | LemonSqueezy API |           |
  |  +------------------+  +------------------+  +------------------+           |
  +---------------------------------------------------------------------------+
```

---

## 4. Database Schema

### EF Core Entities

All entities live in `AgentX.Core/Data/Entities/` and are configured via fluent API in `AgentXDbContext.OnModelCreating`.

#### ConversationEntity.cs

```csharp
namespace AgentX.Core.Data.Entities;

public class ConversationEntity
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public int MessageCount { get; set; }
    public long TokensUsed { get; set; }

    // Navigation
    public ICollection<MessageEntity> Messages { get; set; } = new List<MessageEntity>();
}
```

#### MessageEntity.cs

```csharp
public class MessageEntity
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public string Role { get; set; } = string.Empty; // "user", "assistant", "system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int TokenCount { get; set; }
    public double? GenerationTimeMs { get; set; }
    public string? ModelId { get; set; }
    public string? CitationsJson { get; set; } // JSON array of Citation objects
    public int SortOrder { get; set; }

    // Navigation
    public ConversationEntity Conversation { get; set; } = null!;
}
```

#### DocumentEntity.cs

```csharp
public class DocumentEntity
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty; // "pdf", "docx", "txt", etc.
    public string? MimeType { get; set; }
    public long FileSizeBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty; // SHA256 for duplicate detection
    public DateTime ImportedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public string IndexingStatus { get; set; } = "pending"; // pending, processing, completed, failed
    public string? IndexingError { get; set; }
    public int ChunkCount { get; set; }
    public int PageCount { get; set; }
    public long WordCount { get; set; }
    public string? Summary { get; set; } // AI-generated summary
    public string? ExtractedTitle { get; set; }
    public string? Language { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? MetadataJson { get; set; } // Additional metadata as JSON

    // Navigation
    public ICollection<DocumentChunkEntity> Chunks { get; set; } = new List<DocumentChunkEntity>();
    public ICollection<DocumentCollectionEntity> DocumentCollections { get; set; } = new List<DocumentCollectionEntity>();
    public ICollection<DocumentTagEntity> DocumentTags { get; set; } = new List<DocumentTagEntity>();
}
```

#### DocumentChunkEntity.cs

```csharp
public class DocumentChunkEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int StartCharOffset { get; set; }
    public int EndCharOffset { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public int TokenCount { get; set; }
    public bool IsEmbedded { get; set; }
    public long? VectorRowId { get; set; } // Foreign key to sqlite-vec virtual table

    // Navigation
    public DocumentEntity Document { get; set; } = null!;
}
```

#### CollectionEntity.cs

```csharp
public class CollectionEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconGlyph { get; set; } // Segoe Fluent Icons glyph
    public string? ColorHex { get; set; }
    public long? ParentCollectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int DocumentCount { get; set; }
    public int SortOrder { get; set; }

    // Navigation
    public CollectionEntity? ParentCollection { get; set; }
    public ICollection<CollectionEntity> ChildCollections { get; set; } = new List<CollectionEntity>();
    public ICollection<DocumentCollectionEntity> DocumentCollections { get; set; } = new List<DocumentCollectionEntity>();
}
```

#### DocumentCollectionEntity.cs (join table)

```csharp
public class DocumentCollectionEntity
{
    public long DocumentId { get; set; }
    public long CollectionId { get; set; }
    public DateTime AddedAt { get; set; }

    public DocumentEntity Document { get; set; } = null!;
    public CollectionEntity Collection { get; set; } = null!;
}
```

#### TagEntity.cs

```csharp
public class TagEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public bool IsAutoGenerated { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<DocumentTagEntity> DocumentTags { get; set; } = new List<DocumentTagEntity>();
}
```

#### DocumentTagEntity.cs (join table)

```csharp
public class DocumentTagEntity
{
    public long DocumentId { get; set; }
    public long TagId { get; set; }
    public double Confidence { get; set; } // 0.0 - 1.0 for auto-generated tags
    public DateTime AssignedAt { get; set; }

    public DocumentEntity Document { get; set; } = null!;
    public TagEntity Tag { get; set; } = null!;
}
```

#### SearchHistoryEntity.cs

```csharp
public class SearchHistoryEntity
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public string SearchType { get; set; } = "semantic"; // "semantic", "keyword", "rag"
    public int ResultCount { get; set; }
    public DateTime SearchedAt { get; set; }
    public bool IsSaved { get; set; }
    public string? CollectionFilter { get; set; } // comma-separated collection IDs
}
```

#### SystemPromptEntity.cs

```csharp
public class SystemPromptEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // General, Writing, Code, Analysis, Creative
    public bool IsBuiltIn { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int UsageCount { get; set; }
}
```

#### UserSettingsEntity.cs

```csharp
public class UserSettingsEntity
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "string"; // string, int, bool, double, json
    public DateTime UpdatedAt { get; set; }
}
```

#### WatchFolderEntity.cs

```csharp
public class WatchFolderEntity
{
    public long Id { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IncludeSubfolders { get; set; }
    public string? FileTypeFilter { get; set; } // e.g., "pdf,docx,txt,md"
    public long? TargetCollectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastScanAt { get; set; }
    public int FilesIndexed { get; set; }

    public CollectionEntity? TargetCollection { get; set; }
}
```

#### IndexingJobEntity.cs

```csharp
public class IndexingJobEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string Status { get; set; } = "queued"; // queued, processing, completed, failed
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChunksProcessed { get; set; }
    public int EmbeddingsGenerated { get; set; }
    public double? ProcessingTimeMs { get; set; }

    public DocumentEntity Document { get; set; } = null!;
}
```

#### LicenseEntity.cs

```csharp
public class LicenseEntity
{
    public long Id { get; set; }
    public string LicenseKey { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public string Tier { get; set; } = "starter"; // starter, professional, ultimate
    public bool IsActivated { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
}
```

### sqlite-vec Virtual Table (raw SQL, not EF Core)

```sql
-- Created via Microsoft.Data.Sqlite, NOT EF Core
-- Loaded via: SELECT load_extension('vec0');

CREATE VIRTUAL TABLE IF NOT EXISTS vec_embeddings USING vec0(
    chunk_id INTEGER PRIMARY KEY,
    embedding FLOAT[384]  -- all-MiniLM-L6-v2 produces 384-dimensional vectors
);
```

### EF Core Index Configuration

```csharp
// In AgentXDbContext.OnModelCreating:

// Conversations
entity.HasIndex(e => e.CreatedAt);
entity.HasIndex(e => e.UpdatedAt);
entity.HasIndex(e => e.IsPinned);

// Messages
entity.HasIndex(e => new { e.ConversationId, e.SortOrder });

// Documents
entity.HasIndex(e => e.ContentHash);
entity.HasIndex(e => e.FileType);
entity.HasIndex(e => e.IndexingStatus);
entity.HasIndex(e => e.ImportedAt);
entity.HasIndex(e => e.FileName);

// DocumentChunks
entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex });
entity.HasIndex(e => e.VectorRowId);

// Collections
entity.HasIndex(e => e.ParentCollectionId);

// Tags
entity.HasIndex(e => e.Name).IsUnique();

// SearchHistory
entity.HasIndex(e => e.SearchedAt);

// UserSettings
entity.HasIndex(e => e.Key).IsUnique();

// WatchFolders
entity.HasIndex(e => e.FolderPath).IsUnique();

// IndexingJobs
entity.HasIndex(e => e.Status);
entity.HasIndex(e => e.QueuedAt);
```

---

## 5. Service Interfaces

Every service below follows the established pattern: interface in `AgentX.Core`, implementation alongside it, registered in DI container via `App.xaml.cs`.

### AI Layer

```csharp
// AgentX.Core/AI/IAiProvider.cs
namespace AgentX.Core.AI;

/// <summary>
/// Abstraction over AI inference providers (Ollama, LLamaSharp).
/// Implements Microsoft.Extensions.AI.IChatClient under the hood.
/// </summary>
public interface IAiProvider : IDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }

    Task<bool> CheckConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default);

    Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default);

    Task<float[]> GenerateEmbeddingAsync(
        string text,
        string modelName,
        CancellationToken ct = default);

    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string modelName,
        CancellationToken ct = default);
}
```

```csharp
// AgentX.Core/AI/IAiService.cs
namespace AgentX.Core.AI;

/// <summary>
/// High-level AI service that orchestrates provider selection
/// and provides the primary interface for all AI operations.
/// </summary>
public interface IAiService : IDisposable
{
    IAiProvider ActiveProvider { get; }
    bool IsConnected { get; }
    string ActiveModelId { get; }

    Task InitializeAsync(CancellationToken ct = default);
    Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default);
    Task SetActiveModelAsync(string modelId, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        ChatOptions? options = null,
        CancellationToken ct = default);

    Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        ChatOptions? options = null,
        CancellationToken ct = default);
}
```

```csharp
// AgentX.Core/AI/IModelManager.cs
namespace AgentX.Core.AI;

public interface IModelManager
{
    Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default);
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
    Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default);
    Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default);
    event EventHandler<AiModel>? ModelListChanged;
}
```

```csharp
// AgentX.Core/AI/IEmbeddingService.cs
namespace AgentX.Core.AI;

public interface IEmbeddingService
{
    string EmbeddingModelName { get; }
    int EmbeddingDimensions { get; } // 384 for all-MiniLM-L6-v2

    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateBatchEmbeddingsAsync(
        IReadOnlyList<string> texts,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
    Task<bool> IsModelAvailableAsync(CancellationToken ct = default);
    Task EnsureModelAvailableAsync(CancellationToken ct = default);
}
```

```csharp
// AgentX.Core/AI/IHardwareDetector.cs
namespace AgentX.Core.AI;

public interface IHardwareDetector
{
    Task<HardwareCapability> DetectCapabilitiesAsync();
    Task<string> GetRecommendedModelAsync(HardwareCapability capabilities);
    Task<string> GetHardwareAdvisoryAsync(HardwareCapability capabilities);
}
```

### Chat Services

```csharp
// AgentX.Core/Services/Chat/IChatService.cs
namespace AgentX.Core.Services.Chat;

public interface IChatService
{
    IAsyncEnumerable<string> SendMessageAsync(
        long conversationId,
        string userMessage,
        CancellationToken ct = default);

    Task<string> SendMessageAndWaitAsync(
        long conversationId,
        string userMessage,
        CancellationToken ct = default);

    Task RegenerateLastResponseAsync(
        long conversationId,
        CancellationToken ct = default);

    Task StopGenerationAsync();
    bool IsGenerating { get; }
    event EventHandler<bool>? GenerationStateChanged;
}
```

```csharp
// AgentX.Core/Services/Chat/IConversationService.cs
namespace AgentX.Core.Services.Chat;

public interface IConversationService
{
    Task<ConversationEntity> CreateConversationAsync(
        string? title = null,
        string? systemPrompt = null,
        string? modelId = null);

    Task<ConversationEntity?> GetConversationAsync(long conversationId);
    Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(bool includeArchived = false);
    Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query);
    Task UpdateConversationTitleAsync(long conversationId, string title);
    Task TogglePinAsync(long conversationId);
    Task ArchiveConversationAsync(long conversationId);
    Task DeleteConversationAsync(long conversationId);
    Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId);
    Task AddMessageAsync(long conversationId, string role, string content, int? tokenCount = null, double? generationTimeMs = null);
    Task<int> GetConversationCountAsync();
    Task<long> GetTotalTokensUsedAsync();
}
```

```csharp
// AgentX.Core/Services/Chat/ISystemPromptService.cs
namespace AgentX.Core.Services.Chat;

public interface ISystemPromptService
{
    Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null);
    Task<SystemPromptEntity?> GetPromptAsync(long id);
    Task<SystemPromptEntity> CreatePromptAsync(string name, string content, string category);
    Task UpdatePromptAsync(long id, string name, string content, string category);
    Task DeletePromptAsync(long id);
    Task ToggleFavoriteAsync(long id);
    Task IncrementUsageAsync(long id);
    Task SeedBuiltInPromptsAsync();
}
```

### Document Services

```csharp
// AgentX.Core/Documents/IDocumentProcessor.cs
namespace AgentX.Core.Documents;

public interface IDocumentProcessor
{
    IReadOnlyList<string> SupportedExtensions { get; }
    Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
}
```

```csharp
// AgentX.Core/Documents/IDocumentService.cs
namespace AgentX.Core.Documents;

public interface IDocumentService
{
    Task<DocumentEntity> ImportFileAsync(string filePath, long? collectionId = null, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(IReadOnlyList<string> filePaths, long? collectionId = null, IProgress<int>? progress = null, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentAsync(long documentId);
    Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(string? fileTypeFilter = null, string? statusFilter = null);
    Task<IReadOnlyList<DocumentEntity>> GetDocumentsByCollectionAsync(long collectionId);
    Task DeleteDocumentAsync(long documentId);
    Task ReindexDocumentAsync(long documentId, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash);
    Task<long> GetTotalDocumentCountAsync();
    Task<long> GetTotalStorageBytesAsync();
    Task<Dictionary<string, int>> GetFileTypeDistributionAsync();
    bool CanProcess(string filePath);
    IReadOnlyList<string> GetSupportedExtensions();
}
```

```csharp
// AgentX.Core/Documents/IChunkingService.cs
namespace AgentX.Core.Documents;

public interface IChunkingService
{
    IReadOnlyList<DocumentChunk> ChunkText(
        string text,
        int chunkSize = 512,
        int chunkOverlap = 50,
        string? sectionTitle = null,
        int? pageNumber = null);

    IReadOnlyList<DocumentChunk> ChunkDocument(ProcessedDocument document, int chunkSize = 512, int chunkOverlap = 50);
}
```

### Vector Store

```csharp
// AgentX.Core/Data/VectorDb/IVectorStore.cs
namespace AgentX.Core.Data.VectorDb;

public interface IVectorStore : IDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default);
    Task InsertBatchEmbeddingsAsync(IReadOnlyList<(long ChunkId, float[] Embedding)> embeddings, CancellationToken ct = default);
    Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default);
    Task DeleteBatchEmbeddingsAsync(IReadOnlyList<long> chunkIds, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 10, CancellationToken ct = default);
    Task<long> GetEmbeddingCountAsync(CancellationToken ct = default);
    Task RebuildIndexAsync(CancellationToken ct = default);
}
```

### Search & RAG

```csharp
// AgentX.Core/Search/ISemanticSearchService.cs
namespace AgentX.Core.Search;

public interface ISemanticSearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 20,
        IReadOnlyList<long>? collectionIds = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<SearchResult>> SearchByFileTypeAsync(
        string query,
        string fileType,
        int topK = 20,
        CancellationToken ct = default);

    Task SaveSearchAsync(string query, string searchType, int resultCount);
    Task<IReadOnlyList<SearchHistoryEntity>> GetSearchHistoryAsync(int limit = 50);
    Task ClearSearchHistoryAsync();
}
```

```csharp
// AgentX.Core/Search/IRagPipeline.cs
namespace AgentX.Core.Search;

public interface IRagPipeline
{
    IAsyncEnumerable<string> AskAsync(
        string question,
        IReadOnlyList<long>? collectionIds = null,
        int contextChunks = 5,
        CancellationToken ct = default);

    Task<RagResponse> AskAndWaitAsync(
        string question,
        IReadOnlyList<long>? collectionIds = null,
        int contextChunks = 5,
        CancellationToken ct = default);
}
```

```csharp
// AgentX.Core/Search/ICitationService.cs
namespace AgentX.Core.Search;

public interface ICitationService
{
    IReadOnlyList<Citation> ExtractCitations(
        string generatedText,
        IReadOnlyList<SearchResult> sourceChunks);

    string FormatCitedResponse(
        string generatedText,
        IReadOnlyList<Citation> citations);
}
```

### Collection & Indexing Services

```csharp
// AgentX.Core/Services/Collections/ICollectionService.cs
namespace AgentX.Core.Services.Collections;

public interface ICollectionService
{
    Task<CollectionEntity> CreateCollectionAsync(string name, string? description = null, long? parentId = null);
    Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync();
    Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync();
    Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId);
    Task UpdateCollectionAsync(long collectionId, string name, string? description = null);
    Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false);
    Task AddDocumentToCollectionAsync(long documentId, long collectionId);
    Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId);
    Task MoveCollectionAsync(long collectionId, long? newParentId);
    Task<int> GetCollectionCountAsync();
}
```

```csharp
// AgentX.Core/Services/Indexing/IIndexingService.cs
namespace AgentX.Core.Services.Indexing;

public interface IIndexingService : IDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task IndexDocumentAsync(long documentId, CancellationToken ct = default);
    Task ReindexAllAsync(IProgress<(int Processed, int Total)>? progress = null, CancellationToken ct = default);
    Task<int> GetQueueLengthAsync();
    Task<int> GetProcessedCountAsync();
    bool IsProcessing { get; }
    event EventHandler<IndexingProgressEventArgs>? ProgressChanged;
    event EventHandler<long>? DocumentIndexed;
}

public class IndexingProgressEventArgs : EventArgs
{
    public int QueueLength { get; init; }
    public int Processed { get; init; }
    public string? CurrentDocument { get; init; }
    public double? PercentComplete { get; init; }
}
```

```csharp
// AgentX.Core/Services/Indexing/IFileWatcherService.cs
namespace AgentX.Core.Services.Indexing;

public interface IFileWatcherService : IDisposable
{
    Task StartWatchingAsync(CancellationToken ct = default);
    Task StopWatchingAsync();
    Task AddWatchFolderAsync(string path, bool includeSubfolders = true, string? fileTypeFilter = null, long? collectionId = null);
    Task RemoveWatchFolderAsync(long watchFolderId);
    Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync();
    bool IsWatching { get; }
    event EventHandler<string>? FileDetected;
}
```

```csharp
// AgentX.Core/Services/Indexing/IIndexingQueueService.cs
namespace AgentX.Core.Services.Indexing;

public interface IIndexingQueueService
{
    Task EnqueueAsync(long documentId);
    Task EnqueueBatchAsync(IReadOnlyList<long> documentIds);
    Task<IndexingJobEntity?> DequeueAsync(CancellationToken ct = default);
    Task MarkCompletedAsync(long jobId, int chunksProcessed, int embeddingsGenerated, double processingTimeMs);
    Task MarkFailedAsync(long jobId, string errorMessage);
    Task<int> GetPendingCountAsync();
    Task<IReadOnlyList<IndexingJobEntity>> GetRecentJobsAsync(int limit = 50);
}
```

### Intelligence Services

```csharp
// AgentX.Core/Services/Tagging/IAutoTagService.cs
namespace AgentX.Core.Services.Tagging;

public interface IAutoTagService
{
    Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(
        string documentContent,
        int maxTags = 5,
        CancellationToken ct = default);

    Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default);
    Task<IReadOnlyList<TagEntity>> GetAllTagsAsync();
    Task<TagEntity> CreateTagAsync(string name, string? colorHex = null);
    Task DeleteTagAsync(long tagId);
    Task AssignTagAsync(long documentId, long tagId);
    Task RemoveTagAsync(long documentId, long tagId);
}
```

```csharp
// AgentX.Core/Services/Intelligence/IDuplicateDetectionService.cs
namespace AgentX.Core.Services.Intelligence;

public interface IDuplicateDetectionService
{
    Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(double similarityThreshold = 0.95, CancellationToken ct = default);
    Task<bool> IsDuplicateAsync(string contentHash);
}

public record DuplicateGroup
{
    public string ContentHash { get; init; } = string.Empty;
    public IReadOnlyList<DocumentEntity> Documents { get; init; } = Array.Empty<DocumentEntity>();
    public long WastedBytes { get; init; }
}
```

```csharp
// AgentX.Core/Services/Intelligence/ISummaryService.cs
namespace AgentX.Core.Services.Intelligence;

public interface ISummaryService
{
    Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default);
    Task<string> SummarizeTextAsync(string text, int maxWords = 200, CancellationToken ct = default);
    Task<string> ExtractKeyPointsAsync(long documentId, int maxPoints = 10, CancellationToken ct = default);
    Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default);
}
```

```csharp
// AgentX.Core/Services/Intelligence/IOrganizationSuggestionService.cs
namespace AgentX.Core.Services.Intelligence;

public interface IOrganizationSuggestionService
{
    Task<IReadOnlyList<OrganizationSuggestion>> GetSuggestionsAsync(CancellationToken ct = default);
    Task ApplySuggestionAsync(long suggestionId, CancellationToken ct = default);
    Task DismissSuggestionAsync(long suggestionId);
}

public record OrganizationSuggestion
{
    public long Id { get; init; }
    public string Type { get; init; } = string.Empty; // "move_to_collection", "apply_tag", "rename", "merge"
    public string Description { get; init; } = string.Empty;
    public long DocumentId { get; init; }
    public string? TargetCollectionName { get; init; }
    public string? TargetTagName { get; init; }
    public double Confidence { get; init; }
}
```

### License & Settings

```csharp
// AgentX.Core/Services/License/ILicenseService.cs
namespace AgentX.Core.Services.License;

public interface ILicenseService
{
    Task<LicenseValidationResult> ValidateKeyAsync(string licenseKey, CancellationToken ct = default);
    Task<LicenseActivationResult> ActivateAsync(string licenseKey, CancellationToken ct = default);
    Task<bool> DeactivateAsync(CancellationToken ct = default);
    Task<LicenseEntity?> GetCurrentLicenseAsync();
    LicenseTier GetCurrentTier();
    bool IsActivated { get; }
    bool IsTrialMode { get; }
    int TrialDaysRemaining { get; }
    event EventHandler<LicenseTier>? LicenseChanged;
}

public record LicenseValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public LicenseTier Tier { get; init; }
    public string? CustomerName { get; init; }
}

public record LicenseActivationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? InstanceId { get; init; }
    public LicenseTier Tier { get; init; }
}
```

```csharp
// AgentX.Core/Services/License/LicenseTier.cs
namespace AgentX.Core.Services.License;

public enum LicenseTier
{
    Trial,     // Free, 14-day trial, 5 documents, no RAG
    Starter,   // $79, unlimited docs, basic RAG, 1 watch folder
    Professional, // $149, + auto-tag, + 5 watch folders, + quick actions
    Ultimate   // $249, + organization AI, + command palette, + priority support
}
```

```csharp
// AgentX.Core/Services/Settings/ISettingsService.cs
namespace AgentX.Core.Services.Settings;

public interface ISettingsService
{
    Task<T> GetAsync<T>(string key, T defaultValue);
    Task SetAsync<T>(string key, T value);
    Task<AppSettings> GetAllSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task ResetToDefaultsAsync();
    event EventHandler<string>? SettingChanged;
}
```

```csharp
// AgentX.Core/Services/Settings/AppSettings.cs
namespace AgentX.Core.Services.Settings;

public class AppSettings
{
    // General
    public string Theme { get; set; } = "Dark";
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
    public string Language { get; set; } = "en-US";

    // AI
    public string DefaultProvider { get; set; } = "ollama"; // "ollama" or "llamasharp"
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string DefaultChatModel { get; set; } = "llama3.2:3b";
    public string DefaultEmbeddingModel { get; set; } = "all-minilm:l6-v2";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public int ContextWindow { get; set; } = 8192;

    // Knowledge Vault
    public string StoragePath { get; set; } = string.Empty; // Defaults to %LOCALAPPDATA%/AgentX
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public bool AutoIndexOnImport { get; set; } = true;
    public bool AutoTagOnImport { get; set; } = true;
    public int MaxConcurrentIndexing { get; set; } = 2;

    // Search
    public int DefaultSearchResults { get; set; } = 20;
    public int RagContextChunks { get; set; } = 5;
    public bool SaveSearchHistory { get; set; } = true;

    // UI
    public bool ShowTokenCount { get; set; } = true;
    public bool ShowGenerationTime { get; set; } = true;
    public double FontScale { get; set; } = 1.0;
}
```

---

## 6. NuGet Package List

### AgentX.App (WinUI 3 Frontend)

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.WindowsAppSDK` | 1.6.250108002 | WinUI 3 framework |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.26100.1 | Windows SDK build tools |
| `CommunityToolkit.Mvvm` | 8.2.2 | MVVM toolkit ([ObservableProperty], [RelayCommand]) |
| `CommunityToolkit.WinUI.Controls.Primitives` | 8.0.240109 | Additional WinUI controls |
| `CommunityToolkit.WinUI.Controls.Sizers` | 8.0.240109 | Splitter/sizer controls |
| `Microsoft.Extensions.Hosting` | 8.0.1 | DI container + host builder |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | Dependency injection |
| `Serilog` | 4.0.0 | Structured logging |
| `Serilog.Extensions.Hosting` | 8.0.0 | Serilog host integration |
| `Serilog.Sinks.File` | 5.0.0 | File logging sink |
| `Serilog.Sinks.Debug` | 2.0.0 | Debug output sink |
| `Markdig` | 0.37.0 | Markdown parsing for chat rendering |
| `LiveChartsCore.SkiaSharpView.WinUI` | 2.0.0-rc2 | Charts for dashboard |
| `H.NotifyIcon.WinUI` | 2.1.3 | System tray icon |

### AgentX.Core (Business Logic)

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.1 | Logging interfaces |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 8.0.1 | DI abstractions |
| `Microsoft.Extensions.AI` | 9.3.0-preview | IChatClient abstraction |
| `Microsoft.Extensions.AI.Ollama` | 9.3.0-preview | Ollama IChatClient implementation |
| `OllamaSharp` | 4.0.6 | Ollama API client |
| `LLamaSharp` | 0.16.0 | In-process llama.cpp bindings |
| `LLamaSharp.Backend.Cpu` | 0.16.0 | CPU backend (fallback) |
| `LLamaSharp.Backend.Cuda12` | 0.16.0 | CUDA GPU backend (conditional) |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.10 | EF Core + SQLite |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.10 | EF Core migrations tooling |
| `Microsoft.Data.Sqlite` | 8.0.10 | Raw ADO.NET for sqlite-vec |
| `PDFsharp` | 6.1.1 | PDF text extraction |
| `DocumentFormat.OpenXml` | 3.1.1 | DOCX parsing |
| `System.Text.Json` | 8.0.5 | JSON serialization |
| `System.Management` | 8.0.0 | WMI for hardware detection |

### AgentX.Tests (Unit Tests)

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.NET.Test.Sdk` | 17.10.0 | Test SDK |
| `xunit` | 2.9.0 | Test framework |
| `xunit.runner.visualstudio` | 2.8.2 | VS test runner |
| `coverlet.collector` | 6.0.1 | Code coverage |
| `Moq` | 4.20.70 | Mocking framework |
| `FluentAssertions` | 6.12.0 | Assertion library |
| `Microsoft.EntityFrameworkCore.InMemory` | 8.0.10 | In-memory DB for tests |

### sqlite-vec Native Library

The `sqlite-vec` extension (`vec0.dll`) is a native SQLite extension, not a NuGet package. It must be:
1. Downloaded from the [sqlite-vec GitHub releases](https://github.com/asg017/sqlite-vec/releases)
2. Included as a native asset in the project (`runtimes/win-x64/native/vec0.dll`, etc.)
3. Loaded at runtime via `connection.LoadExtension("vec0")`

Alternatively, use the `sqlite-vec` NuGet wrapper if available at build time:
| Package | Version | Purpose |
|---------|---------|---------|
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.8 | SQLite native bindings |

---

## 7. Design System Specification

Agent-X uses a distinct design system from sysmonitor-windows, sharing the AMOLED dark theme foundation but with a new brand identity.

### Brand Identity

- **Brand Name:** Agent-X
- **Tagline:** "Your Intelligence. Your Machine. Your Control."
- **Brand Personality:** Intelligent, Private, Powerful, Premium

### Color Palette

```
PRIMARY COLORS
- Primary:           #6366F1  (Indigo 500 - AI/intelligence association)
- Primary Dark:      #4338CA  (Indigo 700 - pressed states)
- Primary Light:     #818CF8  (Indigo 400 - hover states)
- Primary Glow:      #6366F120  (20% opacity - hover backgrounds)

BACKGROUND COLORS (AMOLED Dark Theme)
- Background:        #000000  (Pure black - matches existing convention)
- Background Alt:    #0A0A0A  (Slightly elevated)
- Surface:           #111111  (Card backgrounds)
- Surface Elevated:  #1A1A1A  (Elevated panels)
- Surface High:      #242424  (Highest elevation)

BORDER COLORS
- Border Subtle:     #1F1F1F
- Border Default:    #2A2A2A
- Border Strong:     #3A3A3A
- Border Accent:     #6366F140  (40% primary)

TEXT COLORS
- Text Primary:      #FFFFFF
- Text Secondary:    #B0B0B0
- Text Tertiary:     #808080
- Text Disabled:     #505050
- Text Placeholder:  #606060

SEMANTIC COLORS
- Success:           #22C55E  (Green 500)
- Warning:           #F59E0B  (Amber 500)
- Error:             #EF4444  (Red 500)
- Info:              #3B82F6  (Blue 500)

AI-SPECIFIC COLORS
- AI Accent:         #8B5CF6  (Violet 500 - AI responses)
- User Accent:       #6366F1  (Indigo 500 - user messages)
- Citation:          #F59E0B  (Amber - citation links)
- Embedding:         #14B8A6  (Teal - indexing/embedding status)

CHART COLORS (ordered for data visualization)
- Chart 1:           #6366F1  (Indigo)
- Chart 2:           #8B5CF6  (Violet)
- Chart 3:           #EC4899  (Pink)
- Chart 4:           #14B8A6  (Teal)
- Chart 5:           #F59E0B  (Amber)
- Chart 6:           #22C55E  (Green)
```

### Typography

Following the 8pt grid system with the same scale used in ElementForge:

```
FONT FAMILY: Segoe UI Variable (Windows 11 system font)
FALLBACK: Segoe UI, sans-serif

TYPE SCALE:
- Caption:     10px  /  Regular   / 1.4 line-height  / TextTertiary
- Small:       11px  /  Regular   / 1.4 line-height  / TextSecondary
- Body:        13px  /  Regular   / 1.5 line-height  / TextPrimary
- Body Large:  14px  /  Regular   / 1.5 line-height  / TextPrimary
- Subtitle:    16px  /  SemiBold  / 1.4 line-height  / TextPrimary
- Title:       20px  /  SemiBold  / 1.3 line-height  / TextPrimary
- Heading:     24px  /  Bold      / 1.2 line-height  / TextPrimary
- Display:     32px  /  Light     / 1.1 line-height  / TextPrimary
- Hero:        48px  /  Light     / 1.1 line-height  / TextPrimary

CHARACTER SPACING:
- Normal:      0
- Loose:       50   (section headers, badges)
- Wide:        100  (all-caps labels)

MONOSPACE (for code/tokens):
- Cascadia Code, Consolas, monospace
- Body size (13px)
```

### Spacing System (8pt grid)

```
- XXS:    2px
- XS:     4px
- SM:     6px
- MD:     8px
- LG:     12px
- XL:     16px
- XXL:    20px
- 3XL:    24px
- 4XL:    32px
- 5XL:    48px
- 6XL:    64px

COMMON PATTERNS:
- Card padding:       16px (XL)
- Card gap:           12px (LG)
- Section gap:        24px (3XL)
- Page padding:       24px (3XL)
- Navigation width:   280px (expanded), 48px (collapsed)
```

### Corner Radius

```
- SM:     4px   (badges, small elements)
- MD:     6px   (inputs, small buttons)
- LG:     8px   (standard buttons)
- XL:     12px  (cards)
- XXL:    16px  (large cards, panels)
- Full:   9999px (pills, circular)
```

### Elevation (Shadows)

```
- Level 0: No shadow (flat on background)
- Level 1: 0 1px 2px rgba(0,0,0,0.3)   (cards at rest)
- Level 2: 0 2px 4px rgba(0,0,0,0.4)   (hover states)
- Level 3: 0 4px 8px rgba(0,0,0,0.5)   (floating elements)
- Level 4: 0 8px 16px rgba(0,0,0,0.6)  (dialogs, overlays)
```

### Component Styles

All component styles are defined in separate XAML ResourceDictionary files under `Styles/`:

- **Cards.xaml**: CardStyle, ElevatedCardStyle, HeroCardStyle, DocumentCardStyle, ChatCardStyle
- **Buttons.xaml**: PrimaryButtonStyle, SecondaryButtonStyle, GhostButtonStyle, DangerButtonStyle, IconButtonStyle
- **Chat.xaml**: UserBubbleStyle, AssistantBubbleStyle, SystemBubbleStyle, CodeBlockStyle
- **Controls.xaml**: InputFieldStyle, SearchBoxStyle, DropZoneStyle, BadgeStyle, ProgressBarStyle
- **Navigation.xaml**: NavigationView overrides, NavItemStyle
- **Typography.xaml**: All TextBlock styles (PageHeaderStyle, SectionHeaderStyle, etc.)
- **Colors.xaml**: All Color and SolidColorBrush definitions

---

## 8. Phase 1: Foundation and Shell (Weeks 1-4)

### Week 1: Solution Structure and Project Scaffolding

**Files to Create:**

1. `AgentX.sln` -- Solution file with three projects and solution folders
2. `Directory.Build.props` -- Shared properties (TargetFramework, ImplicitUsings, Nullable, Version, Authors)
3. `.gitignore` -- Standard .NET + WinUI ignores
4. `.editorconfig` -- Code style rules
5. `src/AgentX.App/AgentX.App.csproj` -- WinUI 3 app project
6. `src/AgentX.Core/AgentX.Core.csproj` -- Core library project
7. `tests/AgentX.Tests/AgentX.Tests.csproj` -- Test project

**Directory.Build.props Pattern** (matching ElementForge):
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningLevel>4</WarningLevel>
    <AnalysisLevel>latest</AnalysisLevel>
    <Version>1.0.0</Version>
    <Authors>Strategia / Rocky Elsalaymeh</Authors>
    <Company>Strategia</Company>
    <Product>Agent-X</Product>
    <Copyright>Copyright (c) 2026 Strategia</Copyright>
    <Description>Local-First AI Personal Intelligence Hub</Description>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <Optimize>true</Optimize>
    <DebugType>none</DebugType>
  </PropertyGroup>
</Project>
```

**AgentX.App.csproj Pattern** (matching sysmonitor-windows):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <RootNamespace>AgentX.App</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x86;x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <UseRidGraph>true</UseRidGraph>
    <ApplicationIcon>app.ico</ApplicationIcon>
  </PropertyGroup>
  <!-- Debug: Unpackaged -->
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>
  <!-- Release: MSIX Packaged -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <WindowsPackageType>MSIX</WindowsPackageType>
    <GenerateAppxPackageOnBuild>true</GenerateAppxPackageOnBuild>
  </PropertyGroup>
</Project>
```

### Week 2: App Shell, Navigation, Design System

**Files to Create:**

1. `src/AgentX.App/App.xaml` -- Application resources, merged ResourceDictionaries, converter registrations
2. `src/AgentX.App/App.xaml.cs` -- DI host builder, Serilog, 3-tier exception handling (matching sysmonitor-windows exactly)
3. `src/AgentX.App/MainWindow.xaml` -- NavigationView shell with title bar, chrome divider, content frame
4. `src/AgentX.App/MainWindow.xaml.cs` -- Page map dictionary, NavigationView_SelectionChanged handler, window icon
5. `src/AgentX.App/app.manifest` -- DPI awareness, Windows 11 compatibility
6. `src/AgentX.App/Package.appxmanifest` -- MSIX identity and capabilities
7. All files under `src/AgentX.App/Styles/`:
   - `Colors.xaml` -- Full color palette as Color and SolidColorBrush resources
   - `Typography.xaml` -- TextBlock styles for all type scale levels
   - `Cards.xaml` -- Card, ElevatedCard, HeroCard, DocumentCard styles
   - `Buttons.xaml` -- Primary, Secondary, Ghost, Danger, Icon button styles
   - `Chat.xaml` -- Chat bubble, code block, citation styles
   - `Controls.xaml` -- Input, Search, Badge, ProgressBar, DropZone styles
   - `Navigation.xaml` -- NavigationView resource overrides for dark theme

**Navigation Structure (MainWindow.xaml):**

```
NavigationView Menu Items:
  - Dashboard           (Tag: "Dashboard",     Icon: E80F,  Color: Primary)
  - AI Chat             (Tag: "Chat",          Icon: E8BD,  Color: Primary)
  - Conversations       (Tag: "Conversations", Icon: E8F2,  Color: Primary)
  - Model Manager       (Tag: "ModelManager",  Icon: E964,  Color: Primary)
  ---separator---
  - KNOWLEDGE header
  - Knowledge Vault     (Tag: "KnowledgeVault", Icon: E8F1, Color: AI Accent)
  - Collections         (Tag: "Collections",    Icon: E8B7, Color: AI Accent)
  - Search              (Tag: "Search",          Icon: E721, Color: AI Accent)
  - Ask Your Files      (Tag: "AskFiles",        Icon: E9CE, Color: AI Accent)
  ---separator---
  - TOOLS header
  - Quick Actions       (Tag: "QuickActions",   Icon: E945, Color: Teal)
  - System Prompts      (Tag: "SystemPrompts",  Icon: E8C8, Color: Teal)
  - Hardware Advisor    (Tag: "HardwareAdvisor",Icon: E7F4, Color: Teal)

Footer Menu Items:
  - Settings            (Tag: "Settings",   Icon: E713, Color: TextSecondary)
  - License             (Tag: "License",    Icon: E8D7, Color: Success)
```

### Week 3: Database Setup, Settings, Core Models

**Files to Create:**

1. `src/AgentX.Core/Data/AgentXDbContext.cs` -- Full DbContext with all DbSets and fluent configuration
2. All entity files under `src/AgentX.Core/Data/Entities/` (listed in Schema section)
3. `src/AgentX.Core/Data/VectorDb/IVectorStore.cs` -- Interface
4. `src/AgentX.Core/Data/VectorDb/SqliteVecStore.cs` -- sqlite-vec implementation
5. `src/AgentX.Core/Data/VectorDb/VectorSearchResult.cs` -- Result model
6. `src/AgentX.Core/Services/Settings/ISettingsService.cs`
7. `src/AgentX.Core/Services/Settings/SettingsService.cs`
8. `src/AgentX.Core/Services/Settings/AppSettings.cs`
9. `src/AgentX.Core/Helpers/FormatHelper.cs` -- File size formatting, time formatting
10. `src/AgentX.Core/Helpers/PathHelper.cs` -- Safe path operations, app data paths
11. `src/AgentX.Core/Helpers/HashHelper.cs` -- SHA256 file hashing
12. `src/AgentX.Core/Helpers/FileTypeHelper.cs` -- Extension-to-type mapping, MIME types

**Database initialization flow:**
1. On first launch, `AgentXDbContext` creates the database at `%LOCALAPPDATA%/AgentX/agentx.db`
2. EF Core migrations create relational tables
3. `SqliteVecStore.InitializeAsync()` loads the vec0 extension and creates the virtual table
4. `SystemPromptService.SeedBuiltInPromptsAsync()` inserts default system prompts

### Week 4: Views Skeleton, ViewModels, DI Registration

**Files to Create:**

1. All View .xaml and .xaml.cs files (skeleton with basic layout)
2. All ViewModel .cs files (skeleton with DI constructor, basic properties)
3. All Converter files under `src/AgentX.App/Converters/`
4. `src/AgentX.App/Helpers/DispatcherQueueExtensions.cs`
5. `src/AgentX.App/Helpers/NavigationHelper.cs`
6. `src/AgentX.App/Helpers/WindowHelper.cs`
7. `src/AgentX.App/Services/IDialogService.cs` and `DialogService.cs`
8. `src/AgentX.App/Services/IThemeService.cs` and `ThemeService.cs`
9. `src/AgentX.App/Services/INavigationService.cs` and `NavigationService.cs`
10. `src/AgentX.App/ViewModels/SettingsViewModel.cs` -- Full implementation (first usable page)

**DI Registration in App.xaml.cs** (following sysmonitor-windows pattern):

```csharp
// Core - Data
services.AddSingleton<AgentXDbContext>();
services.AddSingleton<IVectorStore, SqliteVecStore>();

// Core - Settings
services.AddSingleton<SettingsService>();
services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());

// Core - AI (registered in Phase 2)

// Core - Documents (registered in Phase 3)

// Core - Search (registered in Phase 4)

// App Services
services.AddSingleton<IDialogService, DialogService>();
services.AddSingleton<IThemeService, ThemeService>();
services.AddSingleton<INavigationService, NavigationService>();

// ViewModels - Transient
services.AddTransient<DashboardViewModel>();
services.AddTransient<SettingsViewModel>();
// ... all other ViewModels

// Views - Transient
services.AddTransient<DashboardPage>();
services.AddTransient<SettingsPage>();
// ... all other Pages
```

### Phase 1 Acceptance Criteria

- [ ] Solution builds on x64 and ARM64 without errors
- [ ] App launches, displays NavigationView with all menu items
- [ ] Navigation between all pages works (each shows placeholder content)
- [ ] Settings page is functional: theme selection, storage path, all options persist
- [ ] SQLite database is created on first launch at correct path
- [ ] EF Core migrations run successfully, all tables created
- [ ] sqlite-vec extension loads and virtual table is created
- [ ] Serilog produces daily rolling log files at %LOCALAPPDATA%/AgentX/Logs/
- [ ] 3-tier exception handling works (verified via intentional test throw)
- [ ] Dark theme is consistent across all navigation targets
- [ ] All design system styles render correctly

---

## 9. Phase 2: AI Chat Core (Weeks 5-8)

### Week 5: AI Provider Abstraction and Ollama Integration

**Files to Create:**

1. `src/AgentX.Core/AI/IAiProvider.cs`
2. `src/AgentX.Core/AI/Providers/OllamaProvider.cs` -- OllamaSharp-based IChatClient wrapper
3. `src/AgentX.Core/AI/Models/AiModel.cs`
4. `src/AgentX.Core/AI/Models/ModelDownloadProgress.cs`
5. `src/AgentX.Core/AI/Models/ChatMessage.cs`
6. `src/AgentX.Core/AI/Models/ChatOptions.cs`
7. `src/AgentX.Core/AI/IAiService.cs`
8. `src/AgentX.Core/AI/AiService.cs` -- Provider orchestrator
9. DI registration for AI services

**OllamaProvider implementation details:**
- Uses `OllamaSharp.OllamaApiClient` connected to `http://localhost:11434`
- Wraps streaming via `IAsyncEnumerable<string>` using `ChatAsync` with streaming
- Model listing via `/api/tags` endpoint
- Model pulling via `/api/pull` with progress reporting
- Connection health check via `/api/version`

### Week 6: LLamaSharp Fallback and Model Manager

**Files to Create:**

1. `src/AgentX.Core/AI/Providers/LLamaSharpProvider.cs` -- In-process llama.cpp
2. `src/AgentX.Core/AI/IModelManager.cs`
3. `src/AgentX.Core/AI/ModelManager.cs`
4. `src/AgentX.Core/AI/IHardwareDetector.cs`
5. `src/AgentX.Core/AI/HardwareDetector.cs`
6. `src/AgentX.Core/AI/Models/HardwareCapability.cs`
7. `src/AgentX.App/ViewModels/ModelManagerViewModel.cs` -- Full implementation
8. `src/AgentX.App/Views/ModelManagerPage.xaml` -- Full implementation
9. `src/AgentX.App/ViewModels/HardwareAdvisorViewModel.cs`
10. `src/AgentX.App/Views/HardwareAdvisorPage.xaml`

**HardwareDetector details:**
- Detects GPU via WMI (`Win32_VideoController`)
- Detects NPU via WMI + registry checks for Intel/Qualcomm NPU
- Reports VRAM, system RAM, CPU cores
- Recommends model size based on available VRAM:
  - < 4GB VRAM: 3B parameter models
  - 4-8GB VRAM: 7B parameter models
  - 8-16GB VRAM: 13B parameter models
  - 16GB+ VRAM: 70B parameter models (quantized)

### Week 7: Chat Interface with Streaming

**Files to Create:**

1. `src/AgentX.Core/Services/Chat/IChatService.cs`
2. `src/AgentX.Core/Services/Chat/ChatService.cs`
3. `src/AgentX.Core/Services/Chat/IConversationService.cs`
4. `src/AgentX.Core/Services/Chat/ConversationService.cs`
5. `src/AgentX.App/ViewModels/ChatViewModel.cs` -- Full implementation
6. `src/AgentX.App/Views/ChatPage.xaml` -- Full implementation
7. `src/AgentX.App/Controls/ChatBubble.xaml` + .cs
8. `src/AgentX.App/Controls/MarkdownRenderer.xaml` + .cs
9. `src/AgentX.App/Controls/TokenStreamingText.xaml` + .cs
10. `src/AgentX.App/Controls/ModelStatusBadge.xaml` + .cs

**ChatPage layout:**
- Top bar: Model selector dropdown, token counter, generation time
- Center: ScrollViewer with ItemsRepeater of ChatBubble controls
- Bottom: Multi-line TextBox input, Send button, Stop button, system prompt indicator
- Chat bubbles: User (right-aligned, primary color), Assistant (left-aligned, AI accent), System (centered, muted)
- Markdown rendering: Headers, bold, italic, code blocks (with syntax highlighting), lists, links
- Token streaming: Characters appear in real-time with typing cursor animation

### Week 8: Conversation Management and System Prompts

**Files to Create:**

1. `src/AgentX.Core/Services/Chat/ISystemPromptService.cs`
2. `src/AgentX.Core/Services/Chat/SystemPromptService.cs`
3. `src/AgentX.App/ViewModels/ConversationListViewModel.cs`
4. `src/AgentX.App/Views/ConversationListPage.xaml`
5. `src/AgentX.App/ViewModels/SystemPromptLibraryViewModel.cs`
6. `src/AgentX.App/Views/SystemPromptLibraryPage.xaml`

**Built-in system prompts to seed:**
- General Assistant, Code Helper, Writing Editor, Research Analyst, Creative Writer
- Data Analyzer, Summarizer, Translator, Technical Explainer, Socratic Teacher

### Phase 2 Acceptance Criteria

- [ ] Ollama connection detection works (shows connected/disconnected status)
- [ ] Model list loads from Ollama API and displays in Model Manager
- [ ] Model downloading works with real-time progress bar
- [ ] Model deletion works
- [ ] Chat sends user message and receives streaming AI response
- [ ] Tokens stream in real-time with visible typing effect
- [ ] Markdown rendering works: headers, code blocks, lists, bold/italic
- [ ] Conversations persist to database and reload correctly
- [ ] Conversation list shows all conversations sorted by last activity
- [ ] Conversation search by title/content works
- [ ] Pin/archive/delete conversation works
- [ ] System prompt selection affects AI behavior
- [ ] Custom system prompts can be created, edited, deleted
- [ ] LLamaSharp fallback loads a local GGUF model when Ollama is unavailable
- [ ] Hardware advisor detects GPU/CPU and recommends appropriate models
- [ ] Token count and generation time display correctly per message

---

## 10. Phase 3: Knowledge Vault (Weeks 9-14)

### Week 9-10: Document Import and Processing Pipeline

**Files to Create:**

1. `src/AgentX.Core/Documents/IDocumentProcessor.cs`
2. `src/AgentX.Core/Documents/Processors/PdfProcessor.cs` -- PDFsharp text extraction
3. `src/AgentX.Core/Documents/Processors/DocxProcessor.cs` -- OpenXml text extraction
4. `src/AgentX.Core/Documents/Processors/TextProcessor.cs` -- Plain text (.txt, .csv)
5. `src/AgentX.Core/Documents/Processors/MarkdownProcessor.cs` -- .md files
6. `src/AgentX.Core/Documents/Processors/ImageProcessor.cs` -- Windows.Media.Ocr
7. `src/AgentX.Core/Documents/Processors/CodeFileProcessor.cs` -- .cs, .py, .js, .ts, .java, etc.
8. `src/AgentX.Core/Documents/Models/ProcessedDocument.cs`
9. `src/AgentX.Core/Documents/Models/DocumentChunk.cs`
10. `src/AgentX.Core/Documents/Models/DocumentMetadata.cs`
11. `src/AgentX.Core/Documents/Models/SupportedFileTypes.cs`
12. `src/AgentX.Core/Documents/IDocumentService.cs`
13. `src/AgentX.Core/Documents/DocumentService.cs`

**SupportedFileTypes:**
```
PDF:      .pdf
Office:   .docx, .doc (via OpenXml)
Text:     .txt, .csv, .log, .json, .xml, .yaml, .yml, .toml
Markdown: .md, .mdx
Code:     .cs, .py, .js, .ts, .tsx, .jsx, .java, .cpp, .c, .h, .go, .rs, .rb, .php, .swift, .kt, .sql, .sh, .ps1, .bat
Image:    .png, .jpg, .jpeg, .bmp, .tiff (via Windows OCR)
```

### Week 11: Text Chunking and Embedding Generation

**Files to Create:**

1. `src/AgentX.Core/Documents/IChunkingService.cs`
2. `src/AgentX.Core/Documents/ChunkingService.cs` -- Recursive character text splitter
3. `src/AgentX.Core/AI/IEmbeddingService.cs`
4. `src/AgentX.Core/AI/EmbeddingService.cs`
5. `src/AgentX.Core/AI/Models/EmbeddingResult.cs`
6. `src/AgentX.Core/Data/VectorDb/SqliteVecStore.cs` -- Full implementation

**Chunking algorithm:**
1. Split document into paragraphs (double newline)
2. For each paragraph, if > `chunkSize` tokens, split at sentence boundaries
3. For each sentence group, if > `chunkSize`, split at word boundaries with overlap
4. Each chunk stores: content, startCharOffset, endCharOffset, pageNumber, sectionTitle
5. Default: 512 tokens per chunk, 50 tokens overlap

**Embedding flow:**
1. For each chunk, call `IEmbeddingService.GenerateEmbeddingAsync(chunk.Content)`
2. EmbeddingService calls Ollama with model `all-minilm:l6-v2` (384-dimensional output)
3. Insert (chunkId, embedding) into sqlite-vec virtual table
4. Update `DocumentChunkEntity.VectorRowId` and `IsEmbedded = true`

### Week 12: Indexing Pipeline and File Watcher

**Files to Create:**

1. `src/AgentX.Core/Services/Indexing/IIndexingService.cs`
2. `src/AgentX.Core/Services/Indexing/IndexingService.cs`
3. `src/AgentX.Core/Services/Indexing/IIndexingQueueService.cs`
4. `src/AgentX.Core/Services/Indexing/IndexingQueueService.cs`
5. `src/AgentX.Core/Services/Indexing/IFileWatcherService.cs`
6. `src/AgentX.Core/Services/Indexing/FileWatcherService.cs`

**Indexing pipeline:**
1. File imported -> DocumentEntity created with status "pending"
2. IndexingJobEntity queued
3. Background service dequeues job
4. DocumentProcessor extracts text
5. ChunkingService splits into chunks
6. DocumentChunkEntity records created
7. EmbeddingService generates vectors for each chunk (batched)
8. SqliteVecStore inserts embeddings
9. DocumentEntity updated: status "completed", ChunkCount, WordCount

**FileWatcherService:**
- Uses `System.IO.FileSystemWatcher` for each registered watch folder
- Debounces file changes (500ms) to avoid duplicate events
- Filters by configured extensions
- Enqueues new/modified files into indexing pipeline
- Runs as a background service started in `MainWindow.xaml.cs`

### Week 13: Knowledge Vault UI

**Files to Create:**

1. `src/AgentX.App/ViewModels/KnowledgeVaultViewModel.cs` -- Full implementation
2. `src/AgentX.App/Views/KnowledgeVaultPage.xaml` -- Full implementation
3. `src/AgentX.App/ViewModels/DocumentDetailViewModel.cs`
4. `src/AgentX.App/Views/DocumentDetailPage.xaml`
5. `src/AgentX.App/Controls/DocumentCard.xaml` + .cs
6. `src/AgentX.App/Controls/DropZone.xaml` + .cs

**KnowledgeVaultPage layout:**
- Header: Document count, storage used, indexing status badge
- Toolbar: Import button, filter by type, filter by status, sort options
- Drop Zone: Full-width drag-and-drop area (collapsed when documents exist, expandable)
- Grid: Responsive grid of DocumentCards showing: thumbnail, filename, type icon, size, date, chunk count, tags, indexing status
- Status bar: Indexing queue length, current indexing file, progress

**DocumentDetailPage layout:**
- Header: File name, path, type, size, imported date
- Metadata panel: Word count, chunk count, page count, language, content hash
- Tags section: Auto-generated tags with confidence, manual tags
- AI Summary: Generated summary (with "Generate" button if not yet created)
- Preview: Text preview of first 1000 characters
- Collections: List of collections this document belongs to
- Actions: Re-index, Delete, Open in Explorer, Copy path

### Week 14: Collection Management and Auto-Tagging

**Files to Create:**

1. `src/AgentX.Core/Services/Collections/ICollectionService.cs`
2. `src/AgentX.Core/Services/Collections/CollectionService.cs`
3. `src/AgentX.Core/Services/Tagging/IAutoTagService.cs`
4. `src/AgentX.Core/Services/Tagging/AutoTagService.cs`
5. `src/AgentX.App/ViewModels/CollectionManagerViewModel.cs`
6. `src/AgentX.App/Views/CollectionManagerPage.xaml`
7. `src/AgentX.App/Controls/CollectionTreeView.xaml` + .cs

**AutoTagService:**
- Sends first 2000 characters of document to LLM with prompt: "Generate 3-5 descriptive tags for this document. Return JSON array of {tag, confidence}."
- Parses response, creates TagEntity if new, creates DocumentTagEntity with confidence score
- Runs automatically after indexing completes (if AutoTagOnImport setting is enabled)

### Phase 3 Acceptance Criteria

- [ ] File import via button opens file picker, imports selected files
- [ ] Drag-and-drop import works on Knowledge Vault page
- [ ] PDF text extraction works correctly (via PDFsharp)
- [ ] DOCX text extraction works correctly (via OpenXml)
- [ ] Plain text, Markdown, and code files import correctly
- [ ] Image OCR extracts text from images (via Windows.Media.Ocr)
- [ ] Text chunking produces correctly-sized chunks with overlap
- [ ] Embedding generation produces 384-dimensional vectors
- [ ] sqlite-vec stores and retrieves embeddings correctly
- [ ] Indexing pipeline processes documents end-to-end
- [ ] Background indexing queue processes multiple documents sequentially
- [ ] File watcher detects new files in configured folders
- [ ] Document cards display correctly with metadata
- [ ] Document detail page shows all metadata and preview
- [ ] Collections can be created, renamed, nested, deleted
- [ ] Documents can be added to and removed from collections
- [ ] Auto-tagging generates relevant tags with confidence scores
- [ ] Manual tag assignment and removal works
- [ ] Indexing status is visually indicated (pending, processing, completed, failed)
- [ ] Progress reporting shows during batch import

---

## 11. Phase 4: Search and RAG (Weeks 15-18)

### Week 15-16: Semantic Search

**Files to Create:**

1. `src/AgentX.Core/Search/ISemanticSearchService.cs`
2. `src/AgentX.Core/Search/SemanticSearchService.cs`
3. `src/AgentX.Core/Search/Models/SearchQuery.cs`
4. `src/AgentX.Core/Search/Models/SearchResult.cs`
5. `src/AgentX.App/ViewModels/SearchViewModel.cs`
6. `src/AgentX.App/Views/SearchPage.xaml`
7. `src/AgentX.App/Controls/SearchResultCard.xaml` + .cs

**Semantic search flow:**
1. User enters query text
2. Query text is embedded via `IEmbeddingService`
3. Embedding is sent to `IVectorStore.SearchAsync()` for KNN search
4. Results are joined with `DocumentChunkEntity` and `DocumentEntity` for metadata
5. Results ranked by cosine similarity score
6. Search is saved to `SearchHistoryEntity`

**SearchPage layout:**
- Search bar: Large input with search icon, keyboard shortcut hint (Ctrl+K)
- Filter panel: Collection filter, file type filter, date range
- Results list: SearchResultCards showing: document name, relevance score (0-100%), matched text excerpt with highlighted keywords, file type icon, collection badges
- Pagination: "Load more" button for large result sets
- History sidebar: Recent searches, saved searches

### Week 17-18: RAG Pipeline (Ask Your Files)

**Files to Create:**

1. `src/AgentX.Core/Search/IRagPipeline.cs`
2. `src/AgentX.Core/Search/RagPipeline.cs`
3. `src/AgentX.Core/Search/ICitationService.cs`
4. `src/AgentX.Core/Search/CitationService.cs`
5. `src/AgentX.Core/Search/Models/RagResponse.cs`
6. `src/AgentX.Core/Search/Models/Citation.cs`
7. `src/AgentX.App/ViewModels/AskFilesViewModel.cs`
8. `src/AgentX.App/Views/AskFilesPage.xaml`
9. `src/AgentX.App/Controls/CitationLink.xaml` + .cs

**RAG pipeline flow:**
1. User asks a question in natural language
2. Question is embedded
3. Top-K most relevant chunks retrieved via semantic search
4. Chunks are formatted into a context prompt:
   ```
   Answer the following question using ONLY the provided context.
   Cite your sources using [1], [2], etc.
   If the context doesn't contain the answer, say so.

   CONTEXT:
   [1] (document: {filename}, page: {page})
   {chunk_text}

   [2] (document: {filename}, page: {page})
   {chunk_text}
   ...

   QUESTION: {user_question}
   ```
5. Full prompt sent to AI for streaming generation
6. CitationService extracts [N] references from response text
7. Citations are rendered as clickable links that navigate to the source document/page

**AskFilesPage layout:**
- Header: "Ask Your Files" with collection scope selector
- Chat-style interface (same ChatBubble components)
- Citations panel: Right sidebar showing source documents with page numbers
- Clicking a citation opens DocumentDetailPage at the relevant section

### Phase 4 Acceptance Criteria

- [ ] Semantic search returns relevant documents for natural language queries
- [ ] Search results show relevance scores and text excerpts
- [ ] Collection-scoped search works correctly
- [ ] File type filtering works
- [ ] Search history is saved and displayed
- [ ] Saved searches can be bookmarked and re-run
- [ ] RAG pipeline retrieves correct context chunks
- [ ] AI generates answers grounded in the provided context
- [ ] Streaming response works in RAG mode
- [ ] Citations are correctly extracted from AI response
- [ ] Citation links navigate to the source document and page/section
- [ ] Cross-collection queries work when no collection filter is applied
- [ ] Empty state handling: graceful messages when no results found
- [ ] Search performance: < 500ms for semantic search with 10K+ chunks

---

## 12. Phase 5: Dashboard and Intelligence (Weeks 19-22)

### Week 19-20: Smart Dashboard

**Files to Create:**

1. `src/AgentX.App/ViewModels/DashboardViewModel.cs` -- Full implementation
2. `src/AgentX.App/Views/DashboardPage.xaml` -- Full implementation
3. `src/AgentX.App/Controls/CircularGauge.xaml` + .cs

**Dashboard layout:**

Row 1: Hero Stats (4 cards in a responsive grid)
- Total Documents: count, file type breakdown pie chart
- Storage Used: GB used, growth trend
- AI Sessions: conversation count, total tokens used
- Index Status: indexed %, queue length, last indexed time

Row 2: Recent Activity (2 columns)
- Left: Recent documents (last 5 imported, with thumbnails)
- Right: Recent conversations (last 5, with preview)

Row 3: Knowledge Insights (2 columns)
- Left: File type distribution (LiveCharts donut chart)
- Right: Top collections by document count (horizontal bar chart)

Row 4: AI Model Status
- Active model name, provider, VRAM usage
- Quick action buttons: New Chat, Import Files, Search

### Week 21: Quick Actions and Intelligence Services

**Files to Create:**

1. `src/AgentX.Core/Services/Intelligence/ISummaryService.cs`
2. `src/AgentX.Core/Services/Intelligence/SummaryService.cs`
3. `src/AgentX.Core/Services/Intelligence/IDuplicateDetectionService.cs`
4. `src/AgentX.Core/Services/Intelligence/DuplicateDetectionService.cs`
5. `src/AgentX.Core/Services/Intelligence/IOrganizationSuggestionService.cs`
6. `src/AgentX.Core/Services/Intelligence/OrganizationSuggestionService.cs`
7. `src/AgentX.App/ViewModels/QuickActionsViewModel.cs`
8. `src/AgentX.App/Views/QuickActionsPage.xaml`

**Quick Actions available:**
- Summarize Document (select document -> AI summary)
- Extract Key Points (select document -> bullet points)
- Translate Text (paste text -> select language -> translated output)
- Find Duplicates (scan all documents -> group by content hash)
- Organization Suggestions (AI analyzes untagged/uncategorized documents)

### Week 22: Command Palette and Keyboard Shortcuts

**Files to Create:**

1. `src/AgentX.App/Controls/CommandPalette.xaml` + .cs
2. `src/AgentX.App/Services/KeyboardShortcutService.cs`

**Command Palette (Ctrl+K):**
- Overlay dialog with search input
- Fuzzy search across: pages, conversations, documents, collections, actions
- Results grouped by category with keyboard navigation
- Enter to execute, Esc to dismiss

**Keyboard Shortcuts:**
| Shortcut | Action |
|----------|--------|
| Ctrl+K | Open Command Palette |
| Ctrl+N | New Conversation |
| Ctrl+I | Import Files |
| Ctrl+F | Focus Search |
| Ctrl+Shift+F | Open Search Page |
| Ctrl+, | Open Settings |
| Escape | Cancel generation / Close dialog |

### Phase 5 Acceptance Criteria

- [ ] Dashboard displays all stats correctly with real data
- [ ] Charts render correctly (donut, bar) with proper colors
- [ ] Recent activity lists show real documents and conversations
- [ ] Quick Actions: Summarize produces coherent summaries
- [ ] Quick Actions: Key points extraction works
- [ ] Quick Actions: Translation works for major languages
- [ ] Duplicate detection finds files with identical content hashes
- [ ] Organization suggestions recommend relevant collections/tags
- [ ] Command palette opens on Ctrl+K
- [ ] Fuzzy search in command palette finds pages, documents, conversations
- [ ] All keyboard shortcuts work correctly
- [ ] Dashboard refreshes automatically when data changes

---

## 13. Phase 6: Polish and Launch (Weeks 23-26)

### Week 23: Onboarding Wizard and License System

**Files to Create:**

1. `src/AgentX.App/ViewModels/OnboardingViewModel.cs`
2. `src/AgentX.App/Views/OnboardingPage.xaml` -- Multi-step wizard
3. `src/AgentX.Core/Services/License/ILicenseService.cs`
4. `src/AgentX.Core/Services/License/LicenseService.cs`
5. `src/AgentX.Core/Services/License/LicenseTier.cs`
6. `src/AgentX.App/ViewModels/LicenseViewModel.cs`
7. `src/AgentX.App/Views/LicensePage.xaml`
8. `server/cloudflare-license-worker/worker.js` -- Reuse Strategia-X pattern
9. `server/cloudflare-license-worker/wrangler.toml`

**Onboarding wizard steps:**
1. Welcome: Brand introduction, privacy promise, feature overview
2. Ollama Setup: Check if Ollama is installed, provide download link, verify connection
3. First Model: Auto-recommend model based on hardware, pull with progress bar
4. Embedding Model: Auto-pull all-minilm:l6-v2
5. Storage Path: Configure where data is stored (default %LOCALAPPDATA%)
6. Ready: Summary of setup, "Get Started" button

**License tiers and gating:**

| Feature | Trial (14d) | Starter ($79) | Professional ($149) | Ultimate ($249) |
|---------|-------------|---------------|---------------------|-----------------|
| AI Chat | Yes | Yes | Yes | Yes |
| Documents | 5 max | Unlimited | Unlimited | Unlimited |
| Collections | 1 | 5 | Unlimited | Unlimited |
| Semantic Search | No | Yes | Yes | Yes |
| RAG (Ask Files) | No | Basic (3 chunks) | Full (10 chunks) | Full (10 chunks) |
| Watch Folders | 0 | 1 | 5 | Unlimited |
| Auto-Tagging | No | No | Yes | Yes |
| Quick Actions | No | No | Yes | Yes |
| Organization AI | No | No | No | Yes |
| Command Palette | No | No | No | Yes |
| System Prompts | 3 built-in | All built-in | All + custom | All + custom |

**License validation flow** (matching Strategia-X Cloudflare worker):
1. User enters license key in-app
2. App calls Cloudflare Worker `/activate` endpoint with key + machine instance name
3. Worker proxies to LemonSqueezy API with secret API key
4. On success, stores `LicenseEntity` in local database
5. Periodic re-validation (every 7 days) via `/validate` endpoint
6. Offline grace period: 30 days before re-validation required

### Week 24: MSIX Packaging and Distribution

**Files to Create/Update:**

1. `src/AgentX.App/Package.appxmanifest` -- Full MSIX manifest
2. `Build-Release.ps1` -- Build script for all platforms
3. `installer/AgentX.iss` -- Inno Setup script (alternative to MSIX)

**MSIX packaging configuration:**
- Package identity: `Strategia.AgentX`
- Publisher: `CN=Strategia`
- Capabilities: `runFullTrust` (required for file system access and Ollama communication)
- Build targets: x64, ARM64
- Self-contained deployment (WindowsAppSDKSelfContained=true)

**Distribution channels:**
1. **Website download**: MSIX bundle + Inno Setup installer (unsigned initially, self-signed for testing)
2. **Microsoft Store** (future, after initial traction): MSIX with Store signing
3. **GitHub Releases** (optional): For open-source community visibility

### Week 25: Performance Optimization and Testing

**Performance targets:**
- Cold startup: < 2 seconds to main window
- Navigation: < 100ms between pages
- Chat token streaming: No visible lag between tokens
- Search: < 500ms for semantic search with 10K chunks
- Import: 10 documents per second (text files), 2 per second (PDFs)
- Memory: < 300MB baseline (excluding AI model memory)

**Optimization techniques:**
1. Lazy service initialization (matching sysmonitor-windows `LazyServiceWrapper<T>`)
2. View/ViewModel transient registration (created on navigation, disposed on leave)
3. Batch embedding generation (process chunks in batches of 32)
4. Database connection pooling via EF Core
5. Virtualized lists (ItemsRepeater with virtualization)
6. Async loading with loading states on all data-dependent views
7. Background indexing with configurable concurrency

### Week 26: Final Testing, Documentation, Launch Prep

**Files to Create:**

1. `README.md` -- Product README with screenshots
2. `PRIVACY_POLICY.md` -- "Your data never leaves your machine"
3. `FEATURES_AND_USER_GUIDE.md` -- Comprehensive user documentation
4. `CHANGELOG.md` -- Version 1.0.0 release notes

**Launch checklist:**
- [ ] All 104+ unit tests passing
- [ ] Manual testing on x64 and ARM64
- [ ] Performance benchmarks meet targets
- [ ] MSIX package installs and uninstalls cleanly
- [ ] Inno Setup installer works on clean Windows 11
- [ ] License activation/deactivation flow works end-to-end
- [ ] Cloudflare Worker deployed and tested
- [ ] LemonSqueezy products created (3 tiers)
- [ ] Landing page live
- [ ] Privacy policy published
- [ ] User guide complete

### Phase 6 Acceptance Criteria

- [ ] Onboarding wizard completes successfully on clean install
- [ ] Ollama installation detection works
- [ ] Model download during onboarding works with progress
- [ ] License activation works with valid LemonSqueezy key
- [ ] License tier correctly gates features
- [ ] Trial mode works for 14 days with limitations
- [ ] MSIX package installs, runs, and uninstalls cleanly
- [ ] Inno Setup installer works as alternative
- [ ] Startup time < 2 seconds
- [ ] Memory usage < 300MB baseline
- [ ] All unit tests passing (target: 80%+ coverage on Core)
- [ ] No crash bugs in 2 hours of continuous use testing
- [ ] Documentation is complete and accurate

---

## 14. Testing Strategy

### Framework and Tools

| Tool | Version | Purpose |
|------|---------|---------|
| xunit | 2.9.0 | Test framework (matching both shipped products) |
| Moq | 4.20.70 | Mocking interfaces |
| FluentAssertions | 6.12.0 | Readable assertions |
| coverlet.collector | 6.0.1 | Code coverage |
| Microsoft.EntityFrameworkCore.InMemory | 8.0.10 | In-memory DB for integration tests |

### Test Categories and Coverage Targets

| Category | Location | Coverage Target | Description |
|----------|----------|-----------------|-------------|
| AI Services | `tests/AI/` | 70% | Provider abstraction, model manager, embedding |
| Documents | `tests/Documents/` | 85% | Chunking, text extraction, processor logic |
| Search | `tests/Search/` | 80% | Semantic search, RAG pipeline, citations |
| Services | `tests/Services/` | 80% | Chat, conversation, collection, indexing, license |
| Data | `tests/Data/` | 75% | Vector store, DbContext, entity validation |
| Helpers | `tests/Helpers/` | 90% | Pure functions: hashing, formatting, file types |

**Overall target: 80% code coverage on AgentX.Core.**

### Test Patterns (matching ElementForge)

```csharp
// Example: ChunkingServiceTests.cs
using AgentX.Core.Documents;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Documents;

public class ChunkingServiceTests
{
    private readonly ChunkingService _sut = new();

    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var text = "This is a short text.";
        var result = _sut.ChunkText(text, chunkSize: 100);
        result.Should().HaveCount(1);
        result[0].Content.Should().Be(text);
    }

    [Fact]
    public void ChunkText_LongText_SplitsWithOverlap()
    {
        var text = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"word{i}"));
        var result = _sut.ChunkText(text, chunkSize: 50, chunkOverlap: 10);
        result.Should().HaveCountGreaterThan(1);
        // Verify overlap exists
        var firstEnd = result[0].Content.Split(' ').TakeLast(10);
        var secondStart = result[1].Content.Split(' ').Take(10);
        firstEnd.Should().IntersectWith(secondStart);
    }

    [Fact]
    public void ChunkText_EmptyText_ReturnsEmptyList()
    {
        var result = _sut.ChunkText("", chunkSize: 100);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_PreservesPageNumber()
    {
        var text = "Some text on page 5.";
        var result = _sut.ChunkText(text, chunkSize: 100, pageNumber: 5);
        result[0].PageNumber.Should().Be(5);
    }
}
```

### Integration Tests

For services that require database access:
```csharp
// Use InMemory provider for fast tests
var options = new DbContextOptionsBuilder<AgentXDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
var context = new AgentXDbContext(options);
```

### What NOT to Test

- XAML views (no UI automation in this phase)
- Ollama API calls (mock the provider interface)
- LLamaSharp inference (mock the provider interface)
- File system operations (mock or use temp directories)
- Windows OCR (mock the ImageProcessor)

---

## 15. Risk Register

| # | Risk | Probability | Impact | Mitigation |
|---|------|-------------|--------|------------|
| R1 | Ollama not installed by user | High | High | Onboarding wizard guides installation. LLamaSharp fallback provides in-process inference. Clear error messaging if neither is available. |
| R2 | sqlite-vec extension loading fails on some systems | Medium | High | Ship vec0.dll as a native asset per-platform. Provide fallback to brute-force cosine similarity on small datasets (< 1000 chunks). Test on clean Windows 11 VMs. |
| R3 | Large document corpus causes memory pressure | Medium | Medium | Batch processing with configurable concurrency. Streaming chunk processing (don't load entire document into memory). SQLite WAL mode for concurrent reads. |
| R4 | LLamaSharp CUDA detection fails | Medium | Medium | Graceful fallback to CPU backend. Clear messaging in Hardware Advisor. Test on machines without NVIDIA GPU. |
| R5 | Embedding model unavailable on first launch | High | Medium | Auto-pull during onboarding. Cache embedding model check. Provide manual retry in Settings. Queue documents for later indexing. |
| R6 | MSIX signing costs/complexity | Medium | Low | Start with self-signed for direct distribution. Use Inno Setup installer as primary channel. Plan Microsoft Store submission later. |
| R7 | WinUI 3 rendering bugs on older Windows 10 | Low | Medium | Target Windows 10 19041+ (same as both shipped products). Test on Windows 10 21H2 and Windows 11. |
| R8 | Token streaming performance with large responses | Low | Medium | Use DispatcherQueue for UI updates. Batch UI updates (accumulate 10-20 tokens before updating TextBlock). Virtualized chat list. |
| R9 | Competitor launches similar product during development | Medium | Medium | 26-week timeline is aggressive. Core differentiators (native, one-time purchase, privacy) are defensible. Ship MVP at Phase 4 (Week 18) if needed. |
| R10 | PDFsharp cannot extract text from scanned PDFs | High | Medium | Detect image-only PDFs, fall back to Windows OCR per page. Document limitation clearly. Recommend pre-OCR'd PDFs. |
| R11 | License validation worker downtime | Low | Low | 30-day offline grace period. Local license cache. Fallback: skip validation if worker unreachable (allow continued use). |
| R12 | Scope creep beyond 26 weeks | Medium | High | Strict phase gates. Each phase has clear acceptance criteria. Phases 5-6 can be deferred to v1.1 if Phase 4 is shippable. |

---

## 16. Distribution Plan

### Distribution Channels

1. **Primary: Website Direct Download**
   - Inno Setup installer (.exe) -- signed with code signing certificate
   - Portable ZIP (no installer required)
   - MSIX bundle (for users who prefer Windows package management)

2. **Secondary: Microsoft Store** (post-launch, v1.1+)
   - MSIX package submitted through Partner Center
   - Benefits: Auto-updates, SmartScreen trust, discoverability
   - Requires: EV code signing certificate or Store signing

3. **GitHub Releases** (optional)
   - Tag releases with version numbers
   - Attach installer and portable ZIP

### License Key System

**LemonSqueezy Products (3 tiers):**

| Product | Price | Activation Limit | LemonSqueezy Product ID |
|---------|-------|------------------|------------------------|
| Agent-X Starter | $79 | 2 machines | (to be created) |
| Agent-X Professional | $149 | 3 machines | (to be created) |
| Agent-X Ultimate | $249 | 5 machines | (to be created) |

**Cloudflare Worker** (reuse Strategia-X pattern):
- Deploy to `agentx-license.{domain}.workers.dev`
- Endpoints: `/validate`, `/activate`, `/deactivate`, `/health`
- Secret: `LEMONSQUEEZY_API_KEY` stored via `wrangler secret put`

**In-app license flow:**
1. First launch -> 14-day trial (stored locally, no server call)
2. Settings > License > Enter license key
3. App calls Cloudflare Worker `/activate`
4. On success: store LicenseEntity, unlock tier features
5. Background: Re-validate every 7 days via `/validate`
6. Deactivation: Settings > License > Deactivate (frees machine slot)

### Update Mechanism

**v1.0: Manual updates**
- Check GitHub/website for latest version on app startup (once per day)
- Display notification badge if update available
- User downloads and installs manually

**v1.1+ (planned): Auto-update**
- Integrate `Squirrel.Windows` or `WinGet` for silent background updates
- Or leverage Microsoft Store auto-update if published there

### Build Pipeline

```powershell
# Build-Release.ps1 pattern (matching sysmonitor-windows)
# 1. Clean and restore
dotnet clean AgentX.sln
dotnet restore AgentX.sln

# 2. Run tests
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --configuration Release

# 3. Publish for each platform
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-arm64 --self-contained

# 4. Build MSIX (Release configuration)
dotnet build src/AgentX.App/AgentX.App.csproj -c Release -p:Platform=x64

# 5. Build Inno Setup installer
iscc installer/AgentX.iss

# 6. Create portable ZIP
Compress-Archive -Path publish/win-x64/* -DestinationPath dist/AgentX-1.0.0-win-x64.zip
```

---

## 17. Critical Path Analysis

### Dependency Graph

```
Phase 1 (Foundation)
  |
  +--> Phase 2 (AI Chat)  ----+
  |                             |
  +--> Phase 3 (Knowledge) ----+--> Phase 4 (Search & RAG)
                                         |
                                         +--> Phase 5 (Dashboard & Intelligence)
                                         |
                                         +--> Phase 6 (Polish & Launch)
```

### Critical Path Items (blocks all downstream work)

1. **Week 1-2: Solution structure + App shell** -- Everything depends on this
2. **Week 3: Database schema** -- Phases 2-6 all write to the database
3. **Week 5: OllamaProvider** -- Chat, embeddings, RAG all depend on AI provider
4. **Week 11: EmbeddingService + SqliteVecStore** -- Search and RAG cannot function without embeddings
5. **Week 15-16: SemanticSearchService** -- RAG pipeline depends on search

### Parallelizable Work

| Phase | Can Parallelize With | Notes |
|-------|---------------------|-------|
| Phase 2 (AI Chat) | Phase 3 (Knowledge Vault) | After Week 5 (OllamaProvider), document processing can begin independently |
| Week 7 (Chat UI) | Week 9-10 (Document processors) | UI work is independent of document processing |
| Week 13 (Knowledge UI) | Week 8 (Conversations) | Different pages, no shared state |
| Week 21 (Quick Actions) | Week 22 (Command Palette) | Independent features |
| Week 23 (Onboarding) | Week 24 (MSIX Packaging) | Can build installer while polishing onboarding |

### Minimum Viable Product (MVP) Milestone

**Week 18 (end of Phase 4) is the MVP:**
- AI chat with streaming works
- Document import and indexing works
- Semantic search works
- "Ask Your Files" RAG works
- Settings and basic UI complete

This is shippable as an Early Access / Beta release. Phases 5-6 add polish, intelligence features, and distribution -- they are valuable but not blocking an initial release.

### Definition of Done -- All Phases

| Phase | Definition of Done |
|-------|-------------------|
| Phase 1 | App launches, navigates, persists settings, database created, logs written, dark theme consistent |
| Phase 2 | AI chat streams tokens, conversations persist, models manageable, system prompts work, hardware detected |
| Phase 3 | Files import (5 types), chunk, embed, store; collections and tags work; file watcher detects new files |
| Phase 4 | Semantic search returns relevant results < 500ms; RAG answers questions with citations; cross-collection search works |
| Phase 5 | Dashboard shows real stats with charts; quick actions work; command palette opens on Ctrl+K; duplicate detection works |
| Phase 6 | Onboarding completes on clean install; license activates; MSIX installs; startup < 2s; 80% test coverage; no crash bugs |

---

### Critical Files for Implementation
- `C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/sysmonitor-windows/src/SysMonitor.App/App.xaml.cs` - Primary reference for DI container setup, Serilog configuration, 3-tier exception handling, and the exact service registration pattern to replicate
- `C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/sysmonitor-windows/src/SysMonitor.App/MainWindow.xaml` - Reference for NavigationView shell pattern, page mapping, title bar design, and dark theme navigation styling
- `C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/sysmonitor-windows/src/SysMonitor.App/Styles/Colors.xaml` - Reference for AMOLED dark theme color architecture, brush naming conventions, and gradient patterns to adapt for Agent-X brand
- `C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/sysmonitor-windows/src/SysMonitor.Core/Data/HistoryDbContext.cs` - Reference for EF Core DbContext setup, SQLite path resolution, fluent API configuration, and index strategy
- `C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/sysmonitor-windows/src/SysMonitor.App/ViewModels/DashboardViewModel.cs` - Reference for ObservableObject + [ObservableProperty] + [RelayCommand] pattern, DispatcherQueue usage, async initialization, IDisposable lifecycle, and the exact ViewModel coding conventions to follow