# Drill-In Action Resolution Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Finish the recommendation review lifecycle so focused drill-in state resolves itself after the user acts on the requested target.

## Problem

The landing banners and row-level focus states now persist correctly, but several acted-on destinations still left the Operations context hanging after the work was already completed:

- Inbox kept the drill-in state after accepting the focused intake item
- Plugin Manager kept the drill-in state after enabling the focused connector
- Workflow Builder kept the drill-in state after saving or exporting a focused stored run

That left the user in an ambiguous state where the page still looked like it needed review even though the primary action had already been completed.

## Design

1. Inbox

- when the focused inbox item is accepted successfully, clear the landing banner and row highlight automatically
- replace the Operations source label with a short resolved confirmation

2. Plugin Manager

- when the focused connector is enabled successfully, clear the focused connector state automatically
- apply the same resolution behavior for bulk-enable when the selected set includes the focused connector
- replace the Operations source label with a short resolved confirmation

3. Workflow Builder

- when a focused stored run is saved to Knowledge Vault, clear the focused stored-run landing state automatically
- when a focused stored run is exported, clear the focused stored-run landing state automatically
- support both reopened current-result context and explicit row actions
- replace the Operations source label with a short resolved confirmation

## Expected User Value

- review flows end cleanly after the requested action is taken
- no stale Operations banners remain after the user resolves the underlying task
- destination pages behave more like guided remediation surfaces instead of passive drill-in viewers
