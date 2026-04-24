# Operations Page And Launch Icon Design

Date: 2026-04-23

## Goal

Promote `Operations` from a dashboard-only concept into a first-class app surface, and align the app launch icon with the new `icons/agent_x.png` asset.

## Approved Approach

Create a dedicated `Operations` page that reuses the new shared operations snapshot service as its top-level status layer, then let the Dashboard hand off into that page with an explicit `Open Operations` action.

This keeps Dashboard compact while giving the app one durable operations surface instead of scattering operations status across unrelated modules.

## Scope

- Add a dedicated `Operations` page and view model in `AgentX.App`.
- Reuse `IOperationsOverviewService` as the page's header/status source.
- Keep Analytics focused on deep analysis and history; do not merge Operations into Analytics.
- Add a visible Dashboard entry point into the new page.
- Register the page in shell navigation and keyboard/command discoverability.
- Set the app launch icon from the new `icons/agent_x.png` source art.

## Page Shape

`Operations` should live as a top-level page in the `INTELLIGENCE` section of the shell.

The page should read like an app-wide mission-control surface:

- a header with current app operations status
- five operational sections based on the shared snapshot
- direct handoffs into Analytics, Sync, Inbox, Workflows, and Plugin Manager

The page should not try to replace Analytics. It should answer: "what needs attention right now?" rather than "what are the long-term metrics?"

## Data Flow

- `IOperationsOverviewService` remains the shared status seam.
- Dashboard continues using the same snapshot for its compact card row.
- The new page expands the same signals into a larger, action-oriented surface.
- Analytics remains a separate deep-dive destination for trend and intelligence detail.

## Navigation And Discoverability

- Add `Operations` to `MainWindow` page registration and left navigation.
- Add a Dashboard button that opens the new page directly.
- Add command/shortcut discoverability so Operations shows up alongside Dashboard and Analytics.

## Launch Icon

Use `icons/agent_x.png` as the source asset for the app launch icon.

If the unpackaged WinUI build needs a `.ico` for the executable icon, generate that from the PNG and wire the project to the generated `.ico` while keeping the PNG as the source art in the repo.

## Testing

Add focused coverage for:

- `OperationsViewModel` snapshot mapping and navigation actions
- updated dashboard handoff to the new Operations page
- command/shortcut discoverability for the Operations surface

Verify the app build still succeeds with the new launch icon wiring.
