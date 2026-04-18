# Changelog

All notable changes to Agent-X are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

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

- **C14 Audit Log** was originally targeted at v2.1.0 per the magnum-opus Bedrock scope. Rescoped to **v2.1.5** on 2026-04-17 to let the C13 encryption landing settle before layering an HMAC-chained audit subsystem on top. Plan (`docs/superpowers/plans/2026-04-16-c14-audit-log.md`) and worktree (`.worktrees/c14-audit-log/`) remain ready.

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

[2.1.0-preview.1]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.0-preview.1
[2.0.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.0.0
[1.5.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.5.0
[1.4.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.4.0
[1.3.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.3.0
