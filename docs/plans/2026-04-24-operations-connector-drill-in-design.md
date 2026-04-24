# Operations Connector Drill-In Design

**Date:** 2026-04-24  
**Track:** `B4` unified operations surface  
**Scope:** make connector previews on the Operations page open the specific plugin they represent instead of only routing to the Plugin Manager page

## Goal

Close the remaining weak drill-in in the Operations hub by making connector preview rows behave like the newer inbox, sync, and workflow preview rows. A user should be able to click a connector preview from Operations and land on the matching plugin with an obvious “opened from Operations” affordance.

## Chosen approach

Use the existing staged drill-in pattern already introduced for inbox, sync, and workflow runs.

- extend the shared operations preview model so connector rows carry the plugin entity ID
- extend `IOperationsDrillInService` with a one-shot plugin request
- have `OperationsViewModel` stage the plugin request before navigating to `PluginManager`
- have `PluginManagerViewModel` consume the request on load, focus the matching plugin, move it to the top of the list, and expose a source label for UI affordances
- have `PluginManagerPage` auto-select the focused plugin and show a visible “Opened from Operations” badge in both the list row and detail header

## Why this approach

It keeps the behavior consistent with the rest of the Operations drill-in model, avoids inventing page-specific navigation state, and stays small enough to layer cleanly on top of the already-dirty local `B4` slice.

## Deferred

- conversation-summary-specific focus inside Analytics
- richer connector health or error detail in the Operations hub
- cross-surface drill-ins for every preview type beyond the current highest-value ones
