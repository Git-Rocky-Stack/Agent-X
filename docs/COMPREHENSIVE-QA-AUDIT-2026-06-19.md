# Agent-X Comprehensive QA Audit

**Date:** 2026-06-19  
**Repository:** `Git-Rocky-Stack/Agent-X`  
**Checkout:** `main` at `b7e9fe76dc915fac2631a58cff0cfe37229ddc3f`  
**Release reviewed:** `v2.1.1`  
**Decision:** **NO-GO**  
**Cross-surface release health:** **38/100** _(at time of audit — see resolution status below)_

> ### ✅ RESOLUTION STATUS — updated 2026-06-21 (v2.1.2)
>
> This audit's **NO-GO** verdict predates the v2.1.2 remediation. **All findings AX-QA-001 through
> AX-QA-016 are now resolved** — see [`../CHANGELOG.md`](../CHANGELOG.md) and [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md).
> In brief: authenticated local API with contained file boundaries and encrypted secrets;
> self-healing + fail-closed migration startup (002/003); state-aware privacy claim (008); vault
> document-reload race fixed (009); the dormant vulnerable SQLite binary removed (010); test
> isolation with the 61 leaked stub files cleaned (011); CI vulnerability/coverage/format gates
> (006/009/012); browser-extension ellipsis + assistive-tech live region (013/015); single-source
> version display (014); the dev-only npm advisory cleared (016); hardened mobile transport with the
> TLS-bypass removed (005); and a **green, CI-gated mobile Android build** (004). The signing-ready
> release pipeline with a provenance gate closes **001/007 in source** — the one remaining action is
> the **signed republish**, which awaits a code-signing certificate.
>
> The findings below are retained verbatim as the historical record of the 2026-06-19 review.

## Executive summary

Agent-X has a strong automated unit baseline, a polished desktop surface, complete localization coverage, and a clean Release build. It is not safe to treat the current public release as remediated or ready for continued distribution.

The main release asset was built before the June 2 security remediation. The public `v2.1.1` installer therefore still contains the unauthenticated wildcard-CORS API, backup ZIP traversal, and plugin path-boundary defects documented as fixed in source. A real upgrade profile also reproduces a migration failure: the runner stamps the entire initial baseline when *any* legacy application table exists, even if required baseline tables are absent. Startup then catches the migration exception and continues to expose the UI, API, and connectors against a partial schema.

The mobile companion is not viable as implemented. Its UI instructs users to connect to the desktop's LAN address, while the desktop listener binds only to loopback. If reachability were widened, the client sends its bearer token and private data over HTTP; if HTTPS is configured, it disables all certificate validation.

Release should remain blocked until the P0 and P1 findings below are fixed, regression-tested, republished under a new patch version, and validated from a clean Windows profile plus a representative legacy-upgrade database.

## Severity summary

| Severity | Count | Release effect |
|---|---:|---|
| P0 blocker | 1 | Public users remain exposed to remediated-in-source security defects |
| P1 critical | 7 | Upgrade reliability, startup ordering, mobile viability/security, release trust |
| P2 high | 6 | Dependency exposure, test confidence, test isolation, UI and repository hygiene |
| P3 medium/low | 2 | Accessibility announcement and dev-only dependency debt |

## Findings

### AX-QA-001 — P0 blocker — Public v2.1.1 installers predate and exclude the security remediation

**Evidence**

- Tag `v2.1.1` points to commit `643951d` (2026-06-01). The local published binary identifies commit `56731d1` in `ProductVersion`.
- The slim and offline installers were produced on June 1. The public GitHub asset was last updated at 2026-06-02 17:28 UTC.
- Security fixes landed later at `bcb1634` on 2026-06-02 22:11 PDT; mobile auth landed at `60aa8fc` seven minutes later.
- Binary inspection: `publish/win-x64/AgentX.Core.dll` contains neither `LocalApiSecurity` nor `ResolveContainedPath`; the current Release build contains both.
- Inspection of the exact shipped source at `56731d1` shows `Access-Control-Allow-Origin: *` with no authorization check, direct `Path.Combine(storagePath, relativePath)` during backup restore, and uncontained plugin assembly/install paths.
- The Inno script packages `publish/win-x64/*` directly (`installer/AgentX-Setup.iss:97`).

**User impact**

Installed v2.1.1 copies can expose document/conversation/search data to arbitrary browser origins while Agent-X is running. Crafted backup/plugin packages retain the previously documented file-boundary risks.

**Required action**

1. Remove or clearly deprecate the current v2.1.1 download immediately.
2. Build a new patch release from current source without `-SkipPublish`.
3. Prove the packaged DLL contains the security types and run live anonymous/authenticated API, malicious-origin CORS, backup traversal, and plugin containment tests against the installed artifact.
4. Publish hashes and an incident/release note explaining that the prior asset did not contain the remediation.

### AX-QA-002 — P1 critical — Legacy upgrade databases can be stamped as complete while baseline tables are missing

**Location:** `src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs:209`, `:409`

`HasApplicationTablesAsync()` returns true when *any* application table exists. `AdoptBaselineAsync()` then unconditionally stamps `_InitialBaseline`. The reproduced database contains 38 application tables but lacks `memories` and `oauth_credentials`; its history says the initial baseline is applied. Later migrations execute `ALTER TABLE memories...` and fail with `SQLite Error 1: 'no such table: memories'`.

The database integrity check itself returns `ok`, so this is migration-history/schema divergence rather than physical corruption.

**Reproduction evidence**

- Two packaged launches logged `Failed to initialize database` at `MigrationRunner.cs:245`.
- The same launches logged `no such table: oauth_credentials` during connector initialization.
- Read-only database inspection confirmed `__EFMigrationsHistory` contains `20260417011607_InitialBaseline`, while both required tables are absent.

**Required action**

Baseline adoption must validate the complete baseline schema or create missing baseline objects before stamping. Add fixtures for every supported pre-migration schema, especially partial schemas from older public versions, and assert both table existence and migration history after upgrade.

### AX-QA-003 — P1 critical — Startup is fire-and-forget and continues after database initialization failure

**Location:** `src/AgentX.App/App.xaml.cs:103`, `:122`, `:191`

`OnLaunched` calls an `async void InitializeCoreServicesAsync()` and immediately constructs the main window. Migration, FTS, API, connectors, AI, feature flags, localization, and theme therefore race the UI and each other. Database migration exceptions are logged and swallowed, after which API and connector initialization continues.

Runtime evidence included:

- theme initialization running before `MainWindow` exists (`InvalidOperationException: MainWindow not initialized`),
- API startup after migration failure,
- connector queries against missing tables,
- UI visible while status still read `Initializing...`.

**Required action**

Make startup awaitable and ordered. A migration failure must enter a blocking recovery/error state; it must not start the API, connectors, or data-backed pages. Add a packaged startup test that asserts ordering and fail-closed behavior.

### AX-QA-004 — P1 critical — The physical mobile companion cannot reach the desktop API

**Location:** `src/AgentX.Core/Services/Api/ApiHostService.cs:117`, `:120`; `src/AgentX.Mobile/Services/AgentXApiClient.cs:43`

The mobile Settings page tells users to enter the desktop's LAN IP. The server binds only to `http://localhost:9846/`. On a physical Android/iOS device, `localhost` is the phone and the desktop's LAN address is not listening.

No mobile project is included in `AgentX.sln`, no CI job builds it, and this workstation lacks the `maui-android` workload, so the mobile code remains compile-unverified.

**Required action**

Choose and document an explicit transport: authenticated TLS on a deliberately configured LAN interface, OS-native pairing/relay, or remove the mobile claim. Add an Android build job and an end-to-end device/emulator reachability test.

### AX-QA-005 — P1 critical — Mobile transport exposes the bearer token or accepts any TLS certificate

**Location:** `src/AgentX.Mobile/Services/AgentXApiClient.cs:23`, `:43`, `:301`

The default and documented LAN URL use HTTP, so the pairing token and private document/search/conversation payloads would traverse the LAN in cleartext. If a user supplies HTTPS, `DangerousAcceptAnyServerCertificateValidator` disables server identity validation and permits trivial interception.

**Required action**

Require HTTPS for non-loopback addresses, validate a pinned/pairing-established certificate, and remove the dangerous validator. Do not broaden the desktop listener until this is fixed.

### AX-QA-006 — P1 critical — CI does not gate the surfaces that failed this audit

Only two workflows exist: desktop/core tests and localization. CI does not:

- build Android or iOS,
- lint/typecheck/build the browser extension,
- run `npm audit` or NuGet vulnerability checks,
- enforce code coverage,
- run `dotnet format --verify-no-changes`,
- publish/install/smoke-test the Windows artifact,
- verify an artifact was built from the release tag/HEAD,
- sign or verify signatures.

This gap directly allowed the public installer to drift behind source security fixes.

### AX-QA-007 — P1 critical — Distributed Windows binaries are unsigned

`Get-AuthenticodeSignature` returned `NotSigned` for both installers and both app executables. This is already listed in `docs/KNOWN-ISSUES.md`, but it remains a release trust and SmartScreen blocker.

**Required action:** sign and timestamp the application and installer in a protected release pipeline, then verify the signature before upload.

### AX-QA-008 — P1 critical — The dashboard makes an unconditional privacy claim contradicted by product behavior

**Location:** `src/AgentX.App/Views/DashboardPage.xaml:1212-1213`

The dashboard states: “All AI processing runs locally... Your data never leaves this machine. No cloud. No exceptions.” Settings explicitly support OpenAI, Anthropic, Brave/Serper web search, Google/Microsoft OAuth, and cloud-optimized routing.

This is a trust/compliance problem, not cosmetic copy. The claim must become state-aware and accurately disclose which selected providers transmit what data.

### AX-QA-009 — P2 high — Core coverage is 46.43% line / 33.15% branch despite 1,877 passing tests

Coverage: 18,904 / 40,707 lines and 3,876 / 11,689 branches.

High-risk main classes with **0%** measured coverage include:

- `ApiHostService`
- `PluginService`
- `WorkflowEngine`
- `SyncService`

`BackupService` is 66.92% line / 40% branch. `MigrationRunner` reports 100% line but only 50% branch and still misses the reproduced partial-baseline upgrade path.

Passing count is therefore not sufficient release confidence. Add integration and branch coverage gates, with security- and migration-critical targets above the repository-wide minimum.

### AX-QA-010 — P2 high — A high-severity SQLite advisory is present in every desktop project

`dotnet list package --vulnerable --include-transitive` reports `SQLitePCLRaw.lib.e_sqlite3 2.1.6` under Core, Tests, and App for [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) / CVE-2025-6965. The advisory describes memory corruption in SQLite before 3.50.2 and currently lists no patched `SQLitePCLRaw.lib.e_sqlite3` package version.

Both `e_sqlite3.dll` and `e_sqlcipher.dll` ship. Runtime module inspection showed Agent-X loads `e_sqlcipher.dll`, not `e_sqlite3.dll`, which lowers immediate exploitability but does not justify shipping the dormant vulnerable native binary. Resolve the duplicate bundle graph or document a verified compensating control.

### AX-QA-011 — P2 high — Tests write temporary workflow artifacts into the real Agent-X profile and leave them behind

**Location:** `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs:1197-1205`; `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs:546-603`, `:665-720`

The unit tests execute production code that writes under `%LOCALAPPDATA%/AgentX/Temp/WorkflowResults`. This QA run created current-dated `Document Review Result...` and `Research Brief Result...` files in the user profile. The tests assert those files exist and do not clean them up.

Inject an artifact/temp-path service and give every test a disposable per-test directory.

### AX-QA-012 — P2 high — Repository formatting gate fails with 69,052 diagnostics across 400 files

`dotnet format AgentX.sln --verify-no-changes --severity info` returned:

- 68,464 `ENDOFLINE` diagnostics
- 588 `WHITESPACE` diagnostics

Root cause: `.editorconfig` requires LF, `.gitattributes` uses `text=auto`, and this Windows checkout has `core.autocrlf=true`, producing CRLF. Align `.gitattributes` and `.editorconfig`, then add a CI formatting gate. Do not mix this mechanical normalization with functional fixes.

### AX-QA-013 — P2 high — Extension recent-item ellipsis does not work

**Location:** `browser-extension/src/popup/popup.css:333-347`

Long recent-clip titles/URLs overlap neighboring content instead of truncating. The spans are inline, so `overflow: hidden` and `text-overflow: ellipsis` do not constrain them. Chromium reproduction is visible in [extension-popup-connected.png](../.gstack/qa-reports/screenshots/extension-popup-connected.png). Script-like title content remained inert, confirming the DOM-based rendering is XSS-safe.

### AX-QA-014 — P2 high — User-facing version metadata is stale and contradictory

- Desktop footer: `Agent-X v1.2.0` (`src/AgentX.App/MainWindow.xaml:500`)
- README “Current version”: v2.1.0 (`README.md:5`)
- Assembly/release: v2.1.1
- Mobile display: v1.0.0

Generate version presentation from one release source rather than hardcoded UI/docs strings.

### AX-QA-015 — P3 medium — Extension feedback is not announced to assistive technology

**Location:** `browser-extension/src/popup/popup.html:41`, `popup.ts:166`

Success/error text is dynamically inserted into a generic `div` with no `role="status"`, `role="alert"`, or `aria-live`. Keyboard controls and visible focus passed, but screen-reader users will not reliably hear pairing/clip results.

### AX-QA-016 — P3 low — One dev-only npm advisory remains

`npm audit` reports low-severity [GHSA-4x5r-pxfx-6jf8](https://github.com/advisories/GHSA-4x5r-pxfx-6jf8) in transitive `@babel/core <= 7.29.0`. `npm audit --omit=dev` is clean, so this does not ship in the extension runtime. Update the toolchain when compatible.

## Validation matrix

| Gate | Result |
|---|---|
| Git state before QA | Clean, `main`, `origin/main` at `b7e9fe7` |
| `git diff --check` | Pass |
| .NET restore | Pass |
| Release x64 solution build | Pass, 0 warnings / 0 errors |
| AgentX.Tests | 1,877 passed / 0 failed / 0 skipped |
| LocaleAudit.Tests | 32 passed / 0 failed / 0 skipped |
| Locale coverage gate | 100% for de, en-US, es, fr, ja, zh-CN; 555/555 each |
| Core coverage | Fail, 46.43% line / 33.15% branch |
| Extension lint/typecheck/build | Pass |
| Extension production dependency audit | Pass, 0 vulnerabilities |
| Extension full dependency audit | 1 low dev-only advisory |
| NuGet vulnerability audit | High advisory present; runtime loads SQLCipher provider |
| Android build | Not run: `maui-android` workload missing |
| iOS build | Not run: requires macOS toolchain |
| Formatting | Fail, 69,052 diagnostics across 400 files |
| Installer signatures | Fail, unsigned |
| Packaged desktop startup | Window reachable; migration/connectors log errors; UI automation enumerated 152 elements |
| Extension Chromium interaction | Connected/offline/pair/clear/clip paths exercised with mocked extension APIs |
| Secret scan | No production high-confidence credential material found; test fixtures contain fake keys |

## Positive observations

- Release x64 build is warning-free.
- All 1,909 executed tests passed with no skips.
- Localization coverage is complete across all six shipping locales.
- Current-source API token generation/constant-time validation, CORS helper, path containment, DPAPI secret storage, bounded web reads, and AES-GCM backup changes are materially stronger than the released artifact.
- The extension uses `textContent`/DOM construction; adversarial recent-clip markup did not execute.
- The dashboard is visually coherent and exposes useful accessible names through Windows UI Automation.
- Extension lint, TypeScript, production webpack build, and production dependency audit pass.

## Release sequence

1. Stop distributing the existing v2.1.1 installer.
2. Fix baseline adoption and make startup fail closed on migration failure.
3. Decide and secure the mobile transport; add Android CI before claiming mobile support.
4. Add artifact provenance, installed-artifact smoke tests, extension/mobile gates, coverage, vulnerability, formatting, and signature verification to CI.
5. Rebuild and sign a new patch version from a clean checkout.
6. Validate clean install, representative legacy upgrades, authenticated API/CORS, backup/plugin hostile inputs, extension pairing/clip, and mobile device pairing.
7. Upload only after hashes, commit provenance, signatures, and release notes agree.

## Scope limitations

- No Ollama model or cloud credentials were used, so model-quality/inference flows were not executed.
- Android could not compile because the required workload is absent; iOS needs macOS.
- The extension popup was exercised in Chromium with mocked `chrome.*` APIs; a fully installed-extension-to-desktop clip remains required.
- The app has no injectable data-root seam. `LOCALAPPDATA` overrides were ignored, so desktop QA was limited to read-only navigation/startup evidence on the existing profile. No destructive user actions were taken.
- The 2.2 GB offline installer was not installed/uninstalled during this pass; its provenance and signature were inspected.

## QA side effects

The desktop launches wrote normal runtime logs/settings to `%LOCALAPPDATA%/AgentX`; the current-source launch generated the first local API token. Unit tests wrote workflow-result temp files into the same profile. These files were not deleted or rolled back because they share the user's application data boundary and should not be modified without an explicit cleanup request.

## Evidence

- [Desktop dashboard](../.gstack/qa-reports/screenshots/desktop-dashboard.png)
- [Extension connected/XSS/overflow state](../.gstack/qa-reports/screenshots/extension-popup-connected.png)
- [Extension keyboard focus state](../.gstack/qa-reports/screenshots/extension-popup-keyboard-focus.png)
- Raw Playwright snapshots, console capture, and Cobertura coverage are under `.gstack/qa-reports/evidence/`.

