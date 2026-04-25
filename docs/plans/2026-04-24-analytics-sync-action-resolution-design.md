# Analytics And Sync Action Resolution Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Extend the drill-in lifecycle on Analytics and Sync Settings so a user can complete the requested review action and see that the landing state has been resolved.

## Problem

Analytics and Sync Settings already had focused landing banners, but they still stopped one step short of the stronger remediation pattern used elsewhere:

- Analytics could stage a conversation summary, but it had no exact-summary action to refresh that durable record from the banner itself
- Sync Settings could stage a sync-history record, but a successful manual sync still left the focused landing state intact

That made both pages feel more like inspection surfaces than guided-resolution surfaces.

## Design

1. Analytics

- add a focused-summary action that refreshes the exact durable summary for the staged conversation
- on success, clear the focused row state and show a resolved confirmation inside the conversation-intelligence section
- on no-op or failure, keep the focus state in place and show a truthful status line instead of pretending the review was resolved

2. Sync Settings

- treat a successful manual sync pass as the resolution action for a focused sync-history drill-in
- after the sync finishes and history reloads, clear the focused row state automatically
- replace the previous source-text status with a short resolved confirmation

## Expected User Value

- Analytics becomes actionable at the exact durable-summary level instead of just showing the staged record
- Sync Settings completes the review cycle cleanly after the user runs a fresh sync pass
- both pages now align better with the app’s broader “signal to action” direction
