# Task 7 — Orphan Localization Audit

**Baseline**: `tools/LocaleAudit/baseline-with-orphans.json` — 138 unique orphan keys per locale × 6 locales = 828 rows.
**Scope**: Also applies to the invariant base `src/AgentX.App/Strings/Resources.resw` (same 138 keys present), for a total of 7 files × 138 rows = **966 rows slated for removal**.

## Summary
**138 truly-dead | 0 dynamic/reflection | 0 regex false-negatives**

All 138 orphan keys were verified via a 3-channel audit across the entire worktree (App + tests + tools + docs):

### Channel A — Direct string literal ("Key")
Searched `src/AgentX.App/**/*.cs` and `**/*.xaml` for `"<key>"` occurrences.
**Result**: Zero hits for any orphan key in production source.

### Channel B — Dynamic GetString construction
Searched for `GetString($"...{prefix}_...")`, `GetString("prefix_" + var)`, and `"prefix_" + variable` patterns across all `.cs` files.
**Result**: Exactly 1 dynamic GetString call site — `tests/LocaleAudit.Tests/CSharpGetStringExtractorTests.cs` line with `GetString(someVar + "Suffix")` — which is a **test fixture string** inside a raw-string literal used to verify the extractor's behavior on concatenation patterns. It does **not** load resw entries.

### Channel C — XAML bindings (`x:Uid`, `{Binding}`, `{x:Bind}`)
Searched all `.xaml` files in `src/AgentX.App` for `x:Uid="<key>"` references.
**Result**: Zero hits for any orphan key in production XAML.

### Broad substring sweep (belt-and-suspenders)
Scanned 532 non-build files across the repo for any textual occurrence of each orphan key. All matches were in either:
- `tools/LocaleAudit/baseline-with-orphans.json` (the orphan list itself — self-reference, expected)
- `tests/LocaleAudit.Tests/*.cs` — **test fixtures** that embed sample keys like `Nav_Dashboard`, `Nav_Chat`, `Nav_Settings`, `Search_ResultCount` inside raw C# string fixtures fed to the extractor under test. These fixtures test the extractor *logic*; they do not depend on resw entries existing.
- `tools/LocaleAudit/CoverageReport.cs` — a single XML-doc comment (`/// e.g. "Nav_Dashboard"`). Documentation, not code.

### Resource-loader audit
Scanned all `.cs` files for `ResourceLoader` assignments and `.GetString(...)` invocations with non-literal arguments. No such dynamic-key loader variables exist in production code.

## Verdict
All 138 orphan keys are safe to delete. Classification:

| Bucket | Count | Notes |
|---|---|---|
| Truly dead | 138 | No references in production source (any channel). |
| Dynamic / reflection | 0 | No dynamic-key patterns found. |
| Regex false-negatives | 0 | No direct references missed by extractors. |

## Group Plan (alphabetical; 13 groups)

### Action_ group (19 keys)
- `Action_Browse`
- `Action_Cancel`
- `Action_Clear`
- `Action_Close`
- `Action_Copy`
- `Action_Create`
- `Action_Delete`
- `Action_Disable`
- `Action_Edit`
- `Action_Enable`
- `Action_Export`
- `Action_Import`
- `Action_Install`
- `Action_Refresh`
- `Action_Save`
- `Action_Search`
- `Action_SelectAll`
- `Action_SelectNone`
- `Action_Uninstall`

### Annotations_ group (8 keys)
- `Annotations_Distribution`
- `Annotations_Empty`
- `Annotations_EmptyHint`
- `Annotations_ExportMarkdown`
- `Annotations_FilterByColor`
- `Annotations_Highlights`
- `Annotations_SearchPlaceholder`
- `Annotations_Title`

### Backup_ group (10 keys)
- `Backup_CreateBackup`
- `Backup_Destination`
- `Backup_Encrypt`
- `Backup_History`
- `Backup_IncludeDocs`
- `Backup_RestoreComplete`
- `Backup_RestoreFromBackup`
- `Backup_Subtitle`
- `Backup_Success`
- `Backup_Title`

### Chat_ group (11 keys)
- `Chat_CopyMessage`
- `Chat_ExportChat`
- `Chat_ModelSelector`
- `Chat_NewConversation`
- `Chat_Placeholder`
- `Chat_Regenerate`
- `Chat_Send`
- `Chat_Stop`
- `Chat_SystemPrompt`
- `Chat_Thinking`
- `Chat_TokenCount`

### Error_ group (4 keys)
- `Error_Generic`
- `Error_Network`
- `Error_NotFound`
- `Error_Permission`

### Nav_ group (22 keys)
- `Nav_Annotations`
- `Nav_AskFiles`
- `Nav_Backup`
- `Nav_BackupRestore`
- `Nav_Chat`
- `Nav_Collections`
- `Nav_Comparison`
- `Nav_Dashboard`
- `Nav_Digest`
- `Nav_HardwareAdvisor`
- `Nav_Inbox`
- `Nav_KnowledgeGraph`
- `Nav_KnowledgeVault`
- `Nav_ModelManager`
- `Nav_PluginManager`
- `Nav_QuickActions`
- `Nav_Search`
- `Nav_Settings`
- `Nav_SyncSettings`
- `Nav_WebImport`
- `Nav_Workflows`
- `Nav_Workspaces`

### Plugin_ group (6 keys)
- `Plugin_Author`
- `Plugin_BatchActions`
- `Plugin_Disable`
- `Plugin_Enable`
- `Plugin_MultiSelect`
- `Plugin_Version`

### Search_ group (9 keys)
- `Search_ClearFilters`
- `Search_Hybrid`
- `Search_Keyword`
- `Search_NoResults`
- `Search_Placeholder`
- `Search_ResultCount`
- `Search_SaveFilter`
- `Search_SavedFilters`
- `Search_Semantic`

### Section_ group (4 keys)
- `Section_Intelligence`
- `Section_Knowledge`
- `Section_Support`
- `Section_System`

### Settings_ group (13 keys)
- `Settings_AIProvider`
- `Settings_About`
- `Settings_Appearance`
- `Settings_ContextWindow`
- `Settings_General`
- `Settings_KeyboardShortcuts`
- `Settings_Language`
- `Settings_LanguageHint`
- `Settings_License`
- `Settings_MaxTokens`
- `Settings_SystemDefault`
- `Settings_Temperature`
- `Settings_Title`

### Status_ group (16 keys)
- `Status_Active`
- `Status_Connected`
- `Status_Disabled`
- `Status_Disconnected`
- `Status_EmptyState`
- `Status_Error`
- `Status_Failed`
- `Status_Indexing`
- `Status_Initializing`
- `Status_Loading`
- `Status_NoResults`
- `Status_Pending`
- `Status_Ready`
- `Status_Saving`
- `Status_Success`
- `Status_Syncing`

### Sync_ group (11 keys)
- `Sync_AutoSync`
- `Sync_Configured`
- `Sync_Conflicts`
- `Sync_EncryptionKey`
- `Sync_FolderPath`
- `Sync_History`
- `Sync_Interval`
- `Sync_LastSync`
- `Sync_NotConfigured`
- `Sync_Settings`
- `Sync_SyncNow`

### Validation_ group (5 keys)
- `Validation_InvalidFormat`
- `Validation_InvalidRange`
- `Validation_Required`
- `Validation_TooLong`
- `Validation_TooShort`

## Deletion scope per group
Each group commit removes the listed keys from **all 7 resw files**:
- `src/AgentX.App/Strings/Resources.resw` (invariant base)
- `src/AgentX.App/Strings/de/Resources.resw`
- `src/AgentX.App/Strings/en-US/Resources.resw`
- `src/AgentX.App/Strings/es/Resources.resw`
- `src/AgentX.App/Strings/fr/Resources.resw`
- `src/AgentX.App/Strings/ja/Resources.resw`
- `src/AgentX.App/Strings/zh-CN/Resources.resw`

Total rows removed per group = keys × 7. Across 13 groups: **138 × 7 = 966 rows**.

Note: The LocaleAudit tool's orphan-count reporting only inspects the 6 locale subdirectories, so the per-locale orphan drop after each group will be exactly the group's key count.

## TODO — Follow-ups
- None required. Extractors (XAML `x:Uid` + C# `GetString`) were sufficient to identify all 138 dead entries. No extractor-coverage gaps were found during this audit.
