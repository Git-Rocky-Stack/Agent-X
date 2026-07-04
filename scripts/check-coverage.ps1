#requires -Version 7.0
<#
.SYNOPSIS
    AX-QA-009 coverage gate. Enforces minimum line/branch coverage on authored AgentX.Core
    code, with elevated floors for security- and migration-critical namespaces.

.DESCRIPTION
    Parses the Cobertura report produced by `dotnet test --collect "XPlat Code Coverage"
    --settings coverlet.runsettings` and fails (exit 1) if any tracked metric falls below its
    configured floor.

    The audit (AX-QA-009) observed that a passing test count is not release confidence: large,
    high-risk Core services sit at 0% coverage. This gate locks in the current authored-code
    coverage so it can only ratchet up, and holds security/migration code to a higher bar than
    the repository-wide minimum — exactly as the finding asks.

    Coverage basis (see coverlet.runsettings): generated scaffolding ([GeneratedCode], EF
    migration files, *.Designer.cs) is excluded so the denominator is code the team authored.
    [CompilerGenerated] is intentionally kept so async/lambda bodies remain measured.

    THRESHOLDS ARE A RATCHET. When coverage rises, raise these floors in the same change so the
    gain is protected. Never lower a floor to make a red build pass — add tests instead.

.PARAMETER CoverageFile
    Path to a coverage.cobertura.xml file, or a directory to search recursively for the most
    recent one. Defaults to the repo-root TestResults / .cov-tmp conventions.

.PARAMETER ReportOnly
    Print the coverage table but always exit 0 (used to read current numbers when setting floors).
#>
[CmdletBinding()]
param(
    [string]$CoverageFile,
    [switch]$ReportOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────────────────────────────────────
# Coverage policy (the ratchet). Percentages are integers (0-100).
#   global            — repository-wide minimum for authored AgentX.Core code.
#   criticalNamespaces — elevated floors for high-risk areas; each MUST be >= the
#                        global floor (the gate asserts this so the policy stays honest).
# ─────────────────────────────────────────────────────────────────────────────
# Floors are set just below the measured baseline (shown in comments) so the gate locks in current
# coverage with a small headroom for CI variance, and every critical floor sits at or above the
# global minimum as AX-QA-009 requires. On 2026-07-03 the campaign's three tracked next-gaps were
# closed in one round. KeywordSearchService (previously 0%) — the FTS5 (porter unicode61) BM25
# keyword-search pipeline (virtual-table init/rebuild, per-document chunk indexing + delta re-index,
# MATCH sanitisation, file-type/collection/date filters, excerpt building) — was lifted to 89.87 line /
# 81.48 branch by running REAL FTS5 end-to-end over the shared in-memory SQLite connection (the bundled
# SQLite ships fts5); the per-document rebuild catch-arm is reached by DROP-TABLE sabotage from inside
# the progress callback. The tests exposed and fixed a latent relevance-inversion bug: SearchAsync
# normalised BM25 as 1/(1+|rank|), which orders WORST-first (FTS5 rank is more negative = better), so
# direct keyword search returned the least-relevant chunks whenever TopK truncated and hybrid RRF
# fusion inherited the inverted list order; the fix (|rank|/(1+|rank|)) keeps the 0-1 range with
# higher = better. TemporalIdentityService (previously only 4 regression tests) — belief tracking with
# EMA sentiment + evolution flags, insight capture/resurfacing, engagement depth, voice-profile
# learning / generate-as-user, and problem-pattern typing — was lifted to 95.60 line / 75.00 branch
# over the EF-SQLite harness, exposing TWO more latent bugs, both fixed: (1)
# GetRelatedConversationsAsync/GetRelatedDocumentsAsync used DateTime subtraction (.TotalDays) in
# Where/OrderBy — untranslatable on the SQLite provider, so EVERY GetPastSelfAsync call threw
# (GetAllMemoriesAsync precedent; fixed by materialising the title matches, then windowing/ordering in
# memory — the Ticks-weighting and keyword-Any probes translated fine and stay server-side); (2)
# DetectInsightsAsync computed marker-based significance (0.6, +0.2 breakthrough, +0.1 excitement) but
# never passed it — CaptureInsightAsync hardcoded 0.7 for every insight, flattening GetTopInsights
# ranking; an optional significance parameter (default 0.7 preserves the user-explicit and annotation
# contracts) now persists the computed score. LocalLlmProvider (previously 0%) — the LLamaSharp-backed
# offline provider — was lifted to 85.77 line / 75.00 branch via two internal seams (ComparisonService
# optional-seam precedent): InferenceOverride substitutes the StatelessExecutor token stream so the
# chat pipeline (llama3 prompt formatting, JSON priming, options mapping, inference lock, truncation
# warning, cancellation) runs for real without a native GGUF, and DownloadUrlResolver redirects
# PullModelAsync to a localhost HttpListener stub (streaming copy, progress reports, atomic .part
# move, failure cleanup); the deliberate residual is LoadModelAsync's success body plus the real
# executor/embedder calls (they need a multi-GB native GGUF — the download test's trailing load
# intentionally fails on the garbage magic, covering the load catch-arm). None of the three is a trust
# boundary, so none becomes a critical namespace; the combined gain raised the GLOBAL floor LINE
# 58 -> 62 (measured 62.58) and BRANCH 48 -> 51 (measured 52.43 — a 52 floor would leave only 0.43pt
# of headroom, inside the run-to-run async-branch variance band this file repeatedly observes).
# Earlier: on 2026-06-30 ComparisonService (previously a 5% stub —
# only its guard clauses were tested) — the AI cross-document comparison pipeline (resolve document
# metadata -> retrieve each document's most-relevant chunks via ISemanticSearchService, scoping /
# ordering by ChunkIndex -> assemble a ComparisonSynthesisRequest -> synthesize a JSON analysis via
# an injected IDocumentSynthesisService -> parse into a ComparisonReport, with a plain-text section
# scanner as the fallback when the response isn't valid JSON) plus the Markdown export renderer — was
# lifted to 100.00 line / 99.02 branch (367/367, 101/102). The key seam is the optional
# IDocumentSynthesisService ctor parameter: injecting a mock lets each test drive the parser
# deterministically (valid JSON with case-insensitive uniquePoints keys + list sanitisation, prose /
# code-fence-wrapped JSON, malformed-body -> fallback, OperationCanceled rethrow vs generic-wrap, empty
# response), while ONE integration test omits the seam so the ctor builds the real
# DocumentSynthesisService from IAiService and the default path runs end to end. This round also removed
# genuinely dead code that had been capping coverage: ComparisonService's private BuildSystemPrompt /
# BuildUserPrompt methods and its AnalysisChatOptions field were leftovers from before the AI call was
# extracted into DocumentSynthesisService (which holds the live copies) — provably unreferenced
# (private, non-partial class), so their removal is a pure maintainability win, not a behaviour change.
# The single remaining uncovered branch is the defensive `?? throw JsonException("Deserialisation
# returned null")` guard, unreachable because a brace-delimited substring never deserializes to JSON
# null. Not a trust boundary, so NOT a critical namespace; its gain raised the GLOBAL floor LINE 57 -> 58
# (measured 58.59) and BRANCH 47 -> 48 (measured 48.68). (Backup line recovered to 77.41 this round —
# the prior round's 75.9 dip was the expected async-timing wobble, not a regression.) Earlier the same
# day, SemanticSearchService (412 measurable lines, previously
# 0%) — the semantic-search pipeline (embed query -> vector ANN search -> EF-Core metadata enrichment ->
# embedding-model-version + collection/file-type/date filtering -> excerpt building -> TopK sort) plus the
# search-history / saved-filter CRUD — was lifted to 97.33 line / 93.00 branch (401/412, 93/100). It composes
# three mockable collaborators over a real AgentXDbContext: IEmbeddingService (query embedding + ModelVersion
# for the compatibility gate), IVectorStore (the ANN search), and an optional IRagConfiguration (retrieval
# multiplier/cap; absent -> built-in fallbacks 3/500). Vector hits are seeded by Distance because
# VectorSearchResult.Similarity is the computed inverse 1-Distance; the version gate compares each chunk's
# EmbeddingModelVersion to the service's ModelVersion (null/empty = legacy-compatible). The three
# OCE-rethrow-vs-generic-swallow arms (embed / vector / chunk-load) are covered by a throwing mock for the OCE
# path and a generic-exception mock (or a disposed context) for the swallow path; a pre-canceled token drives
# the chunk-load OCE rethrow. Not a trust boundary, so NOT a critical namespace; its gain raised the GLOBAL
# floor LINE 56 -> 57 (measured 57.40) and BRANCH 46 -> 47 (measured 47.85) — the branch headroom the prior
# ConversationBranchService round could not safely lock is now comfortably locked. (Sidebar: Backup line
# wobbled to 75.9 again under the larger suite — still >= its 75 floor but a recurring thin margin; its
# residual is the deliberately-uncovered restore-swap body, so more Backup tests can't safely raise it —
# watch, don't lower.) Earlier the same day, ConversationBranchService (421 measurable lines,
# previously 0%) — the EF-backed engine for conversation forking (branch-at-message with message copy +
# token/count aggregation), branch-tree / root queries, cross-conversation message merge, and recursive
# branch deletion — was lifted to 98.34 line / 96.97 branch via the EF-SQLite harness (real AgentXDbContext;
# the injected IConversationService is null-guarded by the ctor but consumed by no method, so supplied as a
# bare mock; a real silent Serilog logger for the ForContext<T>() ctor). Two model facts shaped the tests:
# the self-referencing ParentConversation->Branches FK is DeleteBehavior.Restrict (so a NON-recursive delete
# of a branch that still has children is DB-rejected with DbUpdateException, and recursive deletes must remove
# the deepest descendant first), while Messages->Conversation is Cascade; the generic catch(Exception) rethrow
# arms were reached with a pre-canceled token (OperationCanceledException bypasses the earlier
# catch(InvalidOperationException) arms), the circular-reference maxDepth guard via a self-parent row, and the
# orphaned-parent walk-stop via a raw FK-off DELETE of the parent. Not a trust boundary, so NOT a critical
# namespace; its gain raised the GLOBAL floor LINE 55 -> 56 (measured 56.36) while the BRANCH floor is HELD at
# 46 (measured 47.03 — a 47 floor would leave only 0.03pt of headroom, inside the run-to-run async-branch
# variance band this file repeatedly observes, e.g. OAuth and Backup). Earlier, on 2026-06-28
# CollaborationService (431 measurable lines,
# previously 0%) — a real-time collaboration hub on HttpListener + HttpClient (no EF) — was lifted to
# 84.22 line / 81.03 branch. Its public StartHostingAsync binds the strong-wildcard prefix
# http://+:{port}/ (needs an elevated URL-ACL reservation, unrunnable unprivileged in CI), so the harness
# injects a non-privileged http://localhost:{port}/ listener into the service's own fields and runs its
# real RunListenerLoopAsync via reflection — exercising the production request handlers over real HTTP —
# while the session/presence/event/query surface is tested directly and the timer callbacks
# (PruneExpiredSessions/SendHeartbeat) are invoked by reflection. Not a trust boundary, so NOT a critical
# namespace; its gain raised the GLOBAL floor LINE 54 -> 55 and BRANCH 45 -> 46, bounded by the GLOBAL
# measured value (55.26 / 46.47). Earlier on 2026-06-28 three of the largest remaining authored gaps were
# closed together: SemanticMemoryService (468 measurable lines, previously 0%) -> 97.86 line / 94.48
# branch, WorkflowService (601 lines, previously 22.7%) -> 90.35 / 88.68, and AutoTagService (473 lines,
# 0%) -> 86.26 / 85.19 — all via the EF-SQLite harness (real AgentXDbContext; mocked IAiService /
# IEmbeddingService / IRagConfiguration / IFeatureFlagService; deterministic length-4 embedding vectors
# for exact cosine similarity; a real silent Serilog logger because each ctor consumes
# logger.ForContext<T>(); real temp files for the AutoTag file-read fallback). None is a trust boundary,
# so none is a critical namespace; the combined gain flows into the global denominator, raising the
# GLOBAL floor LINE 51 -> 54 and BRANCH 42 -> 45, bounded by the GLOBAL measured value (54.33 / 45.64).
# This round also fixed a latent always-throws bug in SemanticMemoryService.GetAllMemoriesAsync (its
# OrderBy used the un-translatable GetEffectiveImportance, so EF threw on every call) by materialising
# the active set before the in-memory sort, and hardened OAuth with three constructor null-guard tests
# (branch 74.55 -> 78.18) after the larger suite perturbed async-branch timing toward the OAuth floor;
# the OAuth floor itself is unchanged (75 branch) so its headroom absorbs that variance. Earlier the
# same day ConversationService (424 lines, 0%) -> 98.82 / 100.00 took the global floor 50 -> 51 line /
# 41 -> 42 branch; InboxService (438 lines, 0%) -> 97.95 / 96.00 took it 48 -> 50 / 40 -> 41;
# DocumentService (452 lines, 0%) -> 91.15 / 69.36 took it 47 -> 48 / 38 -> 40; and BackupService
# 15.10% -> 78.66 / 70.00 (mocking the injectable IEncryptedConnectionFactory to redirect the SQLite
# source copy to a seeded throwaway DB). BackupService performs AES-256-GCM encryption of the entire
# user database, so its namespace is tracked as critical (floor 75 line / 65 branch; measured
# 79.21 / 70.00); its residual uncovered region is RestoreFromBackupAsync's database-swap body, which
# writes the hardcoded real user-profile DB path with no injection seam (running it would clobber the
# live DB), plus the PeriodicTimer-gated scheduled-loop body. Earlier rounds: 2026-06-27 OAuth lifted
# 45.18/34.55 -> 82.57/77.27 (unpinned the global floor: line 44 -> 45, branch 33 -> 37) and SyncService
# (global line -> 44); 2026-06-24 PluginService/WorkflowEngine (global line 41 -> 42); 2026-06-21
# ApiHostService. Critical-namespace baselines (Security/Privacy/MigrationRunner) are from 2026-06-20.
$Policy = [ordered]@{
    Global = @{ Line = 62.0; Branch = 51.0 }          # measured 62.58 / 52.43 (2026-07-03, KeywordSearch/TemporalIdentity/LocalLlm)
    CriticalNamespaces = [ordered]@{
        # Security-critical: DB key material, DPAPI secret encryption, encryption-state migration,
        # security status. A regression here is a trust/compliance regression. Its branch floor (62)
        # is the lowest critical branch floor; Backup's line floor (75) is the lowest critical line floor.
        'AgentX.Core.Services.Security' = @{ Line = 80.0; Branch = 62.0 }   # measured 82.41 / 66.22
        # Privacy disclosure (AX-QA-008) — the dashboard "no cloud" claim depends on it; keep tight.
        'AgentX.Core.Services.Privacy'  = @{ Line = 95.0; Branch = 85.0 }   # measured 100   / 90.62
        # OAuth token handling for the calendar/email connectors. Lifted 2026-06-27 from 45.18/34.55;
        # the residual gap is AuthorizeAsync's browser-launch + local-callback body, which cannot run
        # in CI (it shells to the system browser and blocks on a real redirect). No longer binding:
        # both floors now sit well above the global floor.
        'AgentX.Core.Services.OAuth'    = @{ Line = 80.0; Branch = 75.0 }   # measured 82.57 / 77.27
        # Backup-critical: AES-256-GCM authenticated encryption of the WHOLE user database + documents
        # (V2), with legacy AES-256-CBC restore. A regression here risks unrecoverable or tampered
        # backups. Lifted 2026-06-28 from 15.10%; the residual gap is RestoreFromBackupAsync's
        # database-swap body (writes the hardcoded real user-profile DB path — no seam to redirect it)
        # and the PeriodicTimer-gated scheduled-loop body.
        'AgentX.Core.Services.Backup'   = @{ Line = 75.0; Branch = 65.0 }   # measured 79.21 / 70.00
        # Migration-critical: the runner that applies EF migrations and guards against partial
        # baselines (AX-QA-002/003). The migration scaffolds themselves are excluded as generated.
        'AgentX.Core.Data.MigrationRunner' = @{ Line = 95.0; Branch = 85.0 } # measured 98.11 / 91.67
    }
}

# Files that may slip through collector exclusions; never counted toward the gate.
$GeneratedFilePatterns = @(
    '\.g\.cs$',
    '\.Designer\.cs$',
    '[\\/]Migrations[\\/]',
    '[\\/]obj[\\/]',
    'RegularExpressions\.Generated'
)

function Resolve-CoverageFile {
    param([string]$Hint)

    $candidates = @()
    if ($Hint) {
        if (Test-Path -LiteralPath $Hint -PathType Leaf) { return (Resolve-Path -LiteralPath $Hint).Path }
        if (Test-Path -LiteralPath $Hint -PathType Container) {
            $candidates = @(Get-ChildItem -LiteralPath $Hint -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue)
        }
    } else {
        foreach ($root in @('TestResults', '.cov-tmp', 'tests')) {
            if (Test-Path -LiteralPath $root) {
                $candidates += @(Get-ChildItem -LiteralPath $root -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue)
            }
        }
    }

    if ($candidates.Count -eq 0) {
        throw "No coverage.cobertura.xml found (hint: '$Hint'). Did the test step run with --collect 'XPlat Code Coverage' --settings coverlet.runsettings?"
    }
    return ($candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Test-IsGenerated {
    param([string]$FileName)
    if (-not $FileName) { return $false }
    foreach ($pat in $GeneratedFilePatterns) {
        if ($FileName -match $pat) { return $true }
    }
    return $false
}

# Aggregate line + branch counts for every <class> whose type name starts with $Prefix.
# Branch counts come from each branch line's `condition-coverage="NN% (covered/total)"`.
function Measure-Namespace {
    param([System.Xml.XmlElement[]]$Classes, [string]$Prefix)

    $linesCovered = 0; $linesValid = 0; $branchesCovered = 0; $branchesValid = 0
    foreach ($class in $Classes) {
        $name = [string]$class.name
        if (-not $name.StartsWith($Prefix, [StringComparison]::Ordinal)) { continue }
        if (Test-IsGenerated ([string]$class.filename)) { continue }

        $lineNodes = $class.SelectNodes('lines/line')
        foreach ($line in $lineNodes) {
            $linesValid++
            if ([int]$line.hits -gt 0) { $linesCovered++ }
            # The hyphenated `condition-coverage` attribute (e.g. "50% (1/2)") is read via
            # GetAttribute; dotted property access can't see a hyphenated XML attribute name.
            if ([string]$line.branch -eq 'True') {
                $cc = $line.GetAttribute('condition-coverage')
                if ($cc -match '\((\d+)/(\d+)\)') {
                    $branchesCovered += [int]$Matches[1]
                    $branchesValid   += [int]$Matches[2]
                }
            }
        }
    }
    return [pscustomobject]@{
        LinesCovered = $linesCovered; LinesValid = $linesValid
        BranchesCovered = $branchesCovered; BranchesValid = $branchesValid
    }
}

function Get-Rate {
    param([int]$Covered, [int]$Valid)
    if ($Valid -le 0) { return $null }   # no measurable lines/branches → not gated
    return [math]::Round(100.0 * $Covered / $Valid, 2)
}

# ─────────────────────────────────────────────────────────────────────────────
$path = Resolve-CoverageFile -Hint $CoverageFile
Write-Host "Coverage report: $path"
[xml]$doc = Get-Content -LiteralPath $path -Raw

$root = $doc.coverage
$globalLine   = Get-Rate ([int]$root.GetAttribute('lines-covered'))    ([int]$root.GetAttribute('lines-valid'))
$globalBranch = Get-Rate ([int]$root.GetAttribute('branches-covered')) ([int]$root.GetAttribute('branches-valid'))

# Flatten all <class> nodes across every package.
$allClasses = @($doc.SelectNodes('//class'))

$rows = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Row {
    param([string]$Scope, $Line, $Branch, $LineFloor, $BranchFloor)
    $lineOk   = ($null -eq $Line)   -or ($Line   -ge $LineFloor)
    $branchOk = ($null -eq $Branch) -or ($Branch -ge $BranchFloor)
    $status = if ($lineOk -and $branchOk) { 'PASS' } else { 'FAIL' }
    if (-not $lineOk)   { $script:failures.Add("$Scope line $Line% < floor $LineFloor%") | Out-Null }
    if (-not $branchOk) { $script:failures.Add("$Scope branch $Branch% < floor $BranchFloor%") | Out-Null }
    $script:rows.Add([pscustomobject]@{
        Scope = $Scope
        Line = if ($null -eq $Line) { 'n/a' } else { "$Line%" }
        LineFloor = "$LineFloor%"
        Branch = if ($null -eq $Branch) { 'n/a' } else { "$Branch%" }
        BranchFloor = "$BranchFloor%"
        Status = $status
    }) | Out-Null
}

# Global gate.
Add-Row 'GLOBAL (AgentX.Core, authored)' $globalLine $globalBranch $Policy.Global.Line $Policy.Global.Branch

# Critical-namespace gates.
foreach ($ns in $Policy.CriticalNamespaces.Keys) {
    $floor = $Policy.CriticalNamespaces[$ns]
    if ($floor.Line -lt $Policy.Global.Line -or $floor.Branch -lt $Policy.Global.Branch) {
        $failures.Add("POLICY ERROR: critical floor for $ns is below the global floor") | Out-Null
    }
    $m = Measure-Namespace -Classes $allClasses -Prefix ("$ns.")
    if ($m.LinesValid -eq 0) {
        $failures.Add("POLICY ERROR: critical namespace '$ns' matched no measured code (renamed or moved?)") | Out-Null
    }
    $line   = Get-Rate $m.LinesCovered $m.LinesValid
    $branch = Get-Rate $m.BranchesCovered $m.BranchesValid
    Add-Row $ns $line $branch $floor.Line $floor.Branch
}

# ── Report ──
$table = $rows | Format-Table -AutoSize | Out-String
Write-Host ''
Write-Host $table

if ($env:GITHUB_STEP_SUMMARY) {
    $md = "## Coverage gate (AX-QA-009)`n`n"
    $md += "| Scope | Line | Floor | Branch | Floor | Status |`n"
    $md += "|---|---|---|---|---|---|`n"
    foreach ($r in $rows) {
        $icon = if ($r.Status -eq 'PASS') { '✅' } else { '❌' }
        $md += "| $($r.Scope) | $($r.Line) | $($r.LineFloor) | $($r.Branch) | $($r.BranchFloor) | $icon |`n"
    }
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $md
}

if ($failures.Count -gt 0 -and -not $ReportOnly) {
    Write-Host '::error::Coverage gate failed:'
    foreach ($f in $failures) { Write-Host "  - $f" }
    Write-Host ''
    Write-Host 'Add tests to raise coverage. Do not lower the floors in scripts/check-coverage.ps1 to pass.'
    exit 1
}

if ($failures.Count -gt 0) {
    Write-Host "ReportOnly: $($failures.Count) metric(s) would fail the gate."
} else {
    Write-Host 'PASS: all coverage floors met.'
}
exit 0
