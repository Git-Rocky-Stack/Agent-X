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
> 59.59% to **57.40%** because that scaffolding is well-covered and inflates the raw figure. ~57% is
> the honest authored-code baseline; branch (47.85%) tracks the audit's 33.15% closely. The raw and
> authored *branch* rates are effectively identical — authored branch (47.85%) in fact edges out raw
> branch (47.08%) — because the excluded scaffolding is line-heavy but has almost no branches, so it
> inflates only the line figure.

### Floors (the ratchet)

Floors are set just below the measured baseline with a small headroom for CI variance. On
**2026-06-30** `SemanticSearchService` — the next-largest authored gap at **412 measurable lines,
previously 0%** — was lifted to **97.33% line / 93.00% branch** (401 / 412 lines, 93 / 100 branches). It is
the semantic-search pipeline: embed the query, run a vector ANN search, enrich hits with EF-Core document
metadata, filter by embedding-model version + collection / file-type / date, build display excerpts, and
sort / truncate to TopK — plus the search-history and saved-filter CRUD. It composes three mockable
collaborators over a real `AgentXDbContext`: `IEmbeddingService` (query embedding + `ModelVersion` for the
compatibility gate), `IVectorStore` (the ANN search), and an optional `IRagConfiguration` (retrieval
multiplier / cap; absent → built-in fallbacks 3 / 500). Vector hits are seeded by `Distance` because
`VectorSearchResult.Similarity` is the computed inverse `1 - Distance`; the version gate compares each
chunk's `EmbeddingModelVersion` to the service's `ModelVersion` (null / empty = legacy-compatible). The
three OCE-rethrow-vs-generic-swallow arms (embed / vector / chunk-load) are covered by a throwing mock for
the cancellation path and a generic-exception mock (or a disposed context) for the swallow path; a
pre-canceled token drives the chunk-load OCE rethrow. It is **not** a trust boundary, so it is not a
critical namespace — its gain raised the **global** floor **line 56% → 57%** (measured 57.40%) **and
branch 46% → 47%** (measured 47.85%), comfortably locking the branch headroom the prior
`ConversationBranchService` round could not. Its small residual is a handful of unreachable defensive
log / guard arms. *(Sidebar: `Backup` line wobbled to 75.9% again under the larger suite — still above its
75% floor, but a recurring thin margin; its residual is the deliberately-uncovered restore-swap body, so
more Backup tests cannot safely raise it. Watch it; never lower the floor.)*

Earlier the same day, `ConversationBranchService` — the next-largest authored gap at **421 measurable lines,
previously 0%** — was lifted to **98.34% line / 96.97% branch** (414 / 421 lines, 64 / 66 branches). It is
the EF-Core engine for conversation branching: forking a conversation at a message (copying all messages up
to the branch point with token / count aggregation), branch-tree and root-walk queries, cross-conversation
message merge, branch counting / existence checks, and recursive branch deletion. Two model facts shaped the
harness: the self-referencing `ParentConversation → Branches` FK is `DeleteBehavior.Restrict` — so a
**non-recursive** delete of a branch that still has children is rejected by the database
(`DbUpdateException`) and recursive deletes must remove the deepest descendant first — while
`Messages → Conversation` is `Cascade`. The injected `IConversationService` is null-guarded by the
constructor but consumed by no method, so it is supplied as a bare mock; the logger flows through
`logger.ForContext<T>()`, so a real silent Serilog logger is supplied. The generic `catch (Exception)`
rethrow arms are reached with a pre-canceled token (an `OperationCanceledException` bypasses the earlier
`catch (InvalidOperationException)` arms), the circular-reference `maxDepth` guard via a self-parent row,
and the orphaned-parent walk-stop via a raw FK-off `DELETE` of the parent. It is **not** a trust boundary,
so it is not a critical namespace — its gain raised the **global** floor **line 55% → 56%** (measured
56.36%), while the **branch** floor is **held at 46%** (measured 47.03% — a 47% floor would leave only
0.03 pt of headroom, inside the run-to-run async-branch variance band this file repeatedly observes). Its
small residual is `LoadEntireTreeAsync`'s two safety-limit arms (an unreachable `root is null` guard and
the 100-level BFS runaway guard).

Earlier, on **2026-06-28** `CollaborationService` — the next-largest authored gap at **431 measurable lines,
previously 0%** — was lifted to **84.22% line / 81.03% branch** (363 / 431 lines). It is a lightweight
real-time collaboration hub built on `HttpListener` + `HttpClient` (no EF, no external runtime libs). Its
public `StartHostingAsync` binds the strong-wildcard prefix `http://+:{port}/`, which needs an elevated
URL-ACL reservation and cannot run unprivileged in CI; the harness therefore injects a non-privileged
`http://localhost:{port}/` listener into the service's own fields and runs its real `RunListenerLoopAsync`
via reflection, so the production request handlers (session / heartbeat / events / sessions) are exercised
over real HTTP — the same "drive the service's own loop against a localhost prefix" pattern the OAuth
HTTP-flow and ApiHost suites use. The session / presence / event / query surface is tested directly, and
the timer callbacks (`PruneExpiredSessions` / `SendHeartbeat`) are invoked by reflection. It is **not** a
trust boundary, so it is not a critical namespace — its gain raised the **global** floor **line 54% → 55%**
and **branch 45% → 46%**, bounded by the global *measured* value (55.26% / 46.47%). The residual is
`StartHostingAsync`'s `+`-prefix bind body (needs admin) and a couple of fire-and-forget timer lambdas.

Earlier the same day, three of the largest remaining authored gaps were closed together — all driven by the
same in-memory-SQLite harness over the real `AgentXDbContext`, with the AI / embedding / config / feature-
flag collaborators mocked, deterministic length-4 embedding vectors for exact cosine similarity, and a
real silent Serilog logger (each constructor consumes `logger.ForContext<T>()`, so a loose mock's null
return would read as a missing logger):

- **`SemanticMemoryService`** (468 measurable lines, **previously 0%**) → **97.86% line / 94.48% branch** —
  the embedding-based semantic memory store with associative links and temporal decay (retrieve / rank /
  extract-from-conversation / link / feedback / dismiss).
- **`WorkflowService`** (601 lines, **previously 22.7%**) → **90.35% line / 88.68% branch** — the workflow /
  step CRUD, JSON import / export, run-history projection, and built-in-template seeding surface.
- **`AutoTagService`** (473 lines, **0%**) → **86.26% line / 85.19% branch** — AI tag generation (with a
  ChatAsync JSON / delimiter fallback) plus manual tag CRUD; real temp files exercise the file-read content
  fallback.

None is a trust boundary, so none is tracked as a critical namespace; the combined gain flows straight into
the global denominator, raising the **global** floor **line 51% → 54%** and **branch 42% → 45%**, bounded by
the global *measured* value (54.33% / 45.64%). This round also fixed a latent always-throws bug in
`SemanticMemoryService.GetAllMemoriesAsync` (its `OrderBy` evaluated the un-translatable
`GetEffectiveImportance`, so EF threw `InvalidOperationException` on **every** call) by materialising the
active set before the in-memory sort, and added three `OAuth` constructor null-guard tests (branch
**74.55% → 78.18%**) after the larger suite perturbed async-branch scheduling toward the unchanged 75%
OAuth floor — the guard branches restore deterministic headroom without moving the floor.

Before those, `ConversationService` — the largest remaining authored gap at **424 measurable lines,
previously 0%** — was lifted to **98.82% line / 100.00% branch** (419 / 424 lines). It is the EF-Core
CRUD surface for chat conversations and messages (create / query / search / rename / pin / archive /
delete, message add / delete / edit / truncate with conversation-metadata bookkeeping, token + count
stats, folder / tag organization); an in-memory-SQLite harness drives the real `AgentXDbContext` with
mocked optional post-write hooks (`IConversationRecallService` / `IConversationSummaryService`) and a
real silent Serilog logger (the constructor consumes `logger.ForContext<T>()`, so a loose mock's null
return would read as a missing logger). One consolidated disposed-context test rethrows through every
method's log-and-rethrow catch arm. It is **not** a trust boundary (no crypto, no migrations), so it is
not tracked as a critical namespace — its gain flows straight into the global denominator, raising the
**global** floor: **line 50% → 51%** and **branch 41% → 42%**, bounded by the global *measured* value
(51.74% / 42.51%).

Before that, `InboxService` — the largest authored gap at that point (**438 measurable lines,
previously 0%**) — was lifted to **97.95% line / 96.00% branch** (429 / 438 lines): the Smart-Inbox
triage queue over EF Core, with an in-memory-SQLite harness mocking `ICollectionService` / `IAiService`
(token-streamed through a hand-rolled `IAsyncEnumerable`) / `IDocumentService`, real-temp-file ingestion
and preview reads, and disposed-context tests for each catch arm. Also not a trust boundary, it took the
global floor **line 48% → 50%** and **branch 40% → 41%**.

Earlier still the same day, `DocumentService` — the largest authored gap at that point (**452 measurable
lines, previously 0%**) — was lifted to **91.15% line / 69.36% branch** (412 / 452 lines): the
knowledge-vault ingestion orchestrator over EF Core, driven by an in-memory-SQLite harness with a
deterministic `IDocumentProcessor` stub and real-temp-file import I/O. Also not a trust boundary, it
took the global floor **line 47% → 48%** and **branch 38% → 40%**; its small residual is the per-file
MIME table's rarely-hit arms and a few defensive-log branches.

And first that day, `BackupService` coverage was lifted from **15.10%** to **78.66% line / 70.00%
branch**: a full create-backup round-trip is made safe by mocking the injectable
`IEncryptedConnectionFactory` to redirect the SQLite Online-Backup *source* copy to a seeded throwaway
database (never the real user DB) and honour the generated temp *destination*, with reflection covering
the `PeriodicTimer`-gated retention helper. Because `BackupService` performs AES-256-GCM authenticated
encryption of the entire user database, its namespace is tracked as **critical** (floor **75% line /
65% branch**; measured 79.21% / 70.00%). The residual uncovered region is `RestoreFromBackupAsync`'s
database-swap body — it writes the extracted database to the hardcoded real user-profile path with no
injection seam, so exercising it would clobber the live database — plus the `PeriodicTimer`-gated
scheduled-loop body.

Earlier: on **2026-06-27** `OAuth` coverage was lifted from 45.18% / 34.55% to **82.57% line / 77.27%
branch** (an in-process `HttpListener` stub server makes the real token / refresh / revocation HTTP
paths testable despite the service's non-injectable `HttpClient`, and reflection covers the internals
walled behind `AuthorizeAsync`'s `Process.Start` browser launch); `OAuth` had been the binding critical
namespace on both metrics, so that **unpinned** the global floor (line 44% → 45%, branch 33% → 37%). The
**2026-06-27** `SyncService` round took the global line floor → 44%; the **2026-06-24**
`PluginService`/`WorkflowEngine` rounds took global line 41% → 42%; the **2026-06-21** `ApiHostService`
ratchet preceded those. The Security/Privacy/MigrationRunner baselines are from **2026-06-20**. Every
critical floor sits **at or above** the repository-wide minimum, as AX-QA-009 requires.

| Scope | Line floor | Branch floor | Measured (baseline) |
|---|---|---|---|
| Global (`AgentX.Core`, authored) | 57% | 47% | 57.40% / 47.85% |
| `AgentX.Core.Services.Security` | 80% | 62% | 82.41% / 66.22% |
| `AgentX.Core.Services.Privacy` | 95% | 85% | 100% / 90.62% |
| `AgentX.Core.Services.OAuth` | 80% | 75% | 82.57% / 77.27% |
| `AgentX.Core.Services.Backup` | 75% | 65% | 79.21% / 70.00% |
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
— the namespace that had been pinning the global floor — was lifted **45.18% → 82.57% line and
34.55% → 77.27% branch** (2026-06-27), unpinning both global floors. Then, with the global floor free
to ratchet on overall coverage, the largest remaining authored gaps were attacked on
**2026-06-28**. First **`BackupService`** (then the single biggest uncovered service at 493 lines) was
lifted **15.10% → 78.66% line / 70.00% branch**, raising the global floor to 47% line / 38% branch.
Then **`DocumentService`** (452 measurable lines, 0%) was lifted **0% → 91.15% line / 69.36% branch**,
raising the global floor to 48% line / 40% branch. Then **`InboxService`** (438 lines, 0%) was lifted
**0% → 97.95% line / 96.00% branch**, raising it to 50% line / 41% branch. Then **`ConversationService`**
(the next-biggest at 424 measurable lines, also 0%) was lifted **0% → 98.82% line / 100.00% branch**,
raising the global floor again to **51% line / 42% branch**. Then **`SemanticMemoryService`** (468 lines,
0%), **`WorkflowService`** (601 lines, 22.7%), and **`AutoTagService`** (473 lines, 0%) were closed
together — **0% → 97.86% / 94.48%**, **22.7% → 90.35% / 88.68%**, and **0% → 86.26% / 85.19%**
respectively — raising the global floor to **54% line / 45% branch** (and, along the way, fixing a latent
always-throws bug in `SemanticMemoryService.GetAllMemoriesAsync`). Then **`CollaborationService`** (431
lines, 0%) — the `HttpListener`-based real-time hub — was lifted **0% → 84.22% line / 81.03% branch** by
injecting a non-privileged localhost listener and driving the service's real request handlers over HTTP,
raising the global floor to **55% line / 46% branch**. Then **`ConversationBranchService`** (421 lines,
0%) — the EF-Core conversation-forking engine — was lifted **0% → 98.34% line / 96.97% branch**, raising
the global **line** floor to **56%** (the branch floor was held at 46% that round: measured branch was
47.03%, and a 47% floor would have left only 0.03 pt of headroom against the known async-branch variance
band). Then **`SemanticSearchService`** (412 lines, 0%) — the embed → vector-search → metadata-enrich →
filter → excerpt pipeline plus search-history CRUD — was lifted **0% → 97.33% line / 93.00% branch**,
raising the global floor to **57% line / 47% branch** (the branch gain the prior round could not safely lock
is now comfortably above the 47% floor at 47.85% measured). The next lever
for the global floor remains overall measured coverage itself (57.40% line / 47.85% branch): no single
critical namespace caps it — any authored-code coverage gain can raise it. The next-largest authored gaps
are `ComparisonService` (408 lines, 5%), `KeywordSearchService` (399, 0%), and `TemporalIdentityService`
(~395, 17%).

### Running it locally

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --configuration Release -p:Platform=x64 \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings
pwsh scripts/check-coverage.ps1 -CoverageFile TestResults
```

Add `-ReportOnly` to print the table without failing (useful when deciding new floors).
