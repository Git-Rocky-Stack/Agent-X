# Agent-X v1.1+ Enhancement Roadmap

**Product:** Agent-X — Private AI Command Center for Windows
**Version:** v1.1.0 (Enhancement Wave)
**Developer:** Rocky Stack / Strategia
**Date:** March 7, 2026
**Baseline:** v1.0.0 feature-complete with Tier 1-3 original enhancements shipped

---

## Overview

This document defines the next wave of enhancements for Agent-X, organized into four priority tiers. Each enhancement is designed to increase product differentiation, justify the $79/$149/$249 pricing tiers, and build competitive moats against other local-first AI tools (AnythingLLM, PrivateGPT, Obsidian + AI plugins, etc.).

---

## Tier 1: High-Impact Differentiators

These features close critical product gaps and create standout capabilities no local-first competitor currently offers.

### Enhancement 1: Export & Share

**Priority:** Critical
**Effort:** Medium
**License Gate:** Professional ($149) and Ultimate ($249)

**Description:**
Enable users to get work product *out* of Agent-X in shareable formats. Currently data flows in but nothing comes out in a portable, presentation-ready format.

**Capabilities:**
- Export individual conversations as PDF, Markdown, or HTML with full formatting
- Export RAG-cited answers as formatted reports with inline source citations
- Export entire collections as zipped document bundles with metadata manifest
- Batch export multiple conversations into a single PDF/Markdown file
- Copy-to-clipboard for individual AI responses (Markdown or plain text)
- Export conversation history as JSON for archival/migration

**Technical Approach:**
- New `IExportService` in `AgentX.Core/Services/Export/`
- PDF generation via QuestPDF (MIT-licensed .NET PDF library)
- Markdown/HTML templating for conversation export
- ZIP packaging for collection bundles using System.IO.Compression
- New "Export" button/menu on ChatPage, SearchPage, and CollectionManagerPage
- Export dialog with format selection, scope options, and destination picker

---

### Enhancement 2: Prompt Workflows / Chains

**Priority:** Critical
**Effort:** High
**License Gate:** Ultimate ($249)

**Description:**
Let users define multi-step AI pipelines that chain prompts together. Each step's output feeds into the next step's input, enabling complex workflows like "Summarize -> Extract key points -> Generate action items -> Draft email."

**Capabilities:**
- Visual workflow builder page with drag-and-drop step cards
- Pre-built workflow templates: Summarize & Act, Research Brief, Document Review, Content Repurpose
- Custom step types: AI Prompt, Document Lookup (RAG), Text Transform, Conditional Branch, Output Format
- Save, name, and reuse workflows from Quick Actions
- Per-step model selection (use fast model for extraction, powerful model for generation)
- Workflow execution log showing each step's input/output
- Import/export workflow definitions as JSON

**Technical Approach:**
- New `AgentX.Core/Services/Workflows/` namespace
- `IWorkflowService`, `WorkflowEngine`, `WorkflowStep` model classes
- New `WorkflowEntity` and `WorkflowStepEntity` database tables
- New `WorkflowBuilderPage.xaml` and `WorkflowBuilderViewModel.cs`
- New `WorkflowRunnerPage.xaml` for execution monitoring
- Integration with existing `IAiService` for per-step inference
- Integration with `IRagPipeline` for document lookup steps

---

### Enhancement 3: Web Content Ingestion

**Priority:** High
**Effort:** Medium
**License Gate:** Starter ($79) and above

**Description:**
Allow users to paste a URL and ingest web article content directly into the Knowledge Vault. Removes the biggest friction point in building a knowledge base — users no longer need to manually copy-paste or download web content.

**Capabilities:**
- Paste URL -> extract article text, title, author, publish date
- Clean extraction (removes ads, navigation, scripts) using readability algorithm
- YouTube URL -> extract transcript via public API
- Source URL preserved as metadata on the DocumentEntity
- Batch URL import (paste multiple URLs, one per line)
- Browser extension or clipboard monitoring (optional, future)
- Respect robots.txt and rate limiting

**Technical Approach:**
- New `WebProcessor` implementing `IDocumentProcessor` in `AgentX.Core/Documents/Processors/`
- HTML parsing via HtmlAgilityPack + custom readability extraction
- YouTube transcript extraction via public `youtube-transcript-api` equivalent
- New `SourceUrl` property on `DocumentEntity`
- URL import dialog accessible from Knowledge Vault page and via Command Palette
- New `IWebScraperService` in `AgentX.Core/Services/Web/`

---

### Enhancement 4: Conversation Branching & Forking

**Priority:** High
**Effort:** High
**License Gate:** Professional ($149) and Ultimate ($249)

**Description:**
Allow users to branch a conversation at any message to explore alternate AI responses without losing the original thread. Creates a tree-structured conversation model.

**Capabilities:**
- Right-click any message -> "Branch from here" creates a new conversation fork
- Tree view in conversation pane showing branches as indented children
- Switch between branches to compare different AI responses
- Branch naming and annotation
- Merge insights from branches back into main thread (copy messages)
- Visual indicator on messages that have branches
- Delete individual branches without affecting siblings

**Technical Approach:**
- Add `ParentConversationId` and `BranchPointMessageId` columns to `ConversationEntity`
- Modify `IConversationService` to support tree queries
- Update `ChatViewModel` conversation list to render tree structure
- Branch creation duplicates messages up to branch point, creates new conversation
- New branch indicator UI in conversation sidebar
- Conversation export includes branch structure

---

## Tier 2: Revenue & Retention Drivers

These features increase daily usage stickiness and justify premium pricing tiers.

### Enhancement 5: Backup & Restore

**Priority:** Critical
**Effort:** Medium
**License Gate:** All tiers (basic backup free, scheduled backup Professional+)

**Description:**
Full encrypted backup of the entire Agent-X database and document store. For a local-first app charging up to $249, data safety is table-stakes. Users won't trust the app with critical knowledge without this.

**Capabilities:**
- One-click full backup to encrypted ZIP (AES-256)
- Backup includes: SQLite database, vector store, indexed document references, settings, workflows
- Choose destination: local folder, external drive, network path
- Scheduled auto-backup (daily/weekly) with configurable retention (keep last N backups)
- One-click restore from backup file with integrity verification
- Backup size estimation before execution
- Backup history log with timestamps and sizes

**Technical Approach:**
- New `IBackupService` in `AgentX.Core/Services/Backup/`
- SQLite database backup via `SqliteConnection.BackupDatabase()`
- AES-256 encryption via `System.Security.Cryptography`
- ZIP packaging via `System.IO.Compression.ZipFile`
- New `BackupEntity` for tracking backup history
- Scheduled execution via `System.Threading.Timer` or background `IHostedService`
- Settings page section for backup configuration
- Restore wizard dialog

---

### Enhancement 6: Keyboard-First Power Mode

**Priority:** Medium
**Effort:** Low-Medium
**License Gate:** All tiers

**Description:**
Full keyboard navigation throughout the app with customizable shortcuts. Power users (developers, researchers) who pay $249 live on the keyboard.

**Capabilities:**
- Global shortcuts: Ctrl+N (new conversation), Ctrl+Shift+F (search), Ctrl+K (command palette — already exists)
- Chat shortcuts: Ctrl+Enter (send), Escape (cancel generation), Ctrl+Shift+C (copy last response)
- Navigation: Ctrl+1-9 for sidebar pages, Ctrl+Tab cycle conversations
- Vim-style optional mode: j/k scroll messages, / search, g+g top, G bottom
- Customizable keybinds in Settings page
- Keyboard shortcut cheat sheet overlay (Ctrl+?)
- Focus management: Tab order optimized for all pages

**Technical Approach:**
- Extend existing `KeyboardShortcutService` in `AgentX.App/Services/`
- `KeyBinding` model with configurable mappings stored in `AppSettings`
- Accelerator keys on all major UI elements
- New "Keyboard Shortcuts" section in Settings page
- Help overlay page/dialog showing all available shortcuts
- Vim mode as opt-in toggle in Settings

---

### Enhancement 7: Document Annotations & Highlights

**Priority:** Medium
**Effort:** High
**License Gate:** Professional ($149) and Ultimate ($249)

**Description:**
Allow users to add inline highlights and notes on indexed documents. AI can reference user annotations in RAG responses. Turns Agent-X from a search tool into a *thinking* tool.

**Capabilities:**
- Highlight text passages in document preview with color coding (yellow, green, blue, red, purple)
- Add notes/comments attached to highlights
- "Show me my highlights on [topic]" in chat — AI retrieves annotated passages
- Annotations panel showing all highlights across documents, filterable by color/tag
- Export annotations as Markdown summary
- Annotation search — find highlights containing specific text
- Annotation counts shown on document cards in Knowledge Vault

**Technical Approach:**
- New `AnnotationEntity` with `DocumentId`, `StartOffset`, `EndOffset`, `Color`, `NoteText`, `CreatedAt`
- New `IAnnotationService` in `AgentX.Core/Services/Annotations/`
- Document preview control with selection-based highlight creation
- Annotations indexed in vector store for RAG retrieval
- New `AnnotationsPage.xaml` for browsing all annotations
- Integration with `SemanticSearchService` to boost annotated chunks

---

### Enhancement 8: Multi-Language UI Support

**Priority:** Medium
**Effort:** Medium
**License Gate:** All tiers

**Description:**
UI localization framework enabling the app to be used in multiple languages. Opens international markets — the local-first privacy angle resonates strongly in EU and Asia markets.

**Supported Languages (Phase 1):**
- English (default)
- Spanish (es)
- German (de)
- French (fr)
- Japanese (ja)
- Chinese Simplified (zh-CN)

**Capabilities:**
- All UI strings externalized to resource files
- Language picker in Settings (or auto-detect from OS locale)
- RTL layout support foundation for future Arabic/Hebrew
- Date/number formatting respects locale
- AI system prompts automatically include language preference hint

**Technical Approach:**
- WinUI 3 `x:Uid` resource binding with `.resw` resource files per language
- `Resources/` directory with `en-US/Resources.resw`, `es/Resources.resw`, etc.
- `ILocalizationService` wrapping `Windows.ApplicationModel.Resources.ResourceLoader`
- Settings toggle for language override vs. system default
- Gradual rollout: start with English + Spanish, add others incrementally

---

## Tier 3: Polish & Competitive Edge

These features provide differentiation and delight that competitors lack.

### Enhancement 9: Workspace Profiles

**Priority:** Medium
**Effort:** Medium
**License Gate:** Professional ($149) and Ultimate ($249)

**Description:**
Multiple isolated workspaces (e.g., "Work", "Research", "Personal"), each with their own documents, collections, conversations, model preferences, and system prompts.

**Capabilities:**
- Create/rename/delete workspaces from a workspace switcher
- Each workspace has its own SQLite database and vector store
- Workspace-specific settings (default model, temperature, system prompt)
- Quick switch via Ctrl+Shift+1/2/3 or workspace dropdown in title bar
- Workspace color/icon customization
- Import/export individual workspaces as backup bundles
- Cross-workspace search (optional, search all workspaces at once)

**Technical Approach:**
- Workspace metadata stored in a root-level `workspaces.json` or `WorkspacesDb`
- Each workspace gets its own subdirectory under `StoragePath`
- `IWorkspaceService` manages creation, switching, and lifecycle
- `AgentXDbContext` connection string dynamically set per active workspace
- Workspace switcher UI in MainWindow title bar area

---

### Enhancement 10: Smart Inbox / Document Triage

**Priority:** Medium
**Effort:** Medium
**License Gate:** Starter ($79) and above

**Description:**
When watch folders detect new files, show them in a dedicated "Inbox" with AI-generated previews, suggested collections, and one-click accept/reject/tag actions. Puts the user in control of what enters their knowledge base.

**Capabilities:**
- Dedicated "Inbox" page in sidebar navigation
- New files from watch folders land in Inbox instead of auto-indexing
- AI-generated 2-3 sentence preview of each document
- AI-suggested collection placement and tags
- One-click: Accept (index + file to suggested collection), Reject (skip), Defer (keep in inbox)
- Batch actions: Accept All, Reject All, Accept Selected
- Inbox badge count on sidebar icon
- Configurable: toggle between auto-index and inbox triage mode per watch folder

**Technical Approach:**
- New `InboxEntity` with `FilePath`, `Status`, `SuggestedCollectionId`, `SuggestedTags`, `Preview`
- New `IInboxService` in `AgentX.Core/Services/Inbox/`
- Modify `FileWatcherService` to route to inbox when triage mode is enabled
- New `InboxPage.xaml` and `InboxViewModel.cs`
- AI preview generation via `ISummaryService.SummarizeAsync()` on first N bytes
- Badge count via NavigationView InfoBadge

---

### Enhancement 11: Comparative Analysis Mode

**Priority:** Medium
**Effort:** Medium
**License Gate:** Ultimate ($249)

**Description:**
Select 2+ documents and have the AI generate a structured side-by-side comparison: similarities, differences, contradictions, and unique points in each document. Invaluable for researchers, analysts, and legal professionals.

**Capabilities:**
- Multi-select documents in Knowledge Vault -> "Compare" button
- AI-generated comparison report with structured sections
- Side-by-side view with synchronized scrolling
- Highlight contradictions and agreements between documents
- Export comparison report as PDF/Markdown
- Save comparison as a new document in Knowledge Vault
- Comparison history (re-run with different documents)

**Technical Approach:**
- New `IComparisonService` in `AgentX.Core/Services/Intelligence/`
- Structured prompt engineering for comparison output (Markdown table format)
- New `ComparisonPage.xaml` with split-view layout
- Integration with `IDocumentService` for multi-document retrieval
- Integration with `IExportService` for report generation
- Uses RAG pipeline to pull relevant chunks from each document

---

### Enhancement 12: System Tray & Global Hotkey

**Priority:** High
**Effort:** Medium
**License Gate:** All tiers

**Description:**
Minimize Agent-X to system tray with a global hotkey (Win+Shift+A) to instantly open a quick-chat overlay from anywhere on the desktop. Makes Agent-X feel like a native system utility.

**Capabilities:**
- Minimize to system tray instead of taskbar (configurable)
- System tray icon with context menu: Open, Quick Chat, New Conversation, Quit
- Global hotkey (Win+Shift+A, customizable) opens a compact quick-chat overlay window
- Quick-chat overlay: floating borderless window with chat input and response area
- Overlay auto-hides on focus loss (configurable)
- Tray icon tooltip shows active model and connection status
- Notification toasts for completed indexing jobs, digest reports

**Technical Approach:**
- System tray via `Microsoft.Windows.AppNotifications` and/or WinForms `NotifyIcon` interop
- Global hotkey registration via `RegisterHotKey` Win32 API (P/Invoke)
- New `QuickChatWindow.xaml` — compact overlay window
- `ISystemTrayService` in `AgentX.App/Services/`
- MainWindow minimize override to hide to tray
- Settings section for tray behavior and hotkey customization

---

## Tier 4: Future Vision (v2.0 Roadmap)

These features represent the long-term product evolution and are documented here for planning purposes.

### Enhancement 13: Plugin / Extension API

**Priority:** Low (v2.0)
**Effort:** Very High
**License Gate:** Ultimate ($249)

**Description:**
A plugin system allowing third-party document processors, custom AI providers, custom Quick Actions, and custom workflow steps. Creates ecosystem stickiness.

**Capabilities:**
- Plugin discovery and installation from local `.agentx-plugin` packages
- Plugin types: Document Processor, AI Provider, Quick Action, Workflow Step, Theme
- Plugin manifest format (JSON) with versioning and dependency declaration
- Plugin sandbox with limited API surface for security
- Plugin manager page for enable/disable/update/remove
- Community plugin gallery (future, hosted on website)

**Technical Approach:**
- Plugin loading via `System.Runtime.Loader.AssemblyLoadContext` for isolation
- `IPlugin` base interface with lifecycle hooks (Initialize, Activate, Deactivate, Dispose)
- Plugin API NuGet package `AgentX.PluginSDK` exposing safe interfaces
- MEF (Managed Extensibility Framework) or custom discovery
- Plugin settings stored per-plugin in database

---

### Enhancement 14: Voice Input & Audio Transcription

**Priority:** Low (v2.0)
**Effort:** High
**License Gate:** Professional ($149) and Ultimate ($249)

**Description:**
Local speech-to-text for chat input using Whisper, plus audio file ingestion (.mp3, .wav, .m4a) into the Knowledge Vault with full transcription.

**Capabilities:**
- Push-to-talk voice input in chat (microphone button)
- Real-time transcription display while speaking
- Audio file import: drag-drop .mp3/.wav/.m4a -> auto-transcribe -> index transcript
- Speaker diarization (identify different speakers in multi-person audio)
- Timestamp-linked transcripts for easy navigation
- Configurable Whisper model size (tiny/base/small/medium for speed vs. accuracy)

**Technical Approach:**
- Whisper.net (C# bindings for OpenAI Whisper) for local transcription
- New `AudioProcessor` implementing `IDocumentProcessor`
- NAudio for audio format handling and microphone capture
- New `ITranscriptionService` in `AgentX.Core/AI/`
- Whisper model download integrated into Model Manager
- Transcript stored as document with timestamp metadata per segment

---

### Enhancement 15: Collaborative Sync (Optional)

**Priority:** Low (v2.0)
**Effort:** Very High
**License Gate:** Ultimate ($249)

**Description:**
Opt-in encrypted sync between two Agent-X installations (e.g., work desktop and home laptop) via user-provided storage (OneDrive, Google Drive, NAS, USB drive). Maintains the local-first promise while solving the multi-device problem.

**Capabilities:**
- Sync via user-chosen folder (cloud drive folder, network share, USB)
- End-to-end encryption (AES-256) — sync folder contents are unreadable without Agent-X
- Conflict resolution: last-write-wins with manual merge option for conversations
- Selective sync: choose which workspaces/collections to sync
- Sync status indicator in status bar
- Sync history log with details on what changed

**Technical Approach:**
- CRDT-based or timestamp-based merge strategy for database records
- Change tracking via `ModifiedAt` timestamps on all entities
- Encrypted delta export/import to sync folder
- `ISyncService` with `ExportChanges()` and `ImportChanges()` methods
- File system watcher on sync folder for incoming changes
- Sync conflict resolution dialog

---

## Implementation Priority Matrix

| Enhancement | Impact | Effort | Revenue Tier | Priority Score |
|-------------|--------|--------|-------------|---------------|
| 1. Export & Share | Very High | Medium | Pro/Ultimate | **9/10** |
| 12. System Tray & Global Hotkey | Very High | Medium | All | **9/10** |
| 3. Web Content Ingestion | Very High | Medium | Starter+ | **8/10** |
| 5. Backup & Restore | High | Medium | All | **8/10** |
| 2. Prompt Workflows / Chains | Very High | High | Ultimate | **8/10** |
| 10. Smart Inbox / Document Triage | High | Medium | Starter+ | **7/10** |
| 4. Conversation Branching | High | High | Pro/Ultimate | **7/10** |
| 6. Keyboard Power Mode | Medium | Low | All | **7/10** |
| 11. Comparative Analysis | High | Medium | Ultimate | **7/10** |
| 9. Workspace Profiles | High | Medium | Pro/Ultimate | **6/10** |
| 7. Document Annotations | Medium | High | Pro/Ultimate | **6/10** |
| 8. Multi-Language UI | Medium | Medium | All | **5/10** |
| 14. Voice Input & Audio | High | High | Pro/Ultimate | **5/10** |
| 13. Plugin API | High | Very High | Ultimate | **4/10** |
| 15. Collaborative Sync | Medium | Very High | Ultimate | **3/10** |

---

## Version Targets

| Version | Enhancements Included | Target |
|---------|----------------------|--------|
| **v1.1.0** | #1 Export, #2 Workflows, #3 Web Ingestion, #4 Branching | Tier 1 |
| **v1.2.0** | #5 Backup, #6 Keyboard, #7 Annotations, #8 i18n | Tier 2 |
| **v1.3.0** | #9 Workspaces, #10 Smart Inbox, #11 Comparison, #12 System Tray | Tier 3 |
| **v2.0.0** | #13 Plugins, #14 Voice/Audio, #15 Sync | Tier 4 |
