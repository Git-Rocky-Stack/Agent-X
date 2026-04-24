# Operations Summary Attention Rollup Design

Date: 2026-04-24

## Goal

Keep the top Operations summary truthful as the page gains richer remediation signals. Imported-document health and disabled connector rows should affect the summary, not remain buried in card previews while the header says everything is normal.

## Scope

- Count imported documents with `Needs Attention` health as one Operations attention area.
- Count connector previews that can be enabled from Operations as one Operations attention area.
- Reuse the existing concise summary detail string instead of adding new header chrome.

## Non-Goals

- No severity scoring.
- No new card layout.
- No background polling behavior.
- No changes to the underlying imported-document health rules.

## Validation

- ViewModel coverage proves the summary headline and detail mention imported-document indexing and disabled connectors.
