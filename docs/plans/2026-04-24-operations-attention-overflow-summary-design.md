# Operations Attention Overflow Summary Design

Date: 2026-04-24

## Goal

Keep the Operations header concise without hiding that additional operational issues exist when more than three attention areas are active.

## Scope

- Preserve the existing first-three summary detail format.
- Append a compact overflow label such as `3 more` when additional attention areas are active.
- Keep detailed investigation in the existing cards and drill-ins.

## Non-Goals

- No severity ranking changes.
- No expandable header control.
- No new attention model or persistence.

## Validation

- ViewModel coverage proves the header reports the total attention count and compact overflow label.
