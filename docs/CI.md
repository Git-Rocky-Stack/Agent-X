# Continuous Integration

This document tracks Agent-X's CI gates and their status against audit finding **AX-QA-006**
("CI does not gate the surfaces that failed this audit").

## Active workflows

| Workflow | File | Gates |
|---|---|---|
| Build & Test | `.github/workflows/build-test.yml` | Restores, builds `AgentX.Core` + `AgentX.Tests` (Release, x64), installs Playwright Chromium, runs the full unit-test suite, **collects code coverage and enforces the coverage gate** (AX-QA-009). |
| LocaleAudit | `.github/workflows/locale-audit.yml` | Localization coverage ≥ 98% per locale + locale snapshot tests. |
| Extension CI | `.github/workflows/extension-ci.yml` | Browser-extension lint, typecheck, production build, and production-dependency `npm audit`. |
| Dependency Audit | `.github/workflows/dependency-audit.yml` | NuGet vulnerable-package scan across the solution; fails on any High/Critical advisory not on the accepted list. Runs on dependency changes and weekly. |
| Format | `.github/workflows/format.yml` | `dotnet format --verify-no-changes` across the solution: whitespace, LF line endings, and using-directive ordering. Fails if the code is not formatted. Runs on `.cs`/`.csproj`/`.editorconfig`/`.gitattributes`/build-config changes. |
| Release Provenance | `.github/workflows/release-provenance.yml` | **Release-triggered** (not a PR gate). On a published release (or manual `workflow_dispatch`), keyless-signs the release `SHA256SUMS.txt` with `cosign` via GitHub OIDC (no secret), logs it to the public Rekor transparency log, and attaches the signature + certificate to the release. See [`RELEASE-SIGNING.md`](RELEASE-SIGNING.md#layer-2--ci-keyless-provenance-cosign--rekor). |

## AX-QA-006 gate matrix

| Audit-listed gap | Status | Where / why |
|---|---|---|
| Lint/typecheck/build the browser extension | ✅ Added | `extension-ci.yml` |
| `npm audit` (extension) | ✅ Added | `extension-ci.yml` — `npm audit --omit=dev` (production deps; dev-only advisory is AX-QA-016) |
| NuGet vulnerability checks | ✅ Added | `dependency-audit.yml` — allowlist gate (see below) |
| Build Android | ✅ Green (blocking) | `android-build.yml` — installs the `maui-android` workload and builds `src/AgentX.Mobile` (`net8.0-android`) on every change under it, as a **hard gate** (no `continue-on-error`). Verified green on the hosted Linux runner (and locally in Debug + Release, 0 warnings), so the mobile code — including the AX-QA-005 transport hardening — can no longer drift compile-unverified (AX-QA-004). The project stays out of `AgentX.sln` so the Windows desktop build is unaffected; transport decision in [`MOBILE-TRANSPORT.md`](MOBILE-TRANSPORT.md). |
| Build iOS | ⏸ Deferred → **AX-QA-004** | Requires a macOS runner toolchain. Enable when a macOS runner is available. |
| Enforce code coverage | ✅ Added | `build-test.yml` collects coverage on the existing test run and `scripts/check-coverage.ps1` gates it (AX-QA-009). Global floor plus elevated floors for security/migration namespaces — see [Coverage gate](#coverage-gate-ax-qa-009) below. |
| `dotnet format --verify-no-changes` | ✅ Added | `format.yml`. The mechanical normalization (LF via `.gitattributes eol=lf`, whitespace, using order) landed in the same change as the gate, with no functional edits mixed in (AX-QA-012 resolved). Gate runs at **default severity** — formatting + imports only; info-level analyzer refactors (CA1861, IDE0300) are intentionally out of scope. |
| Publish / install / smoke-test the Windows artifact | ⏸ Deferred → **AX-QA-001 / 007** | Belongs to the signed release pipeline (`scripts/build-installers.ps1`), which the maintainer runs manually. |
| Verify the artifact was built from the release tag/HEAD (provenance) | ✅ In pipeline **+ CI** | `build-installers.ps1` aborts if the freshly published `AgentX.Core.dll` lacks the security types (`LocalApiSecurity`, `ResolveContainedPath`) and records the source commit + `SHA256SUMS.txt` (AX-QA-001). On release, `release-provenance.yml` adds keyless `cosign` provenance over `SHA256SUMS.txt` (GitHub OIDC → Fulcio → public Rekor log), so anyone can prove an artifact came from this repo's pipeline. See [`RELEASE-SIGNING.md`](RELEASE-SIGNING.md). |
| Sign / verify signatures | ✅ In pipeline | `build-installers.ps1` Authenticode-signs + timestamps the app binaries and installers and verifies every signature when a certificate is supplied (`-CertificateThumbprint` / `-CertificatePath`); `-RequireSign` makes an unsigned build a hard error (AX-QA-007). The certificate stays with the maintainer, not in CI. |

## NuGet vulnerability allowlist

`dependency-audit.yml` fails on any **High/Critical** advisory. The allowlist (`$accepted` in the
workflow) is currently **empty** — there are no accepted exceptions.

**AX-QA-010 (resolved).** The dormant, vulnerable `SQLitePCLRaw.lib.e_sqlite3` 2.1.6
([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) / CVE-2025-6965) was
removed by switching `AgentX.Core` and `AgentX.Tests` off the `Microsoft.Data.Sqlite` /
`Microsoft.EntityFrameworkCore.Sqlite` meta-packages to their `.Core` variants. The meta-packages
pull `SQLitePCLRaw.bundle_e_sqlite3` transitively; the `.Core` packages do not, leaving only
`SQLitePCLRaw.bundle_e_sqlcipher` — the SQLCipher provider the app actually loads and registers via
`Batteries_V2.Init()`. The Release build output now ships `e_sqlcipher.dll` and no longer ships
`e_sqlite3.dll`, and `dotnet list package --vulnerable --include-transitive` reports zero vulnerable
packages across the solution.

If a future advisory ever has no available fix, add it to `$accepted` in the workflow and document
it in a table here so the gate keeps enforcing everything else.

## Coverage gate (AX-QA-009)

The audit found that a passing test count is not release confidence: 1,877 tests passed while
high-risk Core services (`ApiHostService`, `PluginService`, `WorkflowEngine`, `SyncService`) sat
at **0%** coverage. The `Build & Test` workflow now collects coverage on its existing test run and
`scripts/check-coverage.ps1` fails the build if any tracked metric drops below its floor.

### What is measured

The gate measures **authored `AgentX.Core` code**. `coverlet.runsettings` excludes generated
scaffolding so the denominator is code the team actually writes and can test:

- `[GeneratedCode]` members — source-generated regex and the CommunityToolkit.Mvvm
  `[ObservableProperty]` / `[RelayCommand]` plumbing.
- `[ExcludeFromCodeCoverage]` opt-outs.
- EF Core migration scaffolds (`Data/Migrations/*.cs`, `*.Designer.cs`). The **runner** that applies
  them (`Data/MigrationRunner`) is measured and carries an elevated floor.

`[CompilerGenerated]` is intentionally **not** excluded — coverlet maps async state machines,
iterators, and lambda closures back to their authored source, so dropping them would hide real
async/LINQ logic. `IncludeTestAssembly` stays `false`, so the linked WinUI ViewModels hosted in
`AgentX.Tests.dll` are not self-counted; the gate scopes to `AgentX.Core`, as the finding frames it.

> Excluding the well-covered generated scaffolding lowers the *headline* line number from the raw
> 51.63% to **46.39%** because that scaffolding is well-covered and inflates the raw figure. ~46% is
> the honest authored-code baseline; branch (38.6%) tracks the audit's 33.15% closely. The raw and
> authored *branch* rates are effectively identical — authored branch (38.6%) in fact edges out raw
> branch (38.47%) — because the excluded scaffolding is line-heavy but has almost no branches, so it
> inflates only the line figure.

### Floors (the ratchet)

Floors are set just below the measured baseline with a small headroom for CI variance. On
**2026-06-27** `OAuth` coverage was lifted from 45.18% / 34.55% to **82.57% line / 77.27% branch** (an
in-process `HttpListener` stub server makes the real token / refresh / revocation HTTP paths testable
despite the service's non-injectable `HttpClient`, and reflection covers the token-exchange, persist,
scope-building and PKCE internals walled behind `AuthorizeAsync`'s `Process.Start` browser launch).
`OAuth` was the binding critical namespace on **both** metrics, so lifting it **unpins the global
floor**: the global **line** floor rises **44% → 45%** and the global **branch** floor **33% → 37%**,
now bounded by the global *measured* value (46.39% / 38.6%) rather than by OAuth. The lowest critical
floors are now `Security`'s (line 80%, branch 62%), both far above the global floor — so the global
floor is free to ratchet on overall measured coverage from here. Earlier **2026-06-27** rounds:
`SyncService` (global line → 44%); the **2026-06-24** `PluginService`/`WorkflowEngine` rounds (global
line 41% → 42%) and the **2026-06-21** `ApiHostService` ratchet preceded those. The
Security/Privacy/MigrationRunner baselines are from **2026-06-20**. Every critical floor sits **at or
above** the repository-wide minimum, as AX-QA-009 requires.

| Scope | Line floor | Branch floor | Measured (baseline) |
|---|---|---|---|
| Global (`AgentX.Core`, authored) | 45% | 37% | 46.39% / 38.6% |
| `AgentX.Core.Services.Security` | 80% | 62% | 82.41% / 66.22% |
| `AgentX.Core.Services.Privacy` | 95% | 85% | 100% / 90.62% |
| `AgentX.Core.Services.OAuth` | 80% | 75% | 82.57% / 77.27% |
| `AgentX.Core.Data.MigrationRunner` | 95% | 85% | 98.11% / 91.67% |

**This is a ratchet.** When coverage rises, raise the matching floor in `scripts/check-coverage.ps1`
in the same change so the gain is protected. **Never lower a floor to turn a red build green — add
tests.** The script self-checks that no critical floor drops below the global floor, and that every
critical namespace still matches measured code (it fails loudly if a namespace is renamed or moved).

### Raising overall coverage

The gate prevents regression; it does not by itself lift the 0%-covered services the audit named.
**All four are now closed**: **`ApiHostService`** (0% → full line coverage, 2026-06-21),
**`WorkflowEngine`** (0% → 99.57% line / 81.16% branch, 2026-06-24), **`PluginService`**
(0% → 95.41% line / 92.39% branch, 2026-06-24), and **`SyncService`** (0% → 96.31% line / 98.61%
branch, 2026-06-27) — the global floor was ratcheted up as each landed. With those done, **`OAuth`**
— the namespace that was pinning the global floor — was then lifted **45.18% → 82.57% line and
34.55% → 77.27% branch** (2026-06-27), unpinning both global floors. The only sizeable uncovered
region left in `OAuth` is `AuthorizeAsync`'s browser-launch + local-callback body, which cannot run in
CI. The next lever for the global floor is now overall measured coverage itself (46.39% line / 38.6%
branch): no single critical namespace caps it — any authored-code coverage gain can raise it.

### Running it locally

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --configuration Release -p:Platform=x64 \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings
pwsh scripts/check-coverage.ps1 -CoverageFile TestResults
```

Add `-ReportOnly` to print the table without failing (useful when deciding new floors).
