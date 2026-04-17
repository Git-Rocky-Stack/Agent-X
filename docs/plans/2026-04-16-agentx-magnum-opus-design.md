# Agent-X Magnum Opus — Roadmap v2.1 → v3.0

**Status:** Approved
**Date:** 2026-04-16
**Author:** Rocky Elsalaymeh + Claude (Opus 4.7)
**Planning horizon:** ~45 weeks across 6 phases
**Baseline:** v2.0 "Enrichment" (Calendar + Email + OAuth, just merged — commit `d1206f0`)
**Target exit:** v3.0 "Everywhere" — full cross-platform, ecosystem-complete

---

## Executive Summary

Agent-X has sprinted past its v1.0/v1.1 thesis. v1.3–v1.5 shipped in succession (Workspace Profiles, Smart Inbox, Comparative Analysis, Whisper, Plugin API, Collaborative Sync foundation, DPAPI key encryption, System Tray + Global Hotkey, Browser Extension, Multi-Model Routing, Web Content Ingestion Depth, Conversation Branching, Export DOCX/PPTX, Deep Research Mode). Screen awareness + OCR + IDE detection landed pre-v2.0. HNSW vector store is in. Calendar (Google + Outlook) and Email (Gmail + Outlook) with OAuth PKCE + DPAPI-encrypted tokens just merged. The `.csproj` version bumped to `2.0.0` for the Enrichment release.

**The roadmap ahead is no longer about chasing v1.x gaps — it is about building the moats that turn Agent-X into the local-first AI hub of a user's stack.** The magnum opus thesis:

1. **Foundations must be unshakable.** Split the monoliths, deepen localization, ship keyboard-first power mode, and introduce at-rest encryption + tamper-evident audit log *before* memory touches the disk.
2. **The three platform primitives must co-release.** Model Context Protocol (server + client), Agent Skills System, and Persistent Evolving Memory form one triad — each is weaker alone, dominant together.
3. **The ecosystem is the compounding asset.** Marketplace + SDK + audio summaries turn the primitives into a flywheel.
4. **Presence closes the loop.** Mobile companion, agentic workflow v2, smart replies, calendar briefing, and typed entities make Agent-X show up wherever the user already works.
5. **Reach expands TAM.** Team Edition, publish-to-web, flashcards, runtime dashboard.
6. **Cross-platform kills the final objection.** macOS and Linux via Avalonia, with ≥95% feature parity.

The foundational design directive: **local-first, user-visible, user-controllable.** Nothing leaves the machine without explicit opt-in. Every layer ships with privacy banners, export/wipe controls, provenance, and tier-appropriate defaults.

---

## Section 1 — Current State Assessment

### 1.1 Ship Trajectory

The v1.1 enhancement doc is largely superseded. What has actually shipped:

| v1.1 Enhancement | Status | Evidence |
|---|---|---|
| #1 Export & Share | ✅ | v1.5 — DOCX/PPTX/templates, `ExportService.cs` (3047 LOC), `ExportDialog.xaml` |
| #2 Prompt Workflows/Chains | ✅ | `WorkflowEngine.cs` (921 LOC), `IWorkflowService`, Workflows models |
| #3 Web Content Ingestion | ✅ | v1.5 + sitemap/RSS/Atom parsers, `WebScraperService.cs` (1545 LOC) |
| #4 Conversation Branching | ✅ | v1.5 UI — `BranchCompareWindow`, branch tree sidebar, merge dialog |
| #5 Backup & Restore | ✅ | `BackupService.cs` (990 LOC), `BackupRestorePage.xaml` |
| #6 Keyboard-First Power Mode | ⚠️ Partial | `QuickActionsPage` exists, global hotkey works; **no command palette / jump-to / cheatsheet / chord registry** — carried into Phase 1 |
| #7 Annotations & Highlights | ✅ | `AnnotationsPage`, Annotations service |
| #8 Multi-Language UI | ⚠️ Shallow | 6 locales wired (de, es, fr, ja, zh-CN, en-US); **only 1 `Resources.resw` per locale — depth coverage suspect** — carried into Phase 1 |
| #9 Workspace Profiles | ✅ | v1.3 |
| #10 Smart Inbox | ✅ | v1.3 — `InboxService.cs` (865 LOC) |
| #11 Comparative Analysis | ✅ | v1.3 — `ComparisonService.cs` (780 LOC) |
| #12 System Tray + Global Hotkey | ✅ | v1.4 — Win+Shift+A Quick Chat overlay |
| #13 Plugin API | ✅ | v1.3 foundation + `PluginType.DataConnector` expansion |
| #14 Voice/Whisper | ✅ | v1.3 — Whisper.Net 1.5.0 |
| #15 Collaborative Sync | ✅ | v1.3 foundation — AES-256-GCM sync packages |

**Competitive gaps closed (per `docs/competitive-analysis-2026-04.md`):** DPAPI API-key encryption, browser extension, multi-model routing, screen awareness + OCR + IDE detection, HNSW vector store, Deep Research Mode + web search, Calendar (Google + Outlook), Email (Gmail + Outlook), OAuth with PKCE + CSRF state + DPAPI-encrypted tokens.

### 1.2 Codebase Scale

- **3 .NET projects** — `AgentX.App` (WinUI 3, net8.0-windows10.0.22621.0), `AgentX.Core` (engine, 30+ service folders), `AgentX.Mobile` (MAUI scaffold, net8.0-android + net8.0-ios)
- **1 sample plugin** — `plugins/sample-plugin/`
- **Tests** — 43 test files, **669 `[Fact]/[Theory]` tests**, healthy growth (Screen +32, HNSW +21, OAuth +64, Calendar +14, Email +13)
- **Docs** — ARCHITECTURE, API-REFERENCE, DEVELOPER-GUIDE, PLUGIN-DEVELOPMENT-GUIDE, SERVICE-REFERENCE, USER-GUIDE, v1.3–v1.5 release notes, competitive analysis
- **TODO/FIXME/HACK markers: 0** — exceptional hygiene
- **Deps current** — EF Core 8.0.11, WinAppSDK 1.6.250108002, LLamaSharp 0.19.0, OllamaSharp 4.0.12, Whisper.Net 1.5.0, Microsoft.Extensions.AI 9.5.0, PDFsharp 6.1.1, QuestPDF 2024.12.2, System.Security.Cryptography.ProtectedData 10.0.6

---

## Section 2 — Gap Analysis, Rework Hotspots, New Opportunities

### 2.1 Carry-Over Must-Do

| # | Item | Current state | Gap |
|---|---|---|---|
| A1 | Multi-Language UI depth | 6 locales wired; `Resources.resw` exists per locale | Only 1 resw per locale. String-extraction audit, per-page locale QA, pluralization rules, RTL safety, locale-coverage CI gate |
| A2 | Keyboard-First Power Mode | `QuickActionsPage` + global hotkey | No app-wide command palette, no jump-to-anything, no chord system, no cheatsheet, no shortcut registry |

### 2.2 Rework / Hardening Hotspots

| # | Area | Signal | Fix |
|---|---|---|---|
| B1 | `ExportService.cs` | 3047 LOC — all formats in one file | Split into `IExportFormatter` + 6 formatters + orchestrator + template engine |
| B2 | `ChatViewModel.cs` | 1897 LOC — branching, streaming, citations, research, voice, screen, inbox tangled | Extract 4 coordinators (Branch, Streaming, Attachment, ToolInvocation); ViewModel becomes thin |
| B3 | `WebScraperService.cs` | 1545 LOC — fetch/parse/JSON-LD/sitemap/RSS/subscribe in one class | Split into Fetchers/Parsers/Subscriptions pipeline |
| B4 | `SyncService.cs` (1518) + `SyncSettingsViewModel.cs` (899) | Conflict resolution + encryption + transport tangled | Extract `SyncConflictResolver`, `SyncPackageCodec`, `SyncTransport` interface (cloud-ready) |
| B5 | `MainWindow.xaml.cs` | 939 LOC — navigation + tray + hotkey + chrome | Move concerns into services; code-behind ≤ 200 LOC |
| B6 | `UserGuidePage.xaml` | 2555 LOC monolith | Shard into sections with `ContentControl` template selector (enables localization pagination) |
| B7 | Structured output (JSON Schema) | Not verified in code | `IJsonSchemaEnforcer` wrapping `Microsoft.Extensions.AI` response-format; provider-support matrix |
| B8 | Test coverage on newer services | 669 tests — gap likely in Sync/Collaboration/Plugin lifecycle | Fill gaps in Phase 3; coverage trajectory 75→85% |
| B9 | EF migration discipline | Manual `ALTER TABLE` in `55f85ed` suggests drift | `IMigrationRunner`, pending-migration startup check, rollback scripts |
| B10 | Dependency uplift | .NET 8.0.11 today, .NET 9 LTS available | Plan post-2.0 — not urgent |

### 2.3 New Enhancement Opportunities

#### High-impact moats

| # | Feature | Moat |
|---|---|---|
| C1 | **MCP Server + Client** | Agent-X becomes the hub of a user's AI stack — queryable by Claude Desktop, Cursor, Zed; consumes external MCP tools |
| C2 | Public SDK (Python + TypeScript) | `ApiHostService` exists; publish `agentx-py` + `@strategia/agentx`; matches LM Studio's dev story |
| C3 | Audio Summaries (Piper TTS) | NotebookLM's killer feature, local + private |
| C4 | Agentic Workflow v2 (tool-using agents) | ReAct + tool fan-out + budget caps; Ultimate-tier headliner |
| C5 | Flesh out AgentX.Mobile | MAUI scaffold is bare — read-only companion first |
| C6 | Plugin & Skill Marketplace (offline-first) | Signed `.agentx-plugin` / `.agentx-skill` + static JSON gallery — ecosystem flywheel |
| **C19** | **Agent Skills System** | Markdown/YAML skill docs; local + user-authorable; first-class alongside plugins; skill↔workflow compile path |
| **C20** | **Persistent Evolving Memory** | Four-layer stack (Episodic / Semantic Facts / Procedural / Identity Graph); local-only; user-visible; provenance-tagged; decay + feedback loop |

#### Productivity wins

| # | Feature |
|---|---|
| C7 | Smart replies from vault (email/calendar draft panels) |
| C8 | Calendar Briefing / Morning Digest |
| C9 | Typed Entity Model (Person/Project/Meeting/Book) |
| C10 | Publish-to-Web Export |
| C11 | Canvas / Spatial view (optional, demand-gated) |
| C12 | Spaced Repetition / Flashcards |

#### Trust & enterprise

| # | Feature |
|---|---|
| **C13** | **SQLCipher at-rest encryption** (Phase 1, Phase 2 prereq) |
| **C14** | **Audit log** — tamper-evident, HMAC-chained (Phase 1, Phase 2 prereq) |
| C15 | Team Edition (multi-user sync, OIDC SSO, RBAC) |
| C16 | Runtime Health dashboard |

#### Cross-platform

| # | Feature |
|---|---|
| C17 | macOS port via Avalonia |
| C18 | Linux port via Avalonia |

---

## Section 3 — The Phased Plan

### Baseline
- In-flight: **v2.0 "Enrichment"** (Calendar + Email + OAuth) — releases before Phase 1 starts
- Cadence: one named release per phase, no long-lived branches
- Tier-gating preserved across every phase
- Release notes markdown per phase following v1.3/v1.4/v1.5 pattern

### Phase 1 — Foundations (v2.1 "Bedrock")
**Duration:** 5–6 weeks
**Theme:** Harden the floor, deliver Rocky's two must-dos, lay crypto + audit primitives for Phase 2.

**Scope:**
- A1 Multi-Language UI depth (string-extraction audit, per-page locale QA, pluralization, CI gate, ≥98% coverage)
- A2 Keyboard-First Power Mode (command palette `Ctrl+Shift+P`, jump-to `Ctrl+P`, cheatsheet `?`, chord registry, per-page shortcut help)
- B1 Split `ExportService` (≤400 LOC per file)
- B2 Split `ChatViewModel` (4 coordinators)
- B3 Split `WebScraperService`
- B4 Split `SyncService` + codec extraction
- B5 Shrink `MainWindow.xaml.cs` to ≤200 LOC
- B6 Shard `UserGuidePage.xaml`
- B7 Structured output (JSON Schema enforcer)
- B9 EF migration runner
- **C13 SQLCipher at-rest encryption**
- **C14 Audit log (HMAC-chained, viewer, export)**

**Acceptance gate:** all 669 tests green, +80 new tests for A1/A2/C13/C14, zero regressions across v1.5 features, SQLCipher toggle verified on clean machine, audit log round-trip verified.

### Phase 2 — Platform Primitives (v2.2 "Symbiosis") ★ Co-release
**Duration:** 9–10 weeks
**Theme:** The magnum-opus phase. MCP + Skills + Memory ship together.

**Scope:**
- C1a MCP Server (stdio + SSE transports; capability manifest; discoverable by Claude Desktop, Cursor, Zed)
- C1b MCP Client (`McpClientService` + UI; capability registry; tool invocation in chat)
- **C19 Agent Skills System** — `skills/` directory; `SKILL.md` + `steps.yaml`; `ISkillService`; semantic matcher; `SkillStudioPage`; skill↔workflow compile path
- **C20a Memory Layer 2 (Semantic Facts)** — `MemoryService`, `UserFact` entity, extractor pipeline, promotion policy, decay function
- **C20b Memory Inbox + Retrieval** — inbox UI, top-K injection with provenance, budget caps, feedback loop
- **C20c MemoryPage UX** — timeline, editable table, NL query, forget controls, JSON export, privacy banner
- **C20d Procedural layer** — pattern detector proposes draft skills from user behavior
- C4 (partial) Agentic primitive — ReAct loop skeleton used by Skill invocation

**Tier-gating:**
- Starter: MCP read-only, skill use only, memory ≤100 facts no auto-promotion
- Professional: MCP full, skill authoring, full memory + auto-promotion + Skill generation
- Ultimate: Team skill publishing, shared memory scoping, MCP server signing

**Acceptance gate:** Claude Desktop queries Agent-X vault via MCP; user-authored skill round-trips Skill Studio → registry → invocation → memory fact; "what do you know about me?" returns ≥10 facts with provenance; prompt-injection budget caps proven via test suite; all previous-phase tests green.

**Risk guardrail:** no other features in Phase 2. Mobile memory sync is Phase 4.

### Phase 3 — Ecosystem (v2.3 "Open Hub")
**Duration:** 6 weeks
**Theme:** Turn the primitives into an ecosystem.

**Scope:**
- C6 Plugin & Skill Marketplace — signed `.agentx-plugin` / `.agentx-skill`; installer UI; static JSON gallery; update + rollback; tier-aware
- C2 Public SDK — `agentx-py` (PyPI) + `@strategia/agentx` (npm); auto-generated docs from OpenAPI
- C3 Audio Summaries — Piper TTS (Apache-2.0); "Podcast This Collection"; 1-voice + 2-voice dialog; MP3 + M4B chapters; background queue; Professional+
- B8 Test coverage fill — Sync/Collaboration/Plugin lifecycle; coverage ≥75%

**Acceptance gate:** external dev installs a community skill + plugin from marketplace in <60s; Python SDK smoke test queries vault; 20-doc audio overview generates in <90s on reference hardware.

### Phase 4 — Experience (v2.4 "Presence")
**Duration:** 8 weeks
**Theme:** The agent shows up wherever the user works.

**Scope:**
- C5 AgentX.Mobile read-only companion v1 — iOS + Android; vault browse; global search; chat via LAN tunnel (QR pair); memory-aware; offline cache of top-N favorites; biometric lock
- C4 Agentic Workflow v2 full — tool-using ReAct agents; budget caps; function calling; parallel fan-out; replayable trace; Ultimate tier
- C7 Smart replies — email/calendar draft suggestions grounded in memory + vault
- C8 Calendar Briefing / Morning Digest — per-meeting brief; `DigestPage` + optional tray notification
- C9 Typed Entity Model v1 — `Entity` base + built-ins (Person/Project/Meeting/Book/Note); tag→entity converter; structured query

**Acceptance gate:** mobile paired <30s, searches <500ms; 3-tool-call agentic run produces trace; morning digest for 4 meetings surfaces in tray; typed entity migration converts existing tags non-destructively.

### Phase 5 — Expansion (v2.5 "Reach")
**Duration:** 6 weeks
**Theme:** Broaden use cases without breaking focus.

**Scope:**
- C15 Team Edition foundation — multi-user sync; OIDC SSO (Azure AD / Google / Okta); RBAC (admin/editor/viewer); shared workspaces; per-entity access control; Ultimate + per-seat upsell
- C10 Publish-to-Web — Markdown → HTML bundle; self-host or direct Cloudflare Pages upload; optional password-protect
- C12 Spaced Repetition — auto-generate cards; SM-2; `LearnPage`; stats dashboard
- C16 Runtime Health Dashboard — extend `HardwareAdvisorPage` with latency histograms, GPU memory timeline, inference queue depth, token/sec trends
- C11 (optional) Canvas / spatial view — gated on demand signal from Phase 4

**Acceptance gate:** team workspace with 5 users round-trips edits + respects RBAC; published static site renders 100-doc collection <5MB with working search; flashcard review of 20 cards <2 minutes.

### Phase 6 — Cross-platform (v3.0 "Everywhere")
**Duration:** 10–12 weeks
**Theme:** Kill the final TAM objection.

**Scope:**
- **Framework decision: Avalonia UI** (better WinUI 3 parity than MAUI Blazor; same MVVM patterns; shared `AgentX.Core` unchanged)
- C17 macOS port — new `AgentX.App.Mac`; `LLamaSharp.Backend.Metal`; notarized DMG; feature-parity ≥95%
- C18 Linux port — same Avalonia base; `.deb` + AppImage; CUDA/ROCm optional; feature-parity ≥90% (no DPAPI → SQLCipher passphrase mandatory)
- Platform abstraction pass — extract `IPlatformServices` (file dialogs, tray, hotkey, protected storage, notifications); no WinRT leakage into Core

**Acceptance gate:** macOS build passes v2.4 feature matrix on M3 Mac + Intel Mac; Linux build passes on Ubuntu 24.04 + Fedora 40; Phase 2 primitives (MCP/Skills/Memory) work identically on all three platforms.

---

## Cross-cutting Commitments

| Concern | Rule |
|---|---|
| Release notes | One markdown per release with Overview, Features, Fixes, Breaking Changes, Upgrade Notes |
| Test gate | No phase ships with lower coverage than it started. Trajectory: 75 → 80 → 82 → 85% |
| Security review | Phase 1 (C13/C14) and Phase 2 (Memory) require `/cso` pass before release |
| Docs | API-REFERENCE, DEVELOPER-GUIDE, PLUGIN-DEVELOPMENT-GUIDE updated per phase — no doc debt across phase boundaries |
| Backwards compat | Single forward-only DB migration runner (B9) — breaking changes require migration + rollback |
| SEO/GEO | Marketing landing page for each release on strategia-x.com with full SEO/GEO checklist |
| Localization | After Phase 1, locale-coverage CI gate blocks PRs that add untranslated `x:Uid`s |

---

## Tier-Gating Summary (post-v3.0)

| Capability | Starter $79 | Professional $149 | Ultimate $249 |
|---|---|---|---|
| Core chat + RAG | ✅ (500 docs) | ✅ Unlimited | ✅ Unlimited |
| Workflows / Skills use | ✅ | ✅ | ✅ |
| Skills authoring | — | ✅ | ✅ |
| Memory (basic ≤100 facts) | ✅ | Full + auto-promote | Full + team-shared |
| MCP read | ✅ | Full | Full + signed server |
| Plugin marketplace install | ✅ | ✅ | ✅ |
| Audio summaries | — | ✅ | ✅ |
| SQLCipher at-rest | DPAPI-wrap | DPAPI-wrap | ✅ Passphrase + Hello |
| Agentic Workflow v2 | — | — | ✅ |
| Team Edition | — | — | ✅ (per-seat add-on) |
| Audit log export | — | ✅ | ✅ + HMAC verify |

---

## Approved Decisions

1. **Phase 2 duration 9–10 weeks as single co-release** — MCP + Skills + Memory ship together.
2. **Phase 6 framework: Avalonia UI** for macOS and Linux.
3. **Tier-gating** as specified — MCP read in Starter, Skills authoring in Professional, Agentic v2 + Team + signed-MCP in Ultimate.
4. **SQLCipher + Audit log are Phase 1 prerequisites** — Memory ships on encrypted + audited storage.

---

## Next Step

Implementation plans to be produced per phase via the `superpowers:writing-plans` skill. Phase 1 plan first (immediate); later phases planned phase-by-phase as exit gates approach so specs stay fresh.
