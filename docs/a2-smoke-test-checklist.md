# A2 Task 12: Smoke Test Checklist for Keyboard Power Mode

This checklist verifies all keyboard interactions for the Keyboard Power Mode feature.

## Command Palette (Ctrl+Shift+P)

- [ ] **1. Ctrl+Shift+P opens palette, focus in query box**
  - Press Ctrl+Shift+P
  - Verify palette appears with focus in query input
  - Press Escape to dismiss

- [ ] **2. Type "doc" → results filter to document-related commands**
  - Open palette with Ctrl+Shift+P
  - Type "doc"
  - Verify only document-related commands appear
  - Press Escape to dismiss

- [ ] **3. Arrow keys navigate results, Enter executes selected item, palette closes**
  - Open palette with Ctrl+Shift+P
  - Use arrow keys to navigate through results
  - Press Enter on selected item
  - Verify palette closes and command executes

## Jump-To (Ctrl+P)

- [ ] **4. Ctrl+P opens Jump-To dialog with documents + conversations + pages listed**
  - Press Ctrl+P
  - Verify dialog appears with documents, conversations, and pages
  - Verify items are properly categorized
  - Press Escape to dismiss

- [ ] **5. Type a document name → candidates filter; Enter opens that document**
  - Open Jump-To with Ctrl+P
  - Type name of a document
  - Verify candidates filter to matching documents
  - Press Enter on document
  - Verify document opens

## Cheatsheet (F1 / Ctrl+Shift+?)

- [ ] **6. F1 opens cheatsheet showing all shortcuts grouped by category**
  - Press F1
  - Verify cheatsheet appears
  - Verify shortcuts are grouped by category
  - Press Escape to dismiss

- [ ] **7. Ctrl+Shift+? also opens cheatsheet**
  - Press Ctrl+Shift+?
  - Verify cheatsheet appears
  - Press Escape to dismiss

- [ ] **8. Global shortcuts and page-scoped shortcuts both appear**
  - Open cheatsheet with F1
  - Verify both global and page-scoped shortcuts are shown
  - Verify proper categorization
  - Press Escape to dismiss

- [ ] **9. Close with Escape**
  - Open cheatsheet
  - Press Escape
  - Verify cheatsheet closes

## Navigation shortcuts

- [ ] **10. Ctrl+Shift+D1 → Navigate to Documents (KnowledgeVault)**
  - Press Ctrl+Shift+D1
  - Verify navigation to KnowledgeVault page

- [ ] **11. Ctrl+Shift+D2 → Navigate to Chat**
  - Press Ctrl+Shift+D2
  - Verify navigation to Chat page

- [ ] **12. Ctrl+Shift+D3 → Navigate to Settings**
  - Press Ctrl+Shift+D3
  - Verify navigation to Settings page

## Page-scoped shortcuts

- [ ] **13. On KnowledgeVault page, F5 refreshes document list**
  - Navigate to KnowledgeVault page
  - Press F5
  - Verify document list refreshes

- [ ] **14. On Chat page, Ctrl+Enter sends message (if implemented)**
  - Navigate to Chat page
  - Type a message
  - Press Ctrl+Enter
  - Verify message sends (if implemented)

- [ ] **15. On Settings page, check page-scoped shortcuts appear in cheatsheet**
  - Navigate to Settings page
  - Open cheatsheet with F1
  - Verify page-scoped shortcuts for Settings appear

## Regression checks

- [ ] **16. Ctrl+K still opens existing Command Palette (not consumed by chord system)**
  - Press Ctrl+K
  - Verify original Command Palette opens

- [ ] **17. No multi-step chord prefixes are seeded — single-key shortcuts work immediately**
  - Press any single-key shortcut (F1, F5, etc.)
  - Verify it works without needing prefix keys

- [ ] **18. Escape dismisses any open dialog without side effects**
  - Open any dialog (palette, Jump-To, cheatsheet)
  - Press Escape
  - Verify dialog closes without affecting app state

## Pre-existing functionality

- [ ] **19. App launches without errors**
  - Launch app
  - Verify no errors or crashes

- [ ] **20. Navigation between pages works normally**
  - Click through all navigation items
  - Verify pages load correctly

- [ ] **21. Chat sends messages normally**
  - Go to Chat page
  - Type and send a message
  - Verify message appears

- [ ] **22. Document import works normally**
  - Attempt to import a document
  - Verify import process works

---

## Pass/Fail Summary

### Test Results

**Total Tests:** 22  
**Passed:** [ ]  
**Failed:** [ ]  
**Blocked:** [ ]

### Notes

- Record any observed issues or unexpected behavior
- Note if any shortcuts conflict with existing OS shortcuts
- Document any accessibility concerns

### Test Environment

- OS: [ ]
- Browser/Platform: [ ]
- Agent-X Build: [ ]
- Date: [ ]

### Tester Signature

_________________________