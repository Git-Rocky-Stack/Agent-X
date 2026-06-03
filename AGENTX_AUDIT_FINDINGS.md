# Agent-X Audit Findings

Date: 2026-06-02

Scope: Practical repository audit of the Agent-X solution, with emphasis on runtime entry points, local API exposure, plugin/package handling, backup restore, credential storage, web import, browser extension checks, and verification status.

## Executive Summary

The highest-risk issue is the automatically started local REST API. It listens on `localhost:9846`, exposes user-data endpoints, has no authentication, and returns wildcard CORS headers. That combination lets arbitrary websites issue browser requests to the local Agent-X API and read responses if Agent-X is running.

Two file-boundary issues are also material: backup restore can write outside the configured storage directory through crafted ZIP entries, and plugin installation/activation relies on weak manifest path validation around plugin IDs and entry assemblies. Both should be fixed with strict validation plus full-path containment checks.

Extension TypeScript linting and type checking passed after installing dependencies. .NET tests could not be run because the repository pins .NET SDK `8.0.421` in `global.json`, while the local machine only reported SDK `10.0.300`.

## Remediation Status — RESOLVED (2026-06-02)

All five findings, the npm audit vulnerabilities, and the backup authenticated-encryption hardening from "Additional Notes" have been remediated and verified. The .NET 8 SDK (`8.0.421`) is present at `C:\Users\Rock\.dotnet`, so the full test suite now runs.

| # | Finding | Status | Key changes | Tests |
|---|---------|--------|-------------|-------|
| 1 | Critical — unauthenticated API + wildcard CORS | ✅ Resolved | Per-install DPAPI-encrypted bearer token (`LocalApiSecurity`, `AppSettings.LocalApiToken`); all routes except `/api/extension/health` require `Authorization: Bearer` (constant-time compare, fail-closed); CORS now echoes the origin only for `chrome-extension://`/`moz-extension://` (web origins get no ACAO); `LocalApiEnabled` toggle gates startup; extension pairs via paste-to-token (popup) and sends Bearer; desktop Settings → Connections shows/copies/regenerates the token | `LocalApiSecurityTests`, `ApiHostLifecycleServiceTests` |
| 2 | High — backup ZIP path traversal | ✅ Resolved | `PathHelper.ResolveContainedPath`/`IsSafeRelativeEntry` containment guard; restore loop + `ValidateBackupAsync` reject traversal/rooted entries; per-entry + total expansion caps (zip-bomb) | `BackupServiceSecurityTests`, `PathHelperContainmentTests` |
| 3 | High — weak plugin manifest/path validation | ✅ Resolved | `PluginManifestValidator` wired into `InstallPluginAsync` + strict reverse-DNS ID (rejects `..`/separators/reserved names) and bare-`.dll` entry assembly; containment guards on install path, activation load, and uninstall delete | `PluginManifestValidatorSecurityTests`, existing `PluginManifestValidatorTests` (unchanged green) |
| 4 | Medium — WebSearchApiKey stored in plaintext | ✅ Resolved | Routed through `EncryptIfNotEmpty` + decrypt/auto-migrate like other secrets | `SettingsServiceEncryptionTests` |
| 5 | Med/Low — web-fetch size cap bypass | ✅ Resolved | Bounded streaming read enforces the 10 MB cap even when `Content-Length` is absent/dishonest | `WebContentFetcherSizeLimitTests` |
| — | Additional Notes — backup tamper detection | ✅ Resolved | Backups now use AES-256-GCM authenticated encryption (V2); legacy AES-256-CBC archives still restore | `BackupServiceSecurityTests` (round-trip / tamper / legacy) |
| — | Extension npm audit (4 vulns) | ✅ Resolved | `npm audit fix` + `copy-webpack-plugin@^14`; `npm audit` reports 0 vulnerabilities; lint/typecheck/build green | n/a |

**Verification:** `AgentX.Tests` 1875 passed / 2 skipped / 0 failed; `LocaleAudit.Tests` 32 passed; `AgentX.App` builds Release|x64 with 0 warnings / 0 errors; extension typecheck + lint + production build pass; `npm audit` clean.

## Findings

### 1. Critical: Local REST API Is Unauthenticated And Open To Any Browser Origin

Affected files:

- `src/AgentX.App/App.xaml.cs`
- `src/AgentX.App/Services/ApiHostLifecycleService.cs`
- `src/AgentX.Core/Services/Api/ApiHostService.cs`
- `browser-extension/manifest.json`
- `browser-extension/src/api/agentx-api.ts`

Evidence:

- The API lifecycle service is registered and started automatically during app initialization in `App.xaml.cs`.
- `ApiHostLifecycleService` starts the API on stable port `9846`.
- `ApiHostService` exposes routes for documents, conversations, collections, search, inbox clipping, and extension health.
- `WriteCorsHeaders` sets `Access-Control-Allow-Origin: *`.
- There is no bearer token, shared secret, origin check, extension handshake, CSRF token, or request authentication on the API routes.

Impact:

If Agent-X is running, any website opened in the user's browser can attempt requests to `http://localhost:9846`. Because the service returns permissive CORS headers, the website can read JSON responses. This can expose document metadata, conversation metadata, collection metadata, search snippets, and possibly allow unsolicited inbox clips.

Recommended fix:

- Generate a per-install random API token and store it under DPAPI-protected settings.
- Require the token on all non-health local API routes, for example via `Authorization: Bearer <token>` or a custom header.
- Give the browser extension the token through an explicit pairing/setup flow rather than hardcoding.
- Replace wildcard CORS with a narrow allowlist. For extension traffic, validate the extension origin or use Chrome native messaging instead of open CORS.
- Consider keeping `/api/extension/health` unauthenticated only if it returns no sensitive metadata.
- Consider making the API opt-in instead of starting it automatically for all users.

Suggested tests:

- Anonymous requests to `/api/documents`, `/api/conversations`, `/api/search`, and `/api/inbox/clip` are rejected.
- Authenticated extension requests succeed.
- Requests with an arbitrary `Origin` do not receive readable CORS responses.

### 2. High: Backup Restore Allows ZIP Path Traversal For Document Entries

Affected file:

- `src/AgentX.Core/Services/Backup/BackupService.cs`

Evidence:

- `ValidateBackupAsync` only verifies that the archive contains `database/agentx.db` and `manifest.json`.
- During restore, entries beginning with `documents/` are accepted.
- The relative path after the `documents/` prefix is combined directly with `settings.StoragePath`.
- There is no `Path.GetFullPath` containment check before writing.

Impact:

A crafted backup archive can include entries like `documents/../outside.txt` or equivalent platform-specific traversal forms. On restore, Agent-X may write files outside the intended storage directory. Depending on the selected storage location and user permissions, this could overwrite arbitrary user files.

Recommended fix:

- Normalize the base storage path with `Path.GetFullPath`.
- For each document entry:
  - reject rooted paths,
  - reject empty paths,
  - reject paths containing `..` after normalization,
  - normalize the final target path,
  - require the final target path to start with the normalized storage directory plus a path separator.
- Add explicit validation for document entries during `ValidateBackupAsync`, not only during extraction.
- Consider limiting restored document file sizes and total expanded size to reduce archive-bomb risk.

Suggested tests:

- Restore rejects `documents/../evil.txt`.
- Restore rejects `documents/subdir/../../evil.txt`.
- Restore rejects rooted Windows paths if they appear in ZIP entry names.
- Restore accepts normal nested document paths and writes only under `StoragePath`.

### 3. High: Plugin Manifest Path Validation Is Too Weak Around Install Paths And Entry Assemblies

Affected files:

- `src/AgentX.Core/Services/Plugins/PluginService.cs`
- `src/AgentX.Core/Validation/PluginManifestValidator.cs`
- `src/AgentX.Core/Services/Plugins/PluginManifest.cs`

Evidence:

- `InstallPluginAsync` reads a manifest and calls a private `ValidateManifest` that only checks required fields are non-empty.
- The stricter `PluginManifestValidator` exists but is not used by `PluginService.InstallPluginAsync`.
- `GetPluginInstallPath` uses `SanitizeDirectorySegment`, but that method replaces invalid filename characters only. It does not reject path-control values like `..`.
- `UninstallPluginAsync` recursively deletes `entity.InstallPath`.
- `GetEntryAssemblyName` trusts manifest `entryAssembly` when present.
- `EnablePluginAsync` combines `entity.InstallPath` and `entryAssembly` and loads the resulting DLL path without proving it remains inside the plugin directory.

Impact:

Plugin packages are executable code, so their installation and activation paths are a high-trust boundary. Weak manifest validation can lead to installation into unexpected directories, loading assemblies outside the plugin directory, or recursive deletion of unexpected paths if a bad install path is stored.

Recommended fix:

- Use `PluginManifestValidator` inside `InstallPluginAsync`, and expand it.
- Require plugin IDs to match a strict reverse-DNS allowlist, for example `^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+$`.
- Reject IDs containing path separators, `..`, leading/trailing dots, reserved device names, or control characters.
- Require `entryAssembly` to be a single file name:
  - `Path.GetFileName(entryAssembly) == entryAssembly`
  - no directory separators,
  - `.dll` extension,
  - no rooted path.
- Before extraction, activation, and deletion, compute full paths and prove they are contained under the plugin base directory.
- Consider storing the validated entry assembly name in the database at install time so activation does not need to re-trust a mutable on-disk manifest.

Suggested tests:

- Install rejects plugin ID `..`.
- Install rejects plugin ID containing `/` or `\`.
- Install rejects entry assembly `..\Other.dll`.
- Activation refuses to load a DLL outside the plugin install directory.
- Uninstall refuses to delete an install path outside the plugin base directory.

### 4. Medium: Web Search API Key Is Not DPAPI-Protected On Disk

Affected files:

- `src/AgentX.Core/Services/Settings/AppSettings.cs`
- `src/AgentX.Core/Services/Settings/SettingsService.cs`

Evidence:

- `AppSettings` includes `WebSearchApiKey`.
- `SettingsService.SaveSettingsAsync` encrypts `OpenAiApiKey`, `AnthropicApiKey`, and OAuth client secrets before writing settings to disk.
- `WebSearchApiKey` is not passed through `EncryptIfNotEmpty`.
- `GetSettingsAsync` decrypts OpenAI, Anthropic, and OAuth secrets, but not `WebSearchApiKey`.

Impact:

If a user configures a Brave or Serper web search key, it is stored in plaintext in `%LocalAppData%\AgentX\settings.json`, unlike the other provider secrets.

Recommended fix:

- Encrypt `WebSearchApiKey` in `SaveSettingsAsync`.
- Decrypt it in `GetSettingsAsync`.
- Auto-migrate legacy plaintext values the same way OpenAI and Anthropic keys are migrated.

Suggested tests:

- Saving settings writes an encrypted `webSearchApiKey`.
- Loading settings returns the plaintext web search key to callers.
- Legacy plaintext web search keys are migrated to encrypted form on load.

### 5. Medium/Low: Web Fetch Size Limit Can Be Bypassed When Content-Length Is Missing

Affected file:

- `src/AgentX.Core/Services/Web/WebContentFetcher.cs`

Evidence:

- `FetchHtmlInternalAsync` checks `response.Content.Headers.ContentLength`.
- If the header is absent, the code reads the entire response via `ReadAsStringAsync`.
- The documented limit is 10 MB, but it is only enforced when the server provides an accurate `Content-Length`.

Impact:

A server can omit `Content-Length` and stream a response larger than the intended 10 MB cap. This can cause excessive memory use or degraded responsiveness during web import.

Recommended fix:

- Read the response stream incrementally.
- Count bytes while reading.
- Abort once `MaxContentLengthBytes` is exceeded.
- Decode to string only after the bounded byte buffer is complete.

Suggested tests:

- A response without `Content-Length` but above 10 MB is rejected.
- A response with `Content-Length` above 10 MB is rejected before reading the body.
- A response below the cap still succeeds.

## Additional Notes

### Browser Extension

The extension is scoped to `http://localhost:9846/*` in `manifest.json`, uses DOM APIs rather than `innerHTML` in popup rendering, and passed lint/typecheck. Its client assumes the local API is trusted and unauthenticated. That is acceptable only if the desktop API adds a pairing/authentication layer and does not rely on CORS alone.

### Plugin Runtime Trust Model

Plugins are loaded as .NET assemblies and can execute code in-process. Even with a scoped `IServiceProvider`, .NET code loaded in-process is not a sandbox. Permission declarations in the manifest should be treated as UI/consent metadata, not as enforcement, unless the host moves plugin execution into a separate constrained process.

### Backup Encryption

Backup encryption uses AES-CBC with PBKDF2. The sync package codec uses AES-GCM, which provides authenticated encryption. Consider moving backup archives to authenticated encryption as well, or add an HMAC over the encrypted payload, so tampering is detected reliably before restore.

## Verification Performed

Commands run:

```powershell
npm ci
npm run lint
npm run typecheck
npm audit --audit-level=moderate
dotnet test AgentX.sln --no-restore
git status --short
```

Results:

- `npm ci` completed and installed browser-extension dependencies.
- `npm run lint` passed.
- `npm run typecheck` passed.
- `npm audit --audit-level=moderate` failed with 4 reported dependency vulnerabilities:
  - `brace-expansion`, moderate severity
  - `fast-uri`, high severity
  - `serialize-javascript`, high severity
  - `copy-webpack-plugin` depends on vulnerable `serialize-javascript`
- `dotnet test AgentX.sln --no-restore` could not run because `global.json` requires .NET SDK `8.0.421`, while the installed SDK list only reported `10.0.300`.
- `git status --short` was clean after the audit commands.

## Recommended Remediation Order

1. Lock down the local REST API with per-install authentication and restrictive CORS.
2. Fix backup restore path containment.
3. Harden plugin manifest validation, activation path handling, and uninstall containment.
4. Encrypt `WebSearchApiKey` consistently with the other provider secrets.
5. Enforce streamed response size limits for web import.
6. Address extension npm audit findings.
7. Install .NET SDK `8.0.421` or update `global.json` intentionally, then run the full .NET test suite.
