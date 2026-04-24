# Operations Page Preview Deepening Design

**Date:** 2026-04-23
**Track:** B4 Unified Operations Surface
**Status:** Approved for implementation

## Goal

Deepen the dedicated Operations page so it reads like a real cross-module control surface instead of a second summary dashboard. The page should keep the current high-level status cards, then expose compact previews from the underlying operational systems: conversation intelligence, sync, inbox, workflows, and plugins.

## Design

### Architecture

- Keep `IOperationsOverviewService` as the app-layer aggregation seam for both Dashboard and Operations.
- Extend `OperationsOverviewSnapshot` with UI-ready preview collections rather than pushing more cross-service orchestration into `OperationsViewModel`.
- Continue to use the existing core services and analytics queries:
  - `IAnalyticsService` for conversation and workflow intelligence
  - `IInboxService` for bounded pending-item previews
  - `ISyncService` for recent sync history
  - `IPluginService` for connector/plugin previews

### UX

- Preserve the existing top summary/status cards and navigation handoffs.
- Add compact preview rows directly inside each card:
  - recent durable summary rows
  - recent sync passes
  - pending inbox items
  - recent workflow runs
  - connector/plugin preview rows
- Use placeholder rows when a subsystem has no recent records so the page remains informative on fresh installs.

### Data shaping

- Preview rows should already be UI-ready when they leave `OperationsOverviewService`.
- Each row uses a small, repeatable shape:
  - title
  - status
  - detail
- Formatting stays concise and operational:
  - relative freshness
  - short status labels
  - bounded preview text

### Testing

- Extend `OperationsOverviewServiceTests` to prove preview mapping for real data.
- Extend `OperationsViewModelTests` to prove the richer snapshot is propagated to the UI layer.
- Rebuild the WinUI app after the XAML update.

## Non-goals

- no new persistence
- no new shell navigation
- no dashboard redesign in this slice
- no per-module live refresh loops beyond the existing Operations load path
