# Drill-In Dismiss Parity Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Finish the drill-in landing-banner pattern by giving Inbox and Workflows the same explicit clear-after-review affordance already added to the newer Analytics and Sync destinations.

## Problem

Inbox and Workflow Builder had visible drill-in landing banners, but neither surface exposed a direct dismiss action.

That left the post-navigation state uneven:

- newer destinations could be cleared explicitly
- older destinations relied on incidental state changes
- Workflow Builder could leave a stale row-level "Opened from Operations" marker behind after the banner source label was cleared

## Design

Add a dismiss button to both landing banners and make dismissal clear both layers of focus:

- the top-level landing banner
- the row-level "Opened from Operations" marker

Inbox also keeps the focused item pinned on list reload while the target still exists, so the new dismiss action remains meaningful after a refresh.

## Rules

1. Dismiss only clears landing state, not the underlying inbox item or workflow run.
2. If the current status message still mirrors the drill-in source text, dismiss clears it too.
3. Workflow row focus must never outlive the landing banner state.
4. Keep this slice narrow to Inbox and Workflows only.
