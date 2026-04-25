# Dashboard Recommended Actions Implementation Plan

Date: 2026-04-24

## Task 1

Add a small dashboard recommendation display model and expose a ViewModel collection plus navigation command for recommended actions.

## Task 2

Synthesize up to three recommendations from the dashboard's already-loaded signals, prioritizing setup and remediation before growth suggestions.

## Task 3

Upgrade the dashboard quick-actions area into:

- Recommended Next Steps
- Quick Launch

The new recommendations should render as compact cards and keep the existing static launch buttons below them.

## Task 4

Add focused `DashboardViewModelTests` coverage for recommendation synthesis and recommendation navigation, then run the available validation commands.
