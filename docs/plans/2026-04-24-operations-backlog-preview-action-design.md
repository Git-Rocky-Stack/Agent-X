# Operations Backlog Preview Action Design

**Date:** 2026-04-24  
**Track:** `B4` unified operations surface  
**Scope:** add the first safe backlog remediation action to the Operations hub

## Goal

Extend the Operations page beyond summary refresh and sync remediation by adding one direct ingestion action that meaningfully reduces inbox friction. The first action should be safe, non-destructive, and already backed by a proven page-level workflow.

## Chosen action

`Generate Previews` on the `Ingestion Backlog` card.

This action reuses the existing inbox-wide AI preview generation flow and then reloads the Operations snapshot so the backlog card and preview rows reflect the latest state.

## Architecture

Use the existing `IOperationsActionService` seam rather than adding another Operations-specific code path.

- add `GenerateInboxPreviewsAsync` to `IOperationsActionService`
- implement it in `OperationsActionService` by:
  - checking pending inbox count first
  - returning a no-op success message when the backlog is clear
  - otherwise calling `IInboxService.GenerateAllPreviewsAsync`
- update `OperationsViewModel` with:
  - `GenerateInboxPreviewsCommand`
  - command-state notifications tied to backlog and loading state
  - post-action snapshot reload and shared feedback strip reuse
- place the button beside `Open Inbox` on the backlog card

## Why this action first

It is the safest real backlog remediation already present in the product. It does not change triage decisions, does not delete or accept data, and makes the next user decision inside Inbox materially easier.

## Deferred

- destructive backlog actions such as batch reject or cleanup
- accept-all from Operations
- richer backlog-specific progress indicators
