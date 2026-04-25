# Vault And Plugin Drill-In Lifecycle Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Finish the remaining drill-in lifecycle gaps on Knowledge Vault and Plugin Manager so focused destination state is explicit, dismissible, and cleaned up when the user moves away.

## Problem

These two destinations still relied on implicit state transitions:

- Knowledge Vault could hide the focused banner while leaving the old document row tagged with Operations context
- Plugin Manager could lose focus on refresh with no explicit dismiss path, and its detail callout was still one-way

That made the recommendation review lifecycle inconsistent across destinations.

## Design

1. Knowledge Vault

- add a dismiss action to the focused document banner
- clear all focused document labels when the user dismisses the landing state
- clear stale focus labels when the user selects a different document or closes the preview

2. Plugin Manager

- add a dismiss action to the focused connector callout
- preserve focused connector state across plugin list reloads until dismissed
- restore the normal plugin-count footer message when dismiss clears the drill-in source
- clear focused plugin state when the user intentionally switches to a different connector

## Expected User Value

- no stale "focused from Operations" markers left behind
- predictable behavior after refresh
- one consistent review lifecycle across all drill-in destinations
