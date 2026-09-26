# Sample Document Processor Plugin

A reference plugin for the AgentX plugin system. Implements the `DocumentProcessor` extension point (`IDocumentProcessorPlugin`): it reads plain-text and Markdown files, computes word/line/character counts, and extracts YAML frontmatter from Markdown.

## What It Does

- Reads plain-text (`.txt`, `.text`) and Markdown (`.md`) files
- Counts words, lines, and characters
- Extracts YAML-style frontmatter from Markdown files (key-value pairs between `---` delimiters)
- Returns the text to the host's import pipeline, with the counts and frontmatter as document metadata

Agent-X's built-in processors already read `.txt` and `.md`, and built-in processors always take precedence, so the host routes only `.text` files to this sample. That is by design: a plugin can add formats, not replace how built-in formats are read.

## Build

```bash
cd plugins/sample-plugin
dotnet build -p:Platform=x64
```

The output (`SamplePlugin.dll`, `SamplePlugin.deps.json`, and `manifest.json`, plus the Windows SDK projection assemblies that the host also ships) is written to `bin/x64/Debug/net8.0-windows10.0.22621.0/` (or `Release/` for release builds). AgentX.Core and Serilog are compile-time references only: the host provides them at run time, so the output deliberately contains no `AgentX.Core.dll` or `Serilog.dll`. Shipping a second copy would give the plugin a different `IPlugin` type than the host's.

## Package as .agentx-plugin

1. Build in Release configuration:
   ```bash
   dotnet build -c Release -p:Platform=x64
   ```
2. Create a zip archive with `manifest.json` at its root, next to the build output:
   ```bash
   cd bin/x64/Release/net8.0-windows10.0.22621.0
   zip -r sample-plugin.agentx-plugin manifest.json SamplePlugin.dll SamplePlugin.deps.json SamplePlugin.pdb
   ```
   Include any private dependency DLLs your own plugin needs; the host resolves them from the plugin folder.
3. The resulting `.agentx-plugin` file is ready for distribution.

## Install

1. Open AgentX.
2. Navigate to **Settings > Plugins > Install Plugin**.
3. Select the `.agentx-plugin` archive.
4. The Plugin Manager validates the manifest (including `minAppVersion` and `dependencies`), extracts the files, and records the plugin as **disabled**. Nothing is loaded yet.
5. Enable the plugin. The host loads the assembly, then calls `InitializeAsync` and `ActivateAsync`. An enabled plugin is activated again on every application start.

## Plugin API Overview

### Lifecycle

Every plugin implements `IPlugin` and follows a strict lifecycle:

| Method | When | Purpose |
|---|---|---|
| `InitializeAsync(IPluginContext)` | Once after assembly load (on enable, or at start for an enabled plugin) | Store context, read config, validate deps |
| `ActivateAsync()` | Right after initialization | Start background work, register extensions |
| `DeactivateAsync()` | User disables/uninstalls, or the app shuts down | Flush data, stop services, release resources (the host waits at most 10 seconds) |
| `Dispose()` | After deactivation | Free unmanaged resources |

The `IPluginContext` provides:
- **Services** -- scoped `IServiceProvider` with the approved host services (currently the Smart Inbox service)
- **PluginDataPath** -- per-plugin directory for config, caches, state
- **Logger** -- Serilog `ILogger` pre-enriched with plugin metadata

### Extension Points

Plugins declare a `PluginType`. Two types have a host integration today:

- **DocumentProcessor** -- adds support for new file formats. Implement `IDocumentProcessorPlugin`; while the plugin is active the host offers it every file no built-in processor claims.
- **DataConnector** -- pushes external items into the Smart Inbox through the inbox service in `IPluginContext.Services`.

The remaining types (**AiProvider**, **QuickAction**, **WorkflowStep**, **Theme**, **Custom**) are labels for the Plugin Manager; the host does not yet call such plugins.

### Permissions

`manifest.json` can list the permissions a plugin says it needs (`Documents`, `FileSystem`, `Network`, ...). They are informational only: the host does not ask for consent and does not enforce them. Plugins run in-process with the user's rights and are not sandboxed, so install only plugins you trust.

## Code Structure

```
plugins/sample-plugin/
  SamplePlugin.csproj    -- .NET 8 class library referencing AgentX.Core (compile time only)
  manifest.json          -- plugin identity, compatibility, permissions
  SamplePlugin.cs        -- IDocumentProcessorPlugin implementation with lifecycle and state guards
  SampleDocumentProcessor.cs -- document processing logic (word count, frontmatter)
  README.md              -- this file
```

### Key Patterns

- **Defensive state checks**: `ObjectDisposedException` after `Dispose()`, `InvalidOperationException` if called before `InitializeAsync()`.
- **Serilog logging**: all events go through `IPluginContext.Logger`, which is pre-enriched with the plugin ID.
- **No base class**: plain POCO implementing `IDocumentProcessorPlugin` directly, consistent with AgentX conventions.
- **ConfigureAwait(false)**: all `await` calls use `ConfigureAwait(false)` to avoid capturing the synchronization context in library code.
