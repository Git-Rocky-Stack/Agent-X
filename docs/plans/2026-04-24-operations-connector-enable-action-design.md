# Operations Connector Enable Action Design

**Date:** 2026-04-24  
**Track:** `B4` unified operations surface  
**Scope:** add one safe connector remediation action directly on disabled connector previews in Operations

## Goal

Make the `Connectors & Plugins` card behave like a real remediation surface for the narrow case that is already safe from the top level: a connector is installed but disabled.

## Chosen scope

- show `Enable Connector` only on disabled connector previews
- keep enabled connectors as drill-in only, routing to `Plugin Manager`
- reload the Operations snapshot after enable so the preview row and card headline update immediately
- reuse the existing action feedback strip instead of adding connector-specific chrome

## Architecture

Use the existing `IOperationsActionService` seam rather than calling `IPluginService` directly from the view model.

- add `EnableConnectorAsync(long pluginId)` to `IOperationsActionService`
- implement it in `OperationsActionService` using `IPluginService`
- validate the plugin exists and is a `DataConnector` before enabling it
- extend `OperationsConnectorPreview` with explicit UI flags instead of inferring state from status text

## UX

- disabled connector previews get two actions:
  - `Enable Connector`
  - `Open Plugin`
- enabled connector previews keep only `Open Plugin`
- no disable or uninstall actions from Operations in this slice

## Deferred

- plugin disable from Operations
- non-connector plugin enable from Operations
- bulk connector actions
- connector-specific progress or health detail beyond the shared action strip
