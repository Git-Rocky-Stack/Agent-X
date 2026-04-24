# Operations Overview Unification Design

Date: 2026-04-23

## Goal

Start `B4` by turning the Dashboard operations area into a coherent intelligence-operations surface instead of a set of unrelated module cards.

## Approved Approach

Begin with a dashboard-first shared summary seam.

This slice should:

- add an app-layer operations overview service
- aggregate conversation intelligence, sync health, ingestion backlog, workflow health, and connector/plugin state into one snapshot
- use that snapshot to upgrade the dashboard operations section

This keeps the first `B4` pass incremental while creating a reusable foundation for later operations surfaces.

## Scope

- Add a shared operations snapshot service in the app layer.
- Replace the current dashboard-specific operations stitching logic with that service.
- Add a connector/plugin signal and make the inbox card read as ingestion backlog.
- Keep the work dashboard-first; do not expand Analytics or other pages in the same pass.

## Dashboard Shape

The `Operations Overview` section should become a coherent 5-signal surface:

- conversation intelligence
- sync health
- connectors and plugins
- ingestion backlog
- workflow activity

The layout should feel intentional and compact rather than like a pile of unrelated feature cards.

## Data Sources

- conversation intelligence: `IAnalyticsService`
- workflow health: workflow intelligence overview from `IAnalyticsService`
- ingestion backlog: `IInboxService`
- sync health: `ISyncService`
- connectors and plugin state: `IPluginService`

The workflow card should continue using the new analytics-backed workflow overview instead of `WorkflowEntity.RunCount` as its main signal.

## Behavior

- Each operations signal should remain useful even when one source has no data yet.
- Empty states should guide the user to the next useful action.
- Connector/plugin state should reflect installed and enabled data connectors, not placeholder copy.
- The dashboard should expose clear handoffs to Analytics, Sync, Inbox, Workflows, and Plugin Manager.

## Testing

Add focused coverage for:

- operations overview service snapshot mapping
- dashboard view-model mapping from the shared operations snapshot
- empty-state behavior for the new connector/plugin and ingestion framing
