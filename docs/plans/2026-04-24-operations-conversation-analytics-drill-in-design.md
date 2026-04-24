# Operations Conversation Analytics Drill-In Design

**Date:** 2026-04-24  
**Track:** `B4` unified operations surface  
**Scope:** make Operations conversation previews open Analytics with the matching recent summary visibly focused

## Goal

Close the remaining page-level-only drill-in on the Operations hub. Clicking a conversation preview should not just open Analytics; it should land with the matching recent durable summary promoted and clearly marked as the item that was opened from Operations.

## Chosen approach

Reuse the staged operations drill-in pattern already used for inbox, sync, workflow runs, and plugin focus.

- extend `OperationsConversationPreview` with the source `ConversationId`
- add a one-shot `OperationsConversationDrillInRequest` to the shared operations drill-in service
- update `OperationsViewModel` to stage that request before navigating to `Analytics`
- let `AnalyticsViewModel` consume the request during `LoadConversationIntelligenceAsync`, move the matching summary to the top, and mark it with a source label
- expose the source label in the Analytics summary card template as an accent badge

## Why this approach

It keeps `Operations` as the thin routing surface and keeps summary focus logic inside Analytics where the data already exists. That avoids adding cross-page state to the shell or inventing a second analytics-specific navigation channel.

## Deferred

- deeper Analytics-side drill-ins beyond recent summaries
- auto-scrolling to an arbitrary summary position instead of promoting the target to the top
- conversation-preview drill-ins from other surfaces outside Operations
