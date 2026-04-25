# Dashboard Recommended Actions Design

Date: 2026-04-24

## Goal

Upgrade the Dashboard from a status-rich landing page into a guidance surface that tells the user what to do next.

## Problem

The dashboard already loads useful operational signals, but it still depends on the user to interpret those signals and choose the next action. That limits the visible value of the broader Agent-X intelligence model.

## Scope

- Add a compact "Recommended Next Steps" section to the dashboard.
- Derive recommendations only from signals the dashboard already loads:
  - AI readiness
  - indexing backlog
  - inbox backlog
  - sync posture
  - connector availability
  - workflow activity
  - conversation intelligence coverage
- Show up to three recommendations at a time.
- Route each recommendation directly to the right page.
- Keep the existing static quick-launch actions below the new section.

## Non-Goals

- No new backend service seams.
- No new persistence.
- No new recommendation history or dismissal model.
- No Operations-page redesign.

## Recommendation Model

Each dashboard recommendation should include:

- category label
- icon
- action title
- short explanatory detail
- CTA label
- target route

## Prioritization Rules

Urgent setup and remediation recommendations should appear before exploration and growth recommendations.

Example ordering:

1. finish AI setup
2. clear indexing backlog
3. triage inbox backlog
4. configure sync
5. connect a source
6. improve durable recall coverage
7. create or review workflows

When there are fewer than three urgent items, fill the remaining slots with growth-oriented recommendations such as Ask Your Files, Analytics, Workflows, or Operations.

## Validation

- ViewModel tests prove recommendation synthesis for both attention-heavy and healthy states.
- Command tests prove recommendation clicks route to the expected destination.
- The dashboard still renders the existing quick-launch row unchanged beneath the new guidance section.

