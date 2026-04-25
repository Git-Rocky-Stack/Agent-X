# Operations Status Badge Parity Implementation Plan

Date: 2026-04-24

## Task 1

Add a shared status-tone helper so phrase-based values such as `Current`, `Stale`, `5 pending`, `3 conflicts`, and `Enabled` resolve consistently.

## Task 2

Update `StatusToColorConverter` to use the shared helper and keep the existing brush palette.

## Task 3

Update the Operations conversation, sync, and connector preview templates to render status as badges.

## Task 4

Add focused helper coverage and run the available build and diff verification.
