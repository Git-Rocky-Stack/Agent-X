# Dashboard Workflow Intelligence Mirror Design

Date: 2026-04-23

## Goal

Mirror the newly added workflow analytics story onto Dashboard without duplicating the full Analytics module.

## Approved Scope

- Keep the existing `Workflow Activity` card on Dashboard.
- Replace its coarse, partly cached workflow summary with the new analytics-backed workflow intelligence overview.
- Keep the surface lightweight: headline, workflow health summary, compact supporting insights, and a short top-workflow detail.
- Do not add a second workflow panel or replicate the Analytics lists in full.

## Data Source

Dashboard should consume the existing workflow analytics overview added for Analytics:

- total runs
- success rate
- average run duration
- active workflows in the recent window
- top workflow by actual run history

`WorkflowEntity.RunCount` should not remain the card's primary signal.

## UI Shape

The existing dashboard workflow card should evolve into a distilled status card:

- headline: total workflow runs
- main status: success-rate oriented health line
- compact supporting badges or sublabels for recent activity and average duration
- detail line naming the top workflow or guiding the user to start automation

The result should feel like a homepage mirror of Analytics, not a second workflow console.

## Testing

Extend focused `DashboardViewModel` coverage to verify:

- workflow card values map from the analytics overview
- empty-state guidance remains sensible when no runs exist
- existing operations overview navigation remains intact
