# Quick Actions Contextual Guidance Design

Date: 2026-04-24

## Goal

Evolve Quick Actions from a static tool tray into a contextual action surface that recommends the most useful next step based on the selected document and current intake state.

## Problem

Quick Actions already exposes valuable tools, but it does not currently guide the user toward the right tool for the current document, indexing state, or intake backlog. That makes the page feel generic instead of context-aware.

## Scope

- Add a compact contextual-actions section above the tab strip.
- Derive recommendations from:
  - selected document state
  - available document count
  - current intake/connectors posture from the shared Operations snapshot
- Support both:
  - in-page contextual actions such as summarize, extract key points, semantic duplicate scan, and organization analysis
  - navigational actions such as opening Inbox, Vault, or Plugin Manager
- Keep the existing tab content and tool implementations intact.

## Non-Goals

- No new intelligence services.
- No major tab redesign.
- No new persisted recommendation state.
- No document drill-in or new deep-link model in this slice.

## Interaction Rules

- If the selected document is not ready, recommend fixing or reviewing its indexing state before content actions.
- If the selected document is ready, prioritize direct content actions.
- When intake backlog exists, recommend triage alongside document actions.
- When no connectors are active, recommend enabling a source.
- Keep the section compact and cap the visible recommendations.

## Validation

- ViewModel tests prove recommendation synthesis for ready-document and setup-heavy states.
- Command tests prove contextual actions either execute the correct in-page action or navigate to the correct page.

