# Known Issues

Non-blocking issues and follow-ups for Agent-X. **None of these prevent the app from
building, passing tests, installing, or running.** They are tracked here for
transparency and future work.

_Last updated: 2026-05-31 (v2.1.1)._

---

## Packaging & Distribution

### 1. Installer is unsigned (SmartScreen warning)
`AgentX-Setup-2.1.1-x64.exe` is not Authenticode-signed, so Windows SmartScreen /
Defender shows an **"unknown publisher"** prompt on download and first run.
- **Impact:** UX/trust friction during install; not a functional defect.
- **Fix:** acquire an OV (or EV, for instant SmartScreen reputation) code-signing
  certificate and sign the setup `.exe` (and ideally the app binaries) in the
  release build.

### 2. Installer exceeds GitHub's per-asset size limit
The bundled installer (~2.07 GB = app + Llama 3.2 3B GGUF model) is larger than
GitHub Releases' **2 GiB per-file limit**, so it cannot be attached to a GitHub
release. Source releases are published instead.
- **Fix options:** host the installer externally (e.g., Cloudflare R2 / a CDN
  release bucket), ship a **model-less installer** that downloads the model on
  first run, or split the asset.

### 3. Uninstall deletes the bundled model
The installer places the 2 GB model under `%LOCALAPPDATA%\AgentX\Models`, so the
Inno uninstaller removes it on uninstall (Inno removes everything it installed).
A reinstall therefore re-extracts ~2 GB.
- **Fix options:** install the model to a path not tracked for uninstall, or copy
  it into place on first run, if persistence across reinstall is desired.

### 4. `setup.exe` has no numeric FileVersion
Inno's `AppVersion` populates the ProductVersion resource but not the Win32
**FileVersion**. Inventory/AV tooling that reads FileVersion sees a blank value.
- **Fix:** add `VersionInfoVersion=2.1.1` to `installer/AgentX-Setup.iss`.

### 5. Installer creates an unused data directory
`CurStepChanged` in the `.iss` creates `%LOCALAPPDATA%\AgentX\Data`, but the app
stores its database at `%LOCALAPPDATA%\AgentX\agentx.db` (the parent directory).
The `Data\` subdirectory is never used.
- **Fix:** remove the `ForceDirectories(...\Data)` call from the `.iss` to avoid
  confusion. Harmless otherwise.

---

## Data Layer

### 6. `licenses` table persists despite the DropLicensesTable migration
On a fresh database the `20260528120000_DropLicensesTable` migration is applied,
yet a `licenses` table is still present in the resulting schema (40 tables total).
This suggests the migration's drop is a no-op, the EF model snapshot still defines
the entity, or the baseline recreates it.
- **Impact:** an unused table in the schema; functionally harmless.
- **Fix:** reconcile the migration / model so the schema matches intent.

### 7. Migration runner logs `created=false` on a fresh install
After the v2.1.1 fix, a genuinely fresh install logs
`Migration runner: ... created=false applied=<all 11 migrations>`. The
`created=false` is **cosmetic**: the empty `.db` file already exists (created when
`EnsureKeyApplied()` opens the connection) before the runner inspects it, so
`databaseExistedBefore` is `true`. The `applied=<all>` list reflects the true
outcome (full schema created).
- **Fix (optional):** derive `DatabaseCreated` from schema presence rather than
  `CanConnectAsync()` for accurate reporting.

---

## Application

### 8. Belief-conflict acknowledgement is not persisted
`DashboardViewModel.AcknowledgeConflictAsync` sets
`OriginalConflict.HasBeenAcknowledged = true` in memory and removes the item from
the displayed collection, but **never calls `SaveChangesAsync`** — and there is no
acknowledge method on `ITemporalIdentityService`. Because `GetBeliefConflictsAsync`
filters `!HasBeenAcknowledged` at the database level, **acknowledged conflicts
reappear after an app restart**.
- **Fix:** add a service method that persists the acknowledgement
  (`HasBeenAcknowledged` + `AcknowledgedAt`, then `SaveChangesAsync`) and `await`
  it from the view model (the command can return to being `async` at that point).

---

## Testing / Tooling

### 9. GUI smoke tests are manual
Two Playwright/GUI tests are skipped in automated runs (manual execution). The
WinUI presentation layer is otherwise exercised through ViewModel tests. Run the
manual checklist at [`docs/a2-smoke-test-checklist.md`](a2-smoke-test-checklist.md)
before wide distribution.
