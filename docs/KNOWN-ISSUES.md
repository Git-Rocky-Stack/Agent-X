# Known Issues

Non-blocking issues and follow-ups for Agent-X. **None of these prevent the app from
building, passing tests, installing, or running.** They are tracked here for
transparency and future work.

_Last updated: 2026-06-01._

---

## Open

### 1. Installer is unsigned (SmartScreen warning)
The currently distributed `AgentX-Setup-2.1.1-x64.exe` is not Authenticode-signed, so
Windows SmartScreen / Defender shows an **"unknown publisher"** prompt on download and
first run.
- **Impact:** UX/trust friction during install; not a functional defect.
- **Status (AX-QA-001 / AX-QA-007):** the release pipeline is now **sign-ready** —
  `scripts/build-installers.ps1` signs and timestamps the app binaries and installers
  (`-CertificateThumbprint` or `-CertificatePath`), verifies the signatures, enforces a
  provenance gate (aborts if the published Core DLL lacks the security types), and writes
  `SHA256SUMS.txt`. See [`RELEASE-SIGNING.md`](RELEASE-SIGNING.md).
- **Remaining:** acquire an OV (or EV, for instant SmartScreen reputation) code-signing
  certificate, then rebuild + sign + republish a new patch from current source with
  `-RequireSign`. This is the only blocker — it needs the certificate and a release run.

---

## Resolved — 2026-06-01

Issues #2–#9 from the previous register were addressed this session. Summary and evidence:

### 2. Installer exceeded GitHub's per-asset size limit — RESOLVED
The single Inno script (`installer/AgentX-Setup.iss`) now builds **two profiles** via the
`AgentXOffline` flag:
- **SLIM** (`ISCC AgentX-Setup.iss`) — no bundled model (~180 MB), small enough to attach to a
  GitHub Release. The app downloads the model on first run.
- **OFFLINE** (`ISCC /DAgentXOffline=1 …`) — bundles the model (~2 GB), hosted on Cloudflare R2
  via `scripts/publish-offline-installer.ps1` and linked from the release notes.

Reproducible build: `scripts/build-installers.ps1 [-Profiles slim|offline|both]`.

### 3. Uninstall deleted the bundled model — RESOLVED
- SLIM: the model is downloaded on first run to `%LOCALAPPDATA%\AgentX\Models` and is **never
  part of the installer's file set**, so the uninstaller never tracks or removes it.
- OFFLINE: the bundled model file carries Inno's `uninsneveruninstall` flag, so an uninstall
  leaves it in place and a reinstall does not re-extract ~2 GB.

### 4. `setup.exe` had no numeric FileVersion — RESOLVED
Added `VersionInfoVersion=2.1.1` (plus `VersionInfoProductVersion`, company, description,
copyright) to `AgentX-Setup.iss`, so the generated `setup.exe` carries a real Win32 FileVersion.

### 5. Installer created an unused data directory — RESOLVED
Removed the `ForceDirectories(...\AgentX\Data)` call from `CurStepChanged`. The app's database
lives at `%LOCALAPPDATA%\AgentX\agentx.db`; only `Logs\` and `Models\` are created now.

### 6. `licenses` table vs. DropLicensesTable migration — RESOLVED (verified not a runtime defect)
Empirical check: a new regression test
(`MigrationRunnerTests.RunAsync_on_fresh_database_does_not_leave_the_licenses_table`) runs the
real `MigrationRunner` against a fresh database and asserts **zero** `licenses` tables — it
passes. `InitialBaseline` creates the table and the trailing `DropLicensesTable` drops it; the
EF model snapshot no longer defines the entity. The previous register's hypotheses (no-op drop /
snapshot defines it / baseline recreates it) did **not** reproduce. The create-then-drop on a
fresh install is harmless (microseconds on an empty table) and the migration must stay for
upgraders that still hold the table.

### 7. Migration runner logged `created=false` on a fresh install — RESOLVED
`MigrationRunner` now derives `DatabaseCreated` from **actual schema presence** (no
`__EFMigrationsHistory` and no application tables before the run) rather than `CanConnectAsync()`,
which was always true because `EnsureKeyApplied()` pre-creates the empty `.db` file at startup.
A new regression test asserts `DatabaseCreated == true` for a freshly-opened empty database file.

### 8. Belief-conflict acknowledgement was not persisted — RESOLVED
Added `ITemporalIdentityService.AcknowledgeConflictAsync(conflictId)`, which sets
`HasBeenAcknowledged` + `AcknowledgedAt` and calls `SaveChangesAsync`. `DashboardViewModel`'s
command is `async` again and awaits it. A service test proves an acknowledged conflict no longer
returns from `GetBeliefConflictsAsync` after a simulated restart (fresh context).

### 9. GUI/JS smoke tests were manual — RESOLVED
The two skipped Playwright tests in `JsRenderingServiceTests` are now **hermetic** (served from a
local loopback `HttpListener`, no live `example.com`) and `SkippableFact`s: they run wherever a
Playwright browser is installed and skip gracefully otherwise. A new CI workflow
(`.github/workflows/build-test.yml`) builds Core + Tests on Windows, runs
`playwright install chromium`, and executes the full suite — so they are automated on every push.

---

## Testing / Tooling (carried forward)

- **GUI presentation layer** (WinUI XAML rendering) is still verified primarily through ViewModel
  and service tests plus the manual checklist at
  [`docs/a2-smoke-test-checklist.md`](a2-smoke-test-checklist.md). The first-run model-download
  flow (Onboarding Step 3) is exercised by `BuiltInModelBootstrap` unit tests at the service layer;
  the live dialog should get a manual pass before wide distribution.
- `dotnet test` can leave the xUnit host hanging after tests pass (native SQLitePCLRaw / LLamaSharp
  handles) — always pass `--blame-hang-timeout`.
