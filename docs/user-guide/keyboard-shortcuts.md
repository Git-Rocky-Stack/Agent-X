# Agent-X Keyboard Shortcuts

**Power user navigation guide**

Every shortcut on this page is one Agent-X actually registers. Global shortcuts are seeded by `ShortcutCatalog` (`src/AgentX.App/Services/ShortcutCatalog.cs`); page shortcuts are registered by the page while it is open. Press `F1` anywhere for the live Keyboard Shortcuts dialog, which lists the global shortcuts plus the ones the current page registers.

---

## Table of Contents

1. [Global Shortcuts](#global-shortcuts)
2. [Navigation Shortcuts](#navigation-shortcuts)
3. [Chat Shortcuts](#chat-shortcuts)
4. [Knowledge Vault Shortcuts](#knowledge-vault-shortcuts)
5. [Search and Other Pages](#search-and-other-pages)
6. [Quick Chat](#quick-chat)
7. [Editing Shortcuts](#editing-shortcuts)
8. [Accessibility Shortcuts](#accessibility-shortcuts)
9. [Platform-Specific Notes](#platform-specific-notes)

---

## Global Shortcuts

### Application-Level

| Shortcut | Action | Notes |
|----------|--------|-------|
| `Ctrl+K` | Open Command Palette | Every page on the navigation rail, three actions, and the current page's shortcuts |
| `Ctrl+Shift+P` | Open Command Palette | Alternate chord |
| `Ctrl+P` | Jump To | Open a page, a document, or a conversation by name |
| `F1` | Keyboard Shortcuts | Global shortcuts plus the current page's |
| `Ctrl+Shift+/` | Keyboard Shortcuts | Same as `F1`; the dialog shows it as `Ctrl+Shift+?` |
| `Ctrl+,` | Open Settings | |
| `Ctrl+N` | New Conversation | Opens AI Chat on a fresh conversation |
| `Win+Shift+A` | Quick Chat | A system-wide hotkey: it works while Agent-X runs, also when its window is hidden in the notification area. If another program already uses it, Agent-X logs that and the hotkey does nothing |

These work from the main window, also while a text box has the focus. They do not fire inside dialogs (Jump To, the Keyboard Shortcuts dialog, confirmations).

### Inside the Command Palette

| Shortcut | Action |
|----------|--------|
| Type | Filter the list (fuzzy match: the letters in order, not necessarily adjacent, so "knv" finds Knowledge Vault) |
| `Up` / `Down` | Move through results (`Tab` / `Shift+Tab` also work) |
| `Enter` | Run the selected command or open the selected page |
| `Esc` | Close the palette |

The palette lists the rail's pages under their rail names and groups, then three actions (New Conversation, Import Files, Toggle Theme), then ON THIS PAGE with the shortcuts the current page registers.

### Inside Jump To

| Shortcut | Action |
|----------|--------|
| Type | Filter pages, documents and conversations |
| `Up` / `Down` | Move through results |
| `Enter` | Open the selected item |
| `Esc` | Close Jump To |

Jump To lists every page (including Onboarding, which is how you reopen the first-run wizard), up to 50 documents and up to 50 conversations. Pages appear under their internal names in English, which differ from the rail in places: for example "Chat" (AI Chat), "Search" (Semantic Search), "Digest" (Weekly Digest), "Inbox" (Smart Inbox) and "Sync Settings" (Collaborative Sync).

### The Keyboard Shortcuts dialog

`F1` (or `Ctrl+Shift+/`) lists the global shortcuts grouped as Navigation, Quick Access, Actions and Help, and adds the group of the page you are on, marked "Current page". It is a read-only list; there is no search box.

---

## Navigation Shortcuts

### Main Navigation

| Shortcut | Page |
|----------|------|
| `Ctrl+D` | Dashboard |
| `Ctrl+I` | Knowledge Vault |
| `Ctrl+F` | Semantic Search |
| `Ctrl+Shift+F` | Semantic Search (alternate) |
| `Ctrl+G` | Knowledge Graph |
| `Ctrl+Shift+W` | Workflows |
| `Ctrl+Shift+E` | Web Import |
| `Ctrl+Shift+A` | Analytics |
| `Ctrl+Shift+O` | Operations |
| `Ctrl+,` | Settings |

`Ctrl+I` only opens the Knowledge Vault. To open the file picker as well, use Import Files in the Command Palette.

### Quick-Access Slots

`Ctrl+1` through `Ctrl+9` open nine pages in this fixed order (it is not the order of the navigation rail):

| Shortcut | Page |
|----------|------|
| `Ctrl+1` | Dashboard |
| `Ctrl+2` | AI Chat |
| `Ctrl+3` | Ask Your Files |
| `Ctrl+4` | Semantic Search |
| `Ctrl+5` | Knowledge Vault |
| `Ctrl+6` | Collections |
| `Ctrl+7` | Workflows |
| `Ctrl+8` | Model Manager |
| `Ctrl+9` | Settings |

Every other page is one `Ctrl+K` (Command Palette) or `Ctrl+P` (Jump To) search away.

---

## Chat Shortcuts

### Conversation Management

These are registered by the AI Chat page and work while it is open.

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+N` | New conversation |
| `Ctrl+B` | Show or hide the conversation pane |
| `Ctrl+N` | New conversation (global; also works from any other page) |

In the conversation list, `Enter` opens the focused conversation, and `Shift+F10` or the Menu key opens its menu (Pin or Unpin conversation, Delete conversation), like a right-click.

### Composer

| Shortcut | Action |
|----------|--------|
| `Enter` | Send the message |
| `Shift+Enter` | Insert a line break |

Copy, regenerate, edit, delete, branch and the Good response / Poor response ratings are buttons on each message; they have no keyboard shortcuts. The microphone button records speech into the message box; right-click it to transcribe an audio file instead.

---

## Knowledge Vault Shortcuts

Registered by the Knowledge Vault page while it is open.

| Shortcut | Action |
|----------|--------|
| `F5` | Refresh the document list |
| `Ctrl+I` | Open the Knowledge Vault (global) |

Importing, deleting, re-indexing and collection assignment are done with the page's buttons and multi-select; they have no dedicated shortcuts.

---

## Search and Other Pages

| Page | Shortcut | Action |
|------|----------|--------|
| Semantic Search | `Ctrl+F` | Open the page (global) |
| Semantic Search | `Enter` | Run the search in the search box |
| Ask Your Files | `Enter` | Ask the question; `Shift+Enter` inserts a line break |
| Dashboard | `Enter` | In the search box, open Semantic Search with the query |
| Past Self | `Enter` | In the topic box, run Search Past Self |
| Settings | `Ctrl+S` | Save settings (registered by the Settings page while it is open) |

The search mode (Semantic, Keyword, Hybrid) and filters are chosen on the page; they have no shortcuts.

---

## Quick Chat

`Win+Shift+A`, or Quick Chat in the tray icon's menu, opens a small always-on-top window for a one-off question to the active AI provider.

| Shortcut | Action |
|----------|--------|
| `Enter` | Ask |
| `Esc` | Stop the answer and close the window |

---

## Editing Shortcuts

Text boxes throughout the app (the chat composer, search boxes, forms) use the standard Windows text editing keys.

### Text Editing

| Shortcut | Action |
|----------|--------|
| `Ctrl+C` | Copy |
| `Ctrl+X` | Cut |
| `Ctrl+V` | Paste |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+A` | Select all text in the box |

### Cursor Movement

| Shortcut | Action |
|----------|--------|
| `Left` / `Right` | Move cursor left/right |
| `Ctrl+Left` / `Ctrl+Right` | Move by word |
| `Home` | Start of line |
| `End` | End of line |
| `Ctrl+Home` | Start of text |
| `Ctrl+End` | End of text |

### Text Selection

| Shortcut | Action |
|----------|--------|
| `Shift+Left` / `Shift+Right` | Select characters |
| `Ctrl+Shift+Left` / `Ctrl+Shift+Right` | Select words |
| `Shift+Home` | Select to line start |
| `Shift+End` | Select to line end |

The global shortcuts take priority inside text boxes too: `Ctrl+F`, `Ctrl+I` or `Ctrl+D` in a text box opens that page.

---

## Accessibility Shortcuts

### System Accessibility

These are Windows shortcuts; they work in Agent-X like in any other app.

| Shortcut | Action |
|----------|--------|
| `Windows+Ctrl+Enter` | Toggle Narrator |
| `Windows+U` | Open Accessibility settings |
| `Windows++` / `Windows+-` | Magnifier zoom in / out |
| `Left Alt+Left Shift+Print Screen` | Toggle high contrast |

### Application Accessibility

Agent-X has no zoom shortcuts of its own; use Windows text size or display scaling instead. Every page is reachable from the keyboard: `Tab` moves between controls, and `Ctrl+K` or `Ctrl+P` reach any page. Under a Windows contrast theme the app switches to the system contrast colors.

---

## Platform-Specific Notes

### Windows 11

| Shortcut | Notes |
|----------|-------|
| `Windows+Shift+S` | System screenshot, works within Agent-X |

### Keyboard Layouts

Agent-X shortcuts are defined for US keyboard layouts. On other layouts the punctuation chords (`Ctrl+,` and `Ctrl+Shift+/`) can sit on different physical keys; `F1` and the navigation rail work on every layout.

---

## Customization

Shortcuts are fixed in this release: there is no settings page for rebinding keys, adding chords, or resetting a shortcut map. The Keyboard Shortcuts dialog (`F1`) always shows exactly what is bound.

---

## Quick Reference Card

Print this for quick reference:

```
Agent-X Keyboard Shortcuts
+-------------------------------------------+
| Everywhere                                |
|   Ctrl+K        Command Palette           |
|   Ctrl+P        Jump To                   |
|   F1            Keyboard Shortcuts        |
|   Ctrl+N        New Conversation          |
|   Ctrl+,        Settings                  |
|   Win+Shift+A   Quick Chat                |
+-------------------------------------------+
| Pages                                     |
|   Ctrl+D        Dashboard                 |
|   Ctrl+I        Knowledge Vault           |
|   Ctrl+F        Semantic Search           |
|   Ctrl+G        Knowledge Graph           |
|   Ctrl+Shift+W  Workflows                 |
|   Ctrl+Shift+E  Web Import                |
|   Ctrl+Shift+A  Analytics                 |
|   Ctrl+Shift+O  Operations                |
|   Ctrl+1..9     Quick-access slots        |
+-------------------------------------------+
| On the page                               |
|   Chat:   Ctrl+Shift+N new, Ctrl+B pane   |
|   Chat:   Enter send, Shift+Enter newline |
|   Vault:  F5 refresh                      |
|   Settings: Ctrl+S save                   |
+-------------------------------------------+
```

---

## Tips for Power Users

### Workflow Optimization

1. **Keep hands on keyboard**
   - Minimize mouse usage
   - Learn 3-4 essential shortcuts first

2. **Use command palette**
   - `Ctrl+K` is faster than menu navigation
   - Type a few letters in order; they need not be adjacent

3. **Jump straight to your material**
   - `Ctrl+P` opens a specific document or conversation, not just a page

4. **Use multi-select**
   - Select several documents in the Knowledge Vault for batch operations

---

*Last updated: 2026-09-27*
