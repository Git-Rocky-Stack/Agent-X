# Agent-X Gap Remediation and Enhancement Plan

**Date:** 2026-04-22
**Source:** Repo-grounded gap analysis of the current Agent-X application surface
**Planning shape:** split into immediate hardening/remediation and next-wave enhancements
**Intent:** turn the current breadth of Agent-X into a more coherent, discoverable, durable, and scalable product

---

## Executive Summary

Agent-X already has a large feature surface: chat, RAG, knowledge vault, search, workflows, inbox triage, plugins, sync, export, analytics, encryption, keyboard power mode, and the new persistent conversation-summary layer. The main gaps are no longer basic capability gaps. The highest-value work is now:

1. align documentation and navigation with the real app
2. fix incomplete or weakly surfaced advanced features
3. harden heavy data-loading paths and user-facing workflows
4. make durable intelligence visible in core product surfaces
5. continue the persistent-intelligence roadmap beyond summaries

This plan intentionally separates remediation from expansion so execution does not mix cleanup work with deeper intelligence R&D.

---

## Current State Summary

### Strengths

- Broad local-first AI surface with meaningful differentiation
- Stronger backend intelligence than earlier app versions
- Durable conversation-summary persistence now exists
- Good service-layer test breadth in search, export, security, sync, plugins, and intelligence slices
- Clear Windows-native shell and page architecture

### Primary gaps

- product documentation materially lags the actual shipped surface
- some advanced pages/services are discoverable only partially or indirectly
- at least one feature path appears incompletely productized in UI
- several top-level pages still use patterns that will scale poorly with larger data sets
- core intelligence is stronger in backend plumbing than in visible day-to-day UX
- VM and workflow-level coverage is selective compared with service-level coverage

---

## Track A: Immediate Hardening and Remediation

This track is the recommended first execution path. It improves product quality, trust, discoverability, and maintainability without changing the strategic direction.

### Phase A1: Documentation Reality Alignment

**Goal:** bring product and architecture documentation up to current repo truth.

**Problems addressed**

- docs still describe a smaller app surface than the actual shell, pages, and services
- versioning and capability descriptions are stale
- newer surfaces like Analytics, workflows, sync, plugins, inbox, workspace profiles, and recent intelligence work are under-documented or missing
- some docs still describe old future-state assumptions that are no longer accurate

**Work**

- refresh `docs/README.md` to reflect the current app surface, page count, and modern intelligence capabilities
- refresh `docs/USER-GUIDE.md` so it matches the real navigation and current workflows
- refresh `docs/ARCHITECTURE.md` so service descriptions, page/viewmodel counts, testing notes, and intelligence sections reflect current code
- remove or rewrite stale “future” notes where the feature has already shipped
- add a short “current product map” section showing the major user-facing surfaces

**Acceptance criteria**

- the top three docs no longer undercount pages or view models
- current navigation surfaces are documented consistently
- durable conversation summaries and Analytics conversation intelligence are documented as shipped functionality
- no major feature page is omitted from the user guide

---

### Phase A2: Navigation and Discoverability Hardening

**Goal:** make all important surfaces reachable and consistent across nav, shortcuts, and command palette.

**Problems addressed**

- Analytics exists as a page and service surface but is not first-class in navigation
- command palette and keyboard navigation only cover part of the product map
- advanced surfaces feel hidden or secondary even when they are strategically important

**Work**

- add Analytics to shell navigation
- add Analytics and other major missing pages to `ShortcutCatalog`
- review the full `PageMap` against shell navigation and shortcut coverage
- ensure command palette exposure is consistent for every intended top-level page
- optionally group advanced pages more intentionally if the nav hierarchy feels crowded

**Acceptance criteria**

- every top-level page intended for end users is reachable from at least one clear surface
- Analytics is discoverable from the nav rail and command palette
- nav, command palette, and keyboard routing describe the same product map

---

### Phase A3: Incomplete Feature Productization

**Goal:** close the gap between backend capability and usable end-user workflow.

**Priority target: Collaborative Sync**

**Problems addressed**

- sync scope supports `SelectedCollections` in the view model/service path
- the UI does not expose a real collection-selection experience
- the scope selector itself is weakly wired and looks incomplete as a product flow

**Work**

- bind sync scope selection properly to the view model
- add a real selected-collections picker or checklist UX
- validate and persist selected collection IDs through a visible UI path
- surface sync state, sync scope, and conflict posture more clearly
- review Backup/Restore and Sync together to ensure the “data mobility” story is coherent

**Secondary review targets**

- Plugin Manager: confirm install/enable/disable/uninstall UX is strong enough for non-developer users
- Workflows: confirm builder/editor/run flow is documented and trustworthy enough to count as a tier-one feature

**Acceptance criteria**

- selected-collection sync is fully usable without hidden state
- scope-specific validation errors are tied to visible controls
- sync status/history/conflict states read like a complete feature, not a partially wired admin page

---

### Phase A4: Dashboard and Product Cohesion Upgrade

**Goal:** make the dashboard the real operating surface for Agent-X, not just a summary page.

**Problems addressed**

- dashboard currently focuses on basic stats and recent items
- newer intelligence surfaces live elsewhere and are not tied back into the main entry point
- system-level modules such as sync, inbox, workflows, and durable summary health are not unified into one operational view

**Work**

- add conversation-intelligence highlights to the dashboard
- surface inbox backlog, workflow activity, sync state, and indexing health more intentionally
- promote analytics insights into a lighter dashboard summary instead of isolating them entirely on the Analytics page
- keep Analytics as the deeper inspection surface while the dashboard becomes the overview surface

**Acceptance criteria**

- the dashboard reflects current product priorities, not just legacy metrics
- users can understand AI health, vault health, and intelligence health from one place
- the dashboard provides direct paths into the deeper operational pages

---

### Phase A5: Performance and Scale Hardening

**Goal:** remove obvious query/loading patterns that will degrade as the vault and conversation history grow.

**Problems addressed**

- dashboard loads full collections when it only needs recent items
- knowledge vault loads tags per document in a loop
- some page loads do more work than needed for first paint
- singleton data access patterns should be reviewed as data volume grows

**Work**

- add top-N recent document and recent conversation queries
- batch tag hydration in the vault instead of per-document follow-up calls
- review paging/virtualization opportunities on large list surfaces
- profile the most expensive first-load paths
- review long-lived data access usage around `AgentXDbContext`

**Acceptance criteria**

- major overview pages stop loading entire datasets for small summaries
- vault load avoids obvious N+1-style tag fetch patterns
- large libraries and conversation histories degrade more gracefully

---

### Phase A6: Coverage and Verification Expansion

**Goal:** raise confidence on the biggest user-facing workflows.

**Problems addressed**

- service-layer tests are much stronger than view-model/page workflow coverage
- some large surfaces do not have visible focused tests
- workflows, dashboard, vault, sync UI flow, plugin-manager flow, and workspace-profile flow deserve more direct verification

**Work**

- add focused tests for `DashboardViewModel`
- add focused tests for `KnowledgeVaultViewModel`
- add focused tests for `SyncSettingsViewModel`
- add focused tests for `PluginManagerViewModel`
- add coverage for workflow service/viewmodel behavior
- add regression tests around any nav/discoverability changes made in Phase A2

**Acceptance criteria**

- key user-facing orchestration view models are covered by focused tests
- newly productized flows ship with regression protection
- the test suite reflects current product risk, not only older service slices

---

## Track B: Next-Wave Enhancements

This track begins after the remediation work above establishes a cleaner and more trustworthy base.

### Phase B1: Persistent Intelligence Layer Continuation

**Goal:** move from durable summaries to richer long-horizon intelligence.

**Work**

- persist message-level embeddings
- add durable semantic recall across conversation history
- add clustering for related conversations or themes
- add temporal/trend materialization for long-horizon analytics
- build summary-group and trend-query services on top of durable storage

**Why this matters**

The summary snapshot layer is now a durable seam. Message embeddings and clustering should extend it, not replace it.

---

### Phase B2: Chat-Side Intelligence Visibility

**Goal:** expose persistent intelligence where users actually work: the chat surface.

**Work**

- add a chat-side context inspector
- show durable summary context and freshness in the conversation UI
- expose “why this answer had this context” explanations
- provide optional force-refresh or inspect-summary actions
- connect suggestions, memories, and durable summaries into one understandable context story

**Outcome**

Intelligence stops being hidden backend infrastructure and becomes a visible product differentiator.

---

### Phase B3: Workflow Product Maturity

**Goal:** decide whether workflows are a core product surface and then support that decision properly.

**Work**

- strengthen workflow templates and starter examples
- improve workflow execution visibility and result inspection
- add workflow-based connections to vault/search/export surfaces
- document clear use cases for non-technical users
- consider workflow analytics/history if the feature is meant to be strategic

**Decision gate**

If workflows are not meant to be strategic, simplify the feature. If they are strategic, raise documentation, testing, and UX quality accordingly.

---

### Phase B4: Unified Operations Surface

**Goal:** connect inbox, plugins, calendar, email, sync, workflows, and analytics into a coherent “intelligence operations” layer.

**Work**

- add a more unified operational overview
- surface connector health, sync health, ingestion backlog, and plugin state
- build cleaner handoffs between external data ingestion and vault/intelligence features
- reduce the feeling that advanced pages are isolated modules

---

### Phase B5: Advanced Retrieval and Explainability

**Goal:** deepen trust and usefulness in search, RAG, duplicates, summaries, and comparisons.

**Work**

- richer explanation for retrieved context and reranking
- more inspectable duplicate evidence and similarity reasoning
- deeper comparison traceability and synthesis provenance
- optional saved intelligence artifacts for later reference

---

## Recommended Execution Order

### Recommended order

1. Phase A1: Documentation Reality Alignment
2. Phase A2: Navigation and Discoverability Hardening
3. Phase A3: Incomplete Feature Productization
4. Phase A4: Dashboard and Product Cohesion Upgrade
5. Phase A5: Performance and Scale Hardening
6. Phase A6: Coverage and Verification Expansion
7. Phase B1 onward after remediation stabilizes

### Rationale

- documentation and discoverability fixes increase product trust immediately
- sync/productization work removes a real incomplete seam
- dashboard and cohesion work improve the app’s perceived quality without needing deep new infrastructure
- scale hardening prevents current breadth from turning into UX drag
- deeper intelligence work should build on a cleaner surface, not compete with cleanup work

---

## Immediate Slice Recommendation

If execution begins right away, the best next slice is:

### Slice 1

- refresh `docs/README.md`
- refresh `docs/USER-GUIDE.md`
- refresh `docs/ARCHITECTURE.md`
- add Analytics to shell navigation and shortcut/command-palette coverage
- verify the resulting product map is consistent across docs and UI discovery

### Why this first

- it resolves the highest-confidence drift already visible in the repo
- it improves both developer clarity and end-user discoverability
- it is lower-risk than starting immediately with deeper behavior changes
- it creates a clean baseline for later sync/dashboard/intelligence work

---

## Risks and Guardrails

### Risks

- broad cleanup work can sprawl if it is not sliced tightly
- documentation refresh can become a rewrite if not anchored to current code
- dashboard improvements can become a redesign project instead of a product-cohesion pass
- next-wave intelligence work can outpace UX clarity again if surfaced too late

### Guardrails

- keep each phase shippable on its own
- prefer existing-page improvements before adding new pages
- require verification and doc updates with each meaningful slice
- keep intelligence work paired with visible UX surfaces

---

## Acceptance Criteria for This Plan

- Agent-X has a documented, prioritized remediation-first roadmap
- immediate quality and discoverability work is separated from deeper future intelligence work
- the next implementation slice is obvious without re-analysis
- the roadmap reflects actual repo state rather than generic product advice

---

## Planned Follow-On Deliverables

After this planning document, execution should likely produce:

- a Track A implementation plan with task-by-task file targets
- a docs-and-discoverability execution slice
- a sync productization execution slice
- a dashboard cohesion execution slice
- a persistent-intelligence continuation plan for embeddings/clustering/trends
