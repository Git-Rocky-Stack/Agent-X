# Changelog

All notable changes to Agent-X are documented in this file.

---

## Documentation Release (2026-05-03)

### Added — Comprehensive User Documentation Suite (20+ files, 8,000+ lines)

**Getting Started & Onboarding**
- `docs/user-guide/getting-started/quick-start.md` — 10-minute setup walkthrough for new users
- Covers installation, first launch, passphrase creation, document import, AI chat, and semantic search

**Reference Documentation**
- `docs/user-guide/faq.md` — 100+ frequently asked questions covering installation, features, AI, search, performance, privacy, licensing, and troubleshooting
- `docs/user-guide/troubleshooting.md` — Solutions to common issues organized by category with advanced diagnostics section
- `docs/user-guide/glossary.md` — 100+ term glossary with definitions covering all Agent-X terminology
- `docs/user-guide/keyboard-shortcuts.md` — Comprehensive power user navigation guide with platform-specific notes

**Templates & Scenarios**
- `docs/user-guide/templates/README.md` — Templates overview with usage guide
- `docs/user-guide/templates/document-templates.md` — Project Brief, Meeting Notes, Research Summary, Technical Spec, Code Review templates
- `docs/user-guide/templates/chat-templates.md` — Summarize, Compare, Extract, Research, Technical chat templates
- `docs/user-guide/scenarios/README.md` — Real-world scenarios: Research Paper Analysis, Meeting Intelligence, Code Review Assistant, Document Migration, Personal Knowledge Base

**Video Tutorial Scripts**
- `docs/user-guide/video-scripts/README.md` — Scripts for 5 videos: Quick Start, Advanced RAG, Knowledge Graph, Workflows, GPU Acceleration

**AI Discovery & Indexing**
- `docs/llms.txt` — AI-optimized documentation index for LLM consumption with quick links and key concepts
- `docs/long-llms.txt` — Extended AI reference with comprehensive details on architecture, features, configuration, and best practices

### Updated
- **README.md** — Added comprehensive documentation section with links to all new user guide resources
- Documentation organized under `docs/user-guide/` with clear categorization

### Documentation Statistics
- **Total Files Created:** 20+
- **Total Lines:** 8,000+
- **Categories:** 7 (Getting Started, Reference, Templates, Scenarios, Video Scripts, AI Discovery, Enhanced)
- **Topics Covered:** 100+ glossary terms, 100+ FAQ items, 5 real-world scenarios, 10+ templates, 5 video scripts

---

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed - Code-quality audit: unreachable features and inert controls (2026-08-23)

A full-codebase audit for stubs, placeholder data, and unwired modules. Several features were
fully implemented and tested in their view models but had no way to reach them from the running
app: the control existed but invoked nothing, or no control existed at all. These are now wired,
and four structural tests keep the whole class of defect from returning.

- **The chat model picker did nothing.** The header ComboBox listed available models but never
  reported the selection back, so choosing a model silently kept the previous one. Selecting a
  model now activates and persists it.
- **Smart Inbox triage was inert.** Accept, Defer, and Reject rendered with tooltips and
  accessible names but had no handler, so the queue could not be worked. The status filters had
  the same problem. All four are now wired.
- **Annotations could not be edited or deleted.** The delete button invoked nothing and the whole
  edit flow (`EditAnnotation` / `SaveAnnotation` / `CancelEdit`) had no control anywhere in the
  page. Added an edit panel and wired delete and the color filters.
- **Workflow steps could not be reordered or removed.** Move Up, Move Down, and Remove Step were
  all inert.
- **Backup history could not be restored from a row.** The per-backup restore button had no
  handler; it now runs through the same confirmation gate as the page-level restore.
- **Collections could not be renamed.** `RenameCollection` looked the collection up, logged
  "rename requested", and returned without calling the service. It now persists the new name.
- **Bulk operations had no UI.** Multi-select, Select All, and bulk delete for collections, and
  bulk enable / disable / uninstall for plugins, were implemented and tested but unreachable.
  Both pages gain a multi-select mode with per-row checkboxes and a bulk action bar.
- **Jump-To discarded what you picked.** Selecting a specific document or conversation opened the
  generic Knowledge Vault or Chat page instead of that item. Navigation now carries a payload, and
  the target pages honour it.
- **Dashboard search discarded the query.** Typing in the dashboard search box and submitting
  landed on an empty Search page. The box now submits on Enter and carries the query through.
- **The Auto-Sync toggle did not stop the sync loop.** Switching it off changed a flag while the
  background loop kept running, so the control reported a state it did not enforce.
- **Belief conflicts could not be acknowledged.** The dashboard surfaced conflicts with no way to
  dismiss them, so an acknowledged conflict reappeared on every launch.
- Conversation export gained the routes its view model already implemented: **Copy as Markdown**
  in the export dialog and **Export all conversations** from the chat sidebar. Collections gained
  a per-collection export.
- Backup size estimate can be recalculated, and sync history can be reloaded, without leaving the page.

### Fixed - Honest status reporting

- The dashboard reported **100% indexed / "Idle"** when the indexing query threw, presenting a
  healthy reading for a state it never observed. It now reports "Status unavailable".
- The Past Self voice profile showed a **15-word average sentence length and a "Balanced" style
  with zero samples captured** — invented statistics indistinguishable from real measurements. An
  empty profile now says so.

### Fixed - Correctness and accessibility

- Citation file-path lookup in Ask Your Files blocked on an async call with `Task.Wait()` /
  `Task.Result` inside the answer pipeline, risking a UI-thread deadlock. It is now awaited.
- The Database Encryption section of Settings used WinUI's default typography instead of the
  Command Console type styles used by every other section on the page.
- **Every visible interactive control now has an accessible name.** 68 controls across 13 files
  exposed none, so a screen reader announced them as unlabelled buttons with nothing to tell
  them apart. WinUI only derives a name from string `Content`, and these hold an icon plus a
  label, or an icon alone.
  - 57 icon-plus-label controls now point at the label they already render
    (`AutomationProperties.LabeledBy`), and 4 date pickers point at their own captions. This
    reuses the existing localized text, so no string was duplicated into resources.
  - 24 icon-only controls take their name from the tooltip they already carry, mirrored into
    `AutomationProperties.Name` across all six locales. UIA exposes a tooltip as help text
    rather than as the name, so the tooltip alone never reached a screen reader as an identity.
  - 3 controls with neither a label nor a tooltip received newly translated names, one of which
    (the notification dismiss button) also had a hardcoded English tooltip.
  - A UI Automation sweep of all 29 pages reports 296 named controls and **0 unnamed controls
    that are visible**; the 7 that remain unnamed sit in collapsed panels that were never
    realized during the sweep, and all of them satisfy the static guard below.

### Removed - Dead code

- `WorkspaceService` (821 lines): a complete parallel multi-workspace subsystem with its own
  JSON metadata store and per-workspace database, never registered, never referenced, and never
  tested. It duplicated no live behaviour — the shipped feature is `WorkspaceProfileService`.
- Six unreachable duplicate commands whose behaviour is already provided by a working path:
  chat regenerate and research-mode toggles, two chat export commands superseded by the export
  dialog, a workflow export command that discarded its own result, and a sync folder picker whose
  event nothing subscribed to.

### Added - Structural guard tests

- `NoUnwiredInteractiveControlsTests` fails when a button, menu item, or similar control ships
  with no Click handler, Command binding, or flyout. WinUI silently absorbs presses on such a
  control, so nothing else catches it.
- `NoUnreachableViewModelCommandsTests` fails when a `[RelayCommand]` cannot be invoked from any
  view or code path — implemented, covered by tests, and still unreachable for the user.
- `NoUndefinedXamlResourceKeysTests` fails when XAML references a `StaticResource` or
  `ThemeResource` key nothing defines. These resolve at page realization, so a bad key builds
  clean and throws the first time a user opens the page.
- `InteractiveControlsHaveAccessibleNamesTests` fails when a control has no name for assistive
  technology. Explicitly disabled controls are exempt, since they are never focusable.

Two reusable scripts came out of this: `scripts/name-unlabelled-controls.py` (points controls
at their own visible label) and `scripts/mirror-tooltips-to-automation-names.py` (copies an
existing translated tooltip into the automation name).

### Changed - Command Console redesign (2026-07-05)

The entire UI adopts the **Command Console design system** from the Strategia family, defined in [`DESIGN.md`](DESIGN.md) (the new source of truth for all visual work):

- **Armed red `#AA2024`** replaces the cardinal `#C41E3A` accent across all 29 views, with a full LED status vocabulary (go green, hold amber, warn red, no-go red, scope teal) replacing ad-hoc status colors.
- **Four bundled typefaces** (SIL OFL, no runtime downloads): Public Sans for body text, Archivo Expanded for stencil placards and page titles, Departure Mono for telemetry readouts, Iosevka Term for streams and code (retiring Cascadia Code).
- **Hardware depth**: pages are built from faceplate modules with machined kickers and corner bolts; metrics render in recessed LCD wells with phosphor glow; buttons are machined and armed caps with physical press travel.
- **Instrument strip**: the bottom status bar is reborn as a live instrument row - `MDL` model lamp and readout, `IDX` embedding-queue and `VAULT` document-count LCDs, the `LOCAL`/`NET` privacy lamp, and a new annunciator cluster (`INBOX`/`SYNC`/`JOBS`/`BAK`) fed by a typed aggregation service. Lit lamps navigate to their source page; blinking lamps acknowledge on click.
- **Three themes**: dark (Night Shift, default), light (Day Shift, brushed silver), and high contrast (bound to system colors, exempt from the hardware skin). Display surfaces stay dark in both regular themes by design. Status text tones are shift-aware for contrast on light surfaces.
- **Reduced motion respected**: lamp strikes, meter ballistics, and cap travel snap instantly when Windows animations are disabled.

### Added

- All UI now available in six languages (en-US, de, es, fr, ja, zh-CN): every page, dialog, MainWindow chrome, and the Privacy/Terms pages are x:Uid-instrumented with full resw key parity, enforced by the LocaleAudit CI gate (2026-07-04).
- `SegmentMeter` control: a 12-segment instrument meter with zone tones and needle ballistics, driving the Dashboard indexing gauge and the Hardware Advisor RAM gauge.

### Fixed

- **Theme choice now persists across restarts.** The theme was saved under a key that matched no settings property, so every launch silently reverted to dark.
- **Model Manager connection dot and Sync Settings state dot now reflect live state.** Both were initialized green and never updated, showing "go" even when disconnected or errored.
- Quick Actions page could fail to open due to a tab handler firing during XAML parse (2026-07-04).
- Databases with a stamped baseline but missing tables now self-heal at startup instead of bricking the app (2026-07-04).

## [2.1.2] — 2026-06-21 — "Bedrock" security & supply-chain hardening

Security and hardening release. Closes the full **Codex security audit** and the **Comprehensive QA Audit (2026-06-19)** — every finding **AX-QA-001 through AX-QA-016** — makes the release pipeline signing-ready, and brings the mobile companion to a verified build. No breaking changes; no database schema changes.

### Security

- **Local REST API now requires authentication.** The desktop API (Enhancement #16) previously served unauthenticated loopback requests; it now enforces a bearer token, and the mobile companion authenticates against it (Codex audit).
- **File-access boundaries hardened.** Local-API path handling is contained to approved roots (`ResolveContainedPath`), closing directory-traversal exposure (Codex audit).
- **Secrets encrypted at rest** rather than stored in plaintext configuration (Codex audit).
- **Mobile transport hardened (AX-QA-005).** Removed the `DangerousAcceptAnyServerCertificateValidator` TLS bypass; plaintext HTTP is refused to any non-loopback host; added an optional pairing-established SPKI-SHA-256 certificate pin with constant-time comparison. See [`docs/MOBILE-TRANSPORT.md`](docs/MOBILE-TRANSPORT.md).
- **Dormant vulnerable SQLite binary removed (AX-QA-010).** Switched `AgentX.Core` and the test project to the `Microsoft.Data.Sqlite.Core` / `Microsoft.EntityFrameworkCore.Sqlite.Core` packages, so the unmaintained `e_sqlite3` native binary (GHSA-2m69-gcr7-jv3q) no longer ships; the app continues to run on the SQLCipher provider (`bundle_e_sqlcipher`). The dependency-audit allowlist is now empty.

### Fixed

- **Fresh-install / partial-baseline self-heal (AX-QA-002, AX-QA-003).** The migration runner detects and repairs a partially-stamped baseline, and startup is now fail-closed so a half-initialised database can no longer surface a broken UI; closed a dashboard-load-vs-migration race via `IStartupGate`.
- **Dashboard privacy claim is state-aware (AX-QA-008).** The "no cloud" assurance reflects the actual provider state through `IPrivacyStatusService` instead of being hard-coded.
- **Knowledge-vault document-reload race eliminated (AX-QA-009)** in `KnowledgeVaultViewModel`.
- **Mobile Android build is green (AX-QA-004).** The MAUI companion now builds clean for `net8.0-android` (Debug **and** Release, 0 warnings) and is a **blocking** CI gate. It had been compile-unverified due to a missing Android platform head (now scaffolded: manifest, `MainActivity`/`MainApplication`, icon/splash) and a wrong-API call in `MauiProgram.cs` — a non-existent parameterless `UseMaui()`, corrected to the canonical `UseMauiApp<App>()`.
- **Single-source version display (AX-QA-014).** The dashboard footer, Settings page, and backup manifest now read one assembly-backed version (`AppVersionInfo`) instead of three drifting hard-coded strings.
- **Browser extension (AX-QA-013, AX-QA-015).** Long recent-clip titles/URLs truncate with an ellipsis; the feedback area is an ARIA live region announced to assistive technology (escalating to `assertive` for errors).

### Added / Changed — release engineering

- **Signing-ready installer pipeline with provenance gate (AX-QA-001, AX-QA-007).** `scripts/build-installers.ps1` Authenticode-signs and RFC-3161 timestamps the app binary plus both installers, verifies the signatures, writes `SHA256SUMS.txt`, and **aborts if the published `AgentX.Core.dll` lacks the security types** — the exact regression that shipped in the public v2.1.1 asset (built from stale source). See [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md).
- **Keyless supply-chain provenance in CI (cosign + Rekor).** New `.github/workflows/release-provenance.yml` signs the release `SHA256SUMS.txt` with [Sigstore](https://www.sigstore.dev/) `cosign` using GitHub's ambient OIDC identity — **no secret, no long-lived key** — records it in the public **Rekor** transparency log, and attaches the signature + ephemeral certificate to the release. Signing the manifest transitively covers both the SLIM (GitHub) and OFFLINE (R2) installers. This is the second of a two-layer model (local Authenticode + CI keyless provenance); end users can verify origin with `cosign verify-blob` — see [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md#verifying-a-download).
- **Free OSS code-signing path documented.** [`docs/SIGNPATH-APPLICATION.md`](docs/SIGNPATH-APPLICATION.md) is the application + canonical record for free Authenticode signing via the [SignPath Foundation](https://signpath.org/), removing the need for a paid certificate to clear the Windows SmartScreen "Unknown Publisher" warning.
- **CI vulnerability + quality gates (AX-QA-006, AX-QA-009, AX-QA-012, AX-QA-016).** Browser-extension and NuGet vulnerability gating; `AgentX.Core` coverage floors; a repository format gate; and removal of the unused React ESLint plugins that were the sole importers of the `@babel/core` dev advisory (`npm audit --omit=dev`: 0 vulnerabilities).
- **Android build CI (AX-QA-004).** New `.github/workflows/android-build.yml` compiles `src/AgentX.Mobile` (`net8.0-android`) on every change under it; iOS is conditioned out on Linux and deferred (needs a macOS runner).
- **Test isolation (AX-QA-011).** Workflow tests no longer write into the real user profile; removed 61 leaked `WorkflowResults` profile stub files.
- **GitHub Actions runtime** bumped off the deprecated Node 20 runtime.
- `Directory.Build.props` `<Version>` bumped `2.1.1` → `2.1.2` (single source; `AppVersionInfo` flows it to every surface).

---

## [2.1.1] — 2026-05-31 — "Bedrock" fresh-install fix

Patch release. Fixes a **critical fresh-install defect** found during full installer validation: on a brand-new machine the database came up empty (no tables) and every feature failed with `no such table: documents/memories/user_settings/...`.

### Fixed

- **Fresh installs now build the full database schema.** At startup `EnsureKeyApplied()` opens the SQLite connection (to apply the SQLCipher PRAGMA) before the migration runner, which creates an empty `agentx.db` file. The runner then saw `CanConnectAsync() == true`, mistook the empty file for a pre-migration install, ran baseline adoption — which *stamps* the baseline as applied **without creating tables** — and `MigrateAsync` skipped schema creation. Baseline adoption is now gated on the database actually containing application tables, so an empty database flows through `MigrateAsync` and receives the full schema. Verified end-to-end via a clean install → launch → uninstall: all 11 migrations apply, 40 tables created, zero `no such table` errors.
- Added a `MigrationRunner` regression test that opens the connection before running the runner, reproducing the real startup sequence (the prior fresh-DB test never did, which is why the defect slipped through).
- Zeroed out all 13 Release build analyzer warnings at the root (nullable annotations, an unused `async`, and test-only Moq/null-handling) — the build is now warning-free.

### Changed

- `Directory.Build.props` `<Version>` bumped `2.1.0` → `2.1.1`.
- Installer `AgentX-Setup.iss` `MinVersion` raised `10.0.18362` → `10.0.19041` to match the app's `TargetPlatformMinVersion` (older builds would install but fail to launch).

---

## [2.1.0] — 2026-05-30 — "Bedrock"

Final v2.1.0 release. Promotes the `2.1.0-preview.1` data-layer slice to a stable release and completes the v2.1 scope. Full notes: [`docs/v2.1.0-RELEASE-NOTES.md`](docs/v2.1.0-RELEASE-NOTES.md).

### Added

- **A1 Multi-Language UI** — six shipping locales (de / en-US / es / fr / ja / zh-CN) with CLDR pluralization, RTL-ready `FlowDirection`, a `LocaleAudit.Tool` CI gate (≥98% coverage), and per-page snapshot tests
- **A2 Keyboard-First Power Mode** — fuzzy Command Palette, Jump-To navigation, and a page-scoped shortcut Cheatsheet
- **B9 EF Core migrations** and **C13 SQLCipher at-rest encryption** promoted from preview to stable

### Changed

- **In-app User Guide localization completed** — every `UserGuide_*` string is now natively translated across all five non-English locales (de / es / fr / ja / zh-CN), replacing the prior English placeholders; stale placeholder headers removed
- `Directory.Build.props` `<Version>` bumped `2.1.0-preview.1` → `2.1.0`

### Fixed

- Documented previously-silent `JsonException` handling in HTML/PDF citation export
- Corrected stale calendar-sync and Serper knowledge-graph comments; removed dead internal-path references from release notes

### Rescoped

- **C14 Audit Log** remains targeted at **v2.1.5** — a Phase 2 Memory prerequisite that ships before Memory regardless of v2.1.5 timing

---

## [2.1.0-preview.1] — 2026-04-17 — "Bedrock" data-layer hardening

Pre-release shipping the data-layer slice of the v2.1 Bedrock hardening stream. Ships on `phase1-bedrock` at commit `e4bb5ce`. Full release notes: [`docs/v2.1.0-preview.1-RELEASE-NOTES.md`](docs/v2.1.0-preview.1-RELEASE-NOTES.md).

### Added

- **B9 EF Core migration runner** — `IMigrationRunner` + `MigrationRunner` with pending-migration API, `MigrationResult`, `PendingMigrationsException`, `AgentXDbContextFactory` for design-time tooling, `InitialBaseline` migration capturing current schema, and baseline-adoption for pre-migration installs
- **C13 SQLCipher at-rest encryption** — `SQLitePCLRaw.bundle_e_sqlcipher` provider, `IDatabaseKeyService` with DPAPI-wrap and UserPassphrase (PBKDF2-HMAC-SHA256, 600k iterations) modes, `IEncryptedConnectionFactory` applying `PRAGMA key` on every `SqliteConnection`, `IDatabaseEncryptionMigrator` using `sqlcipher_export` for atomic plaintext→encrypted conversion with rollback
- **C13 Settings UI** for database encryption enable flow (tier-aware: Ultimate passphrase dialog, others transparent enable)
- **C13 Startup unlock flow** using `IEncryptionStateFile` marker to break the unlock ↔ migration chicken-and-egg
- **`InvalidDatabaseKeyException`** with SQLite ErrorCode-26 / "file is not a database" detection for wrong-passphrase recovery loops
- **Out-of-DB key storage** at `%LocalAppData%\AgentX\encryption.info.json` — separates encryption state from the encrypted vault so startup unlock has no DB dependency (C13 hotfix, merged 2026-04-17)

### Changed

- `EnsureCreatedAsync` and the manual `ALTER TABLE inbox_items` block are **removed** from app startup and replaced by `IMigrationRunner.RunAsync()` — all schema changes now flow through EF migrations
- All 5 production `SqliteConnection` creation sites route through `IEncryptedConnectionFactory` for uniform key application
- DI registrations for `DatabaseKeyProvider`, `EncryptedConnectionFactory`, and `DatabaseKeyService` are singletons (data-plane crypto lifetime invariant)
- `Directory.Build.props` `<Version>` bumped `2.0.0` → `2.1.0-preview.1`; added `AssemblyVersion`, `FileVersion`, and `InformationalVersion`

### Rescoped

- **C14 Audit Log** was originally targeted at v2.1.0 per the magnum-opus Bedrock scope. Rescoped to **v2.1.5** on 2026-04-17 to let the C13 encryption landing settle before layering an HMAC-chained audit subsystem on top. It remains a Phase 2 Memory prerequisite and will ship before Memory regardless of v2.1.5 timing.

### Security

- SQLCipher 4 AES-256-CBC at-rest encryption (opt-in, tier-aware)
- DPAPI-wrapped key material never written to the encrypted DB
- PBKDF2-HMAC-SHA256 (600,000 iterations) for passphrase mode, exceeding OWASP 2023 recommendation (600k)
- `.plain.bak` retained only through the atomic-swap critical section during encryption enable

### Known Issues

- Test-host shutdown hang (`H1`) — native library handles from SQLitePCLRaw / Whisper.Net / LLamaSharp prevent clean xUnit host exit. Tests pass in ~6s; CI uses `blame-hang-timeout=30s`.
- `dotnet ef migrations add` workflow requires `-p:CopyLocalLockFileAssemblies=true` pre-build followed by `--no-build` invocation (net8.0-windows TFM runtime-pack limitation).

---

## [2.0.0] — 2026-04-16 — "Enrichment" calendar/email/ide-awareness

Shipped to `main` via PR #1 (merge commit `99bf449`) on branch `feature/calendar-email-integration`.

### Added

- **Feature 9 HNSW ANN vector scaling** — Hierarchical Navigable Small World index for the vector store, with core, factory, and test coverage
- **Feature 10 Calendar + Email integration** — OAuth2 + PKCE + CSRF state infrastructure (`IOAuthService`), Google Calendar + Outlook Calendar providers, Gmail + Outlook Email providers, `CalendarPlugin` + `EmailPlugin` with `IPlugin` lifecycle, sync services with delta tokens, Settings UI with connect/disconnect, full search pipeline integration
- **Feature 12 Screen-awareness with IDE detection** — `IdeWindowDetector`, `ScreenContextResult.IdeContext`, Quick Chat prompt integration
- **`PluginType.DataConnector`** and scoped `IPluginContext` for plugin OAuth access
- OAuth settings, Calendar settings, Email settings in `AppSettings` + `OAuthProviderRegistry`

### Fixed

- OAuth `TokenResponse` deserialization (`[JsonPropertyName]` for snake_case OAuth2 fields)
- `OAuthService.BuildAuthorizationUrl` query string (manual URL escape via `List<KeyValuePair>` + `Uri.EscapeDataString` instead of `Dictionary.ToString()`)
- OAuth hardening — CSRF state parameter, PKCE code challenge, 5-minute HttpListener timeout, `IDisposable` lifecycle
- Cross-arch build — `RuntimeIdentifier` now derives from `Platform` so x86/x64/ARM64 builds work

### Changed

- Bumped `Directory.Build.props` `<Version>` to `2.0.0` (commit `2a4d5cc`)

---

## [1.5.0] — 2026-04-14 — "Expansion"

See [`docs/v1.5.0-RELEASE-NOTES.md`](docs/v1.5.0-RELEASE-NOTES.md).

Added: Web Content Ingestion Depth, Conversation Branching, Export Format Expansion, Deep Research Mode.

## [1.4.0] — 2026-04-14 — "Foundation + First Expansion"

See [`docs/v1.4.0-RELEASE-NOTES.md`](docs/v1.4.0-RELEASE-NOTES.md).

Added: DPAPI API Key Encryption, System Tray + Global Hotkey, Browser Extension, Multi-Model Routing.

## [1.3.0] — 2026-04-12

See [`docs/v1.3.0-RELEASE-NOTES.md`](docs/v1.3.0-RELEASE-NOTES.md).

Added: Workspace Profiles, Smart Inbox, Comparative Analysis, Voice Input, Plugin API, Collaborative Sync.

---

[2.1.2]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.2
[2.1.1]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1
[2.1.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.0
[2.1.0-preview.1]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.0-preview.1
[2.0.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.0.0
[1.5.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.5.0
[1.4.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.4.0
[1.3.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.3.0
