# Continuous Integration

This document describes Agent-X's CI gates: what each workflow in `.github/workflows` runs,
when it runs, and how to run the same checks locally. It also tracks the gates against audit
finding **AX-QA-006** ("CI does not gate the surfaces that failed this audit").

## Active workflows

| Workflow | File | Runs on | Gates |
|---|---|---|---|
| Build & Test | `build-test.yml` | Windows | Restores and builds `AgentX.Tests` (Release, x64), then builds the WinUI app `src/AgentX.App` (Release, x64) so XAML, code-behind and composition-root errors outside the test project's linked sources fail the build. Installs Playwright Chromium, runs the full test suite with coverage collection (`--blame-hang-timeout 5m`), and enforces the **coverage gate** (AX-QA-009). Uploads the `.trx` results and the Cobertura report. |
| Format | `format.yml` | Windows | `dotnet format AgentX.sln --verify-no-changes`: whitespace, LF line endings and using-directive order across the whole solution. |
| LocaleAudit | `locale-audit.yml` | Linux | Two jobs. **Locale coverage >= 98%** runs the LocaleAudit tool with `--fail-below 98`. **Locale snapshot tests** runs `tests/LocaleAudit.Tests`, including the translation-mirror guard. |
| Dependency Audit | `dependency-audit.yml` | Windows and Linux | NuGet vulnerable-package scans of `AgentX.sln` and the standalone sample plugin (Windows job) and of `AgentX.Mobile` (Linux job with the `maui-android` workload). Fails on any High or Critical advisory not on the accepted list, or when a scan cannot run. |
| Extension CI | `extension-ci.yml` | Linux | Browser extension: `npm ci`, lint, typecheck, production build, and `npm audit --audit-level=high` over the full dependency tree. |
| Android Build | `android-build.yml` | Linux | Installs the `maui-android` workload and builds `src/AgentX.Mobile` for `net8.0-android` (Release). A blocking gate. |
| Release Provenance | `release-provenance.yml` | Linux | **Release-triggered, not a PR gate.** On a published release (or a manual `workflow_dispatch` for a tag), keyless-signs the release `SHA256SUMS.txt` with `cosign` via GitHub OIDC (no secret), records it in the public Rekor transparency log, and attaches the signature and certificate to the release. A release with no assets at all has nothing to sign and stops there; a release with assets but no manifest fails. See [`RELEASE-SIGNING.md`](RELEASE-SIGNING.md#layer-2---ci-keyless-provenance-cosign--rekor). |

### When each workflow runs

Every gating workflow runs on pull requests and on pushes to `main`, filtered by path:

| Workflow | Paths that trigger it |
|---|---|
| Build & Test | `src/**`, `tests/AgentX.Tests/**`, `Directory.Build.props`, `global.json`, `coverlet.runsettings`, `scripts/check-coverage.ps1`, `.claude/verify-ignore`, the workflow file |
| Format | `**/*.cs`, `**/*.csproj`, `.editorconfig`, `.gitattributes`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, the workflow file |
| LocaleAudit | Pull requests: `src/AgentX.App/**/*.xaml`, `src/**/*.cs`, `src/AgentX.App/Strings/**/*.resw`, `scripts/translations/**`, `tools/LocaleAudit/**`, `tests/LocaleAudit.Tests/**`. Pushes to `main`: every push. |
| Dependency Audit | `**/*.csproj`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, the workflow file, and weekly on Monday at 07:00 UTC |
| Extension CI | `browser-extension/**`, the workflow file |
| Android Build | `src/AgentX.Mobile/**`, `Directory.Build.props`, `global.json`, the workflow file |

### Build & Test

The test project compiles a subset of the app's sources (view models and pure services) into
`AgentX.Tests`, so building the tests alone would miss errors in the rest of
`src/AgentX.App`. The workflow therefore builds `src/AgentX.App/AgentX.App.csproj` as a
separate step before the tests run. The JS-rendering integration tests are `SkippableFact`s;
CI installs Chromium through the `playwright.ps1` script in the build output so they run
instead of being skipped.

The suite includes the structural tests in `tests/AgentX.Tests/CodeQuality`, which read the
source, XAML and scripts instead of running features. They fail on interactive controls with
no Click handler or Command, `[RelayCommand]`s nothing binds, XAML resource keys nothing
defines, keyed resources nothing uses, document processors missing from the composition root,
import picker formats no processor reads, controls a screen reader cannot name, spacing off
the 4-pixel grid, radius values outside the machined stops, banned palette hues, and release
scripts that drift from the app and the installer, among other rules.

### LocaleAudit

The **Locale coverage >= 98%** job builds `tools/LocaleAudit` and runs it over the XAML
`x:Uid`s under `src/AgentX.App`, the C# `GetString` keys under `src`, and the resw files in
`src/AgentX.App/Strings`. It fails if any locale covers less than 98% of the referenced keys,
uploads `locale-audit-report.json`, and posts the per-locale table as a pull-request comment
(skipped for pull requests from forks, whose token is read-only; the comment never decides the
result).

The **Locale snapshot tests** job runs `tests/LocaleAudit.Tests`:

- extractor, resw reader and report tests;
- `PerPageLocaleSnapshotTests`: all six locale folders exist (`de`, `en-US`, `es`, `fr`, `ja`,
  `zh-CN`), every locale meets the 98% floor with no orphan entries and no blank values, and
  every locale carries exactly the en-US key set;
- `LegacyTranslationMirrorTests` (the translation-mirror guard): no key in
  `scripts/translations/{de,es,fr,ja,zh-CN}.json` may be missing from the resw files, so a
  removed string cannot linger in the mirrors as if it were a live translation.

### Dependency Audit

The Windows job restores `AgentX.sln` and `plugins/sample-plugin/SamplePlugin.csproj` (the
sample plugin is outside the solution) and runs
`dotnet list <target> package --vulnerable --include-transitive` for each. The Linux job
installs the `maui-android` workload, restores `src/AgentX.Mobile/AgentX.Mobile.csproj` (also
outside the solution) and scans it the same way. Both upload their reports as artifacts.

## AX-QA-006 gate matrix

| Audit-listed gap | Status | Where / why |
|---|---|---|
| Lint/typecheck/build the browser extension | Added | `extension-ci.yml` |
| `npm audit` (extension) | Added | `extension-ci.yml` - `npm audit --audit-level=high` over the full tree. The earlier `--omit=dev` form audited an empty set, because every extension package is a devDependency, and could never fail. |
| NuGet vulnerability checks | Added | `dependency-audit.yml` - the solution, the sample plugin and `AgentX.Mobile`, with an empty allowlist (see below) |
| Build Android | Added (blocking) | `android-build.yml` installs the `maui-android` workload and builds `src/AgentX.Mobile` (`net8.0-android`) on every change under it, as a hard gate (no `continue-on-error`), so the mobile code, including the AX-QA-005 transport hardening, can no longer drift compile-unverified (AX-QA-004). The project stays out of `AgentX.sln` so the Windows desktop build is unaffected; transport decision in [`MOBILE-TRANSPORT.md`](MOBILE-TRANSPORT.md). |
| Build iOS | Deferred (AX-QA-004) | Requires a macOS runner toolchain. On Linux the mobile project targets `net8.0-android` only. |
| Enforce code coverage | Added | `build-test.yml` collects coverage on the test run and `scripts/check-coverage.ps1` gates it (AX-QA-009). Global floor plus elevated floors for critical namespaces; see [Coverage gate](#coverage-gate-ax-qa-009) below. |
| `dotnet format --verify-no-changes` | Added | `format.yml`. The mechanical normalization (LF via `.gitattributes eol=lf`, whitespace, using order) landed in the same change as the gate, with no functional edits mixed in (AX-QA-012 resolved). The gate runs at **default severity**: formatting and imports only; info-level analyzer refactors (CA1861, IDE0300) are out of scope. |
| Publish / install / smoke-test the Windows artifact | Deferred (AX-QA-001 / 007) | Belongs to the signed release pipeline (`scripts/build-installers.ps1`), which the maintainer runs manually. |
| Verify the artifact was built from the release tag/HEAD (provenance) | In the release pipeline and in CI | `build-installers.ps1` aborts if the freshly published `AgentX.Core.dll` lacks the security types (`LocalApiSecurity`, `ResolveContainedPath`) and records the source commit and `SHA256SUMS.txt` (AX-QA-001). On release, `release-provenance.yml` adds keyless `cosign` provenance over `SHA256SUMS.txt` (GitHub OIDC -> Fulcio -> public Rekor log), so anyone can prove an artifact came from this repository's pipeline. See [`RELEASE-SIGNING.md`](RELEASE-SIGNING.md). |
| Sign / verify signatures | In the release pipeline | `build-installers.ps1` Authenticode-signs and timestamps the app binaries and installers and verifies every signature when a certificate is supplied (`-CertificateThumbprint` / `-CertificatePath`); `-RequireSign` makes an unsigned build a hard error (AX-QA-007). The certificate stays with the maintainer, not in CI. |

## NuGet vulnerability allowlist

`dependency-audit.yml` fails on any **High/Critical** advisory. The allowlist (`$accepted` in
both jobs) is **empty**: there are no accepted exceptions.

**AX-QA-010 (resolved).** The dormant, vulnerable `SQLitePCLRaw.lib.e_sqlite3` 2.1.6
([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) / CVE-2025-6965) was
removed by switching `AgentX.Core` and `AgentX.Tests` off the `Microsoft.Data.Sqlite` /
`Microsoft.EntityFrameworkCore.Sqlite` meta-packages to their `.Core` variants. The meta-packages
pull `SQLitePCLRaw.bundle_e_sqlite3` transitively; the `.Core` packages do not, leaving only
`SQLitePCLRaw.bundle_e_sqlcipher`, the SQLCipher provider the app loads and registers through
`Batteries_V2.Init()`. The Release build output ships `e_sqlcipher.dll` and no `e_sqlite3.dll`.

If a future advisory ever has no available fix, add it to `$accepted` in the workflow and
document it in a table here so the gate keeps enforcing everything else.

## Coverage gate (AX-QA-009)

The audit found that a passing test count is not release confidence: 1,877 tests passed while
high-risk Core services (`ApiHostService`, `PluginService`, `WorkflowEngine`, `SyncService`) sat
at **0%** coverage. The `Build & Test` workflow collects coverage on its test run and
`scripts/check-coverage.ps1` fails the build if any tracked metric drops below its floor. When
it runs in CI it also writes its table to the job summary.

### What is measured

The gate measures **authored `AgentX.Core` code**. `coverlet.runsettings` excludes generated
scaffolding so the denominator is code the team actually writes and can test:

- `[GeneratedCode]` members: source-generated regex and the CommunityToolkit.Mvvm
  `[ObservableProperty]` / `[RelayCommand]` plumbing.
- `[ExcludeFromCodeCoverage]` opt-outs.
- EF Core migration scaffolds (`Data/Migrations/*.cs`, `*.Designer.cs`) and build output under
  `obj`. The **runner** that applies the migrations (`Data/MigrationRunner`) is measured and
  carries an elevated floor.

`[CompilerGenerated]` is intentionally **not** excluded: coverlet maps async state machines,
iterators and lambda closures back to their authored source, so dropping them would hide real
async/LINQ logic. `IncludeTestAssembly` stays `false`, so the WinUI view models compiled into
`AgentX.Tests.dll` are not self-counted; the gate scopes to `AgentX.Core`, as the finding
frames it.

Excluding the generated scaffolding lowers the headline line figure, because that scaffolding
is well covered (on 2026-06-30, for example, 60.56% raw against 58.59% authored). Branch
figures barely move, because the excluded code is line-heavy with almost no branches.

### Floors (the ratchet)

The floors in `scripts/check-coverage.ps1`, each set just below the value measured when it was
last raised, with a small headroom for run-to-run variance:

| Scope | Line floor | Branch floor | Measured when set |
|---|---|---|---|
| Global (`AgentX.Core`, authored) | 65% | 55% | 65.42% / 56.29% (2026-08-24) |
| `AgentX.Core.Services.Security` | 80% | 62% | 82.41% / 66.22% |
| `AgentX.Core.Services.Privacy` | 95% | 85% | 100% / 90.62% |
| `AgentX.Core.Services.OAuth` | 80% | 75% | 82.57% / 77.27% |
| `AgentX.Core.Services.Backup` | 75% | 65% | 79.21% / 70.00% |
| `AgentX.Core.Data.MigrationRunner` | 95% | 85% | 98.11% / 91.67% |

The critical namespaces hold code where a regression is a trust, privacy or data-loss
problem: database keys and DPAPI secret encryption, the privacy disclosure behind the
Dashboard's "no cloud" claim, OAuth tokens for the calendar and email connectors, AES-256-GCM
backup encryption, and the migration runner.

**This is a ratchet.** When coverage rises, raise the matching floor in
`scripts/check-coverage.ps1` in the same change so the gain is protected, and record the
measured value in the comment beside it. **Never lower a floor to turn a red build green; add
tests.** The script also checks its own policy: it fails if a critical floor is below the
global floor, or if a critical namespace no longer matches any measured code (a rename or
move).

### Ratchet history

The four 0%-covered services the audit named (`ApiHostService`, `WorkflowEngine`,
`PluginService`, `SyncService`) are all covered now, and later rounds kept lifting the global
floor. The full narrative of each round is in the comment block of
`scripts/check-coverage.ps1`.

| Date | Round | Global floor after (line / branch) |
|---|---|---|
| 2026-06-20 | Security, Privacy and MigrationRunner baselines | - |
| 2026-06-21 | `ApiHostService` from 0% | - |
| 2026-06-24 | `PluginService` and `WorkflowEngine` from 0% | 42% line |
| 2026-06-27 | `SyncService` from 0%; `OAuth` from 45.18% / 34.55% to 82.57% / 77.27% | 45% / 37% |
| 2026-06-28 | `BackupService`, `DocumentService`, `InboxService`, `ConversationService`, `SemanticMemoryService`, `WorkflowService`, `AutoTagService`, `CollaborationService` (since removed from the code) | 55% / 46% |
| 2026-06-30 | `ConversationBranchService`, `SemanticSearchService`, `ComparisonService` | 58% / 48% |
| 2026-07-03 | `KeywordSearchService`, `TemporalIdentityService`, `LocalLlmProvider` | 62% / 51% |
| 2026-08-24 | `ChunkingService`, `AdaptiveChunkingService`, `DocumentDisplayDto` | 65% / 55% |

Several rounds also fixed latent bugs the new tests exposed, among them an always-throwing
`SemanticMemoryService.GetAllMemoriesAsync`, an inverted BM25 relevance order in
`KeywordSearchService`, and two untranslatable LINQ queries that made every Past Self lookup
in `TemporalIdentityService` throw.

To look for the next gap, read the per-class figures in the Cobertura report: CI uploads it
as the `coverage-cobertura` artifact, and a local run writes it under `TestResults`.

## Running the gates locally

Build (the platform argument is required; a bare `dotnet build` fails on the WinUI project):

```bash
dotnet build -p:Platform=x64
```

Tests with coverage, then the coverage gate (PowerShell 7 for the script):

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --configuration Release -p:Platform=x64 \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings
pwsh scripts/check-coverage.ps1 -CoverageFile TestResults
```

Add `-ReportOnly` to print the table without failing (useful when deciding new floors). Do
not add `--no-build` after editing `src/AgentX.App`: the test assembly compiles app sources,
so a stale build gives a false green.

Only the structural CodeQuality tests:

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~AgentX.Tests.CodeQuality"
```

Formatting (PowerShell; the environment variable lets `dotnet format` load the WinUI project):

```powershell
$env:Platform = 'x64'
dotnet format AgentX.sln --verify-no-changes
```

LocaleAudit, both jobs:

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -c Release -- \
  src/AgentX.App src src/AgentX.App/Strings --output locale-audit-report.json --fail-below 98
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj -c Release
```

NuGet audit (after a restore; repeat for `plugins/sample-plugin/SamplePlugin.csproj` and, with
the `maui-android` workload installed, `src/AgentX.Mobile/AgentX.Mobile.csproj`):

```powershell
$env:Platform = 'x64'
dotnet list AgentX.sln package --vulnerable --include-transitive
```

Browser extension, from `browser-extension/`:

```bash
npm ci
npm run lint
npm run typecheck
npm run build
npm audit --audit-level=high
```

Android companion (on Windows and macOS the project also targets `net8.0-ios`, which needs the
iOS workload; CI builds on Linux, where it targets Android only):

```bash
dotnet workload install maui-android
dotnet build src/AgentX.Mobile/AgentX.Mobile.csproj -c Release
```
