# Sample Document Processor Plugin

A reference plugin for the AgentX plugin system. Implements a `DocumentProcessor` extension point that reads `.txt` and `.md` files, computes word/line/character counts, and extracts YAML frontmatter from Markdown.

## What It Does

- Reads plain-text (`.txt`) and Markdown (`.md`) files
- Counts words, lines, and characters
- Extracts YAML-style frontmatter from Markdown files (key-value pairs between `---` delimiters)
- Returns results as a `ProcessedDocument` record with all metrics and metadata

## Build

```bash
cd plugins/sample-plugin
dotnet build
```

The output assembly (`SamplePlugin.dll`) is written to `bin/Debug/net8.0-windows10.0.22621.0/` (or `Release/` for release builds).

## Package as .agentx-plugin

1. Build in Release configuration:
   ```bash
   dotnet build -c Release
   ```
2. Create a zip archive containing the build output **and** `manifest.json`:
   ```bash
   cd bin/Release/net8.0-windows10.0.22621.0
   zip -r sample-plugin.agentx-plugin manifest.json SamplePlugin.dll SamplePlugin.pdb
   ```
3. The resulting `.agentx-plugin` file is ready for distribution.

## Install

1. Open AgentX.
2. Navigate to **Settings > Plugins > Install Plugin**.
3. Select the `.agentx-plugin` archive.
4. The Plugin Manager validates the manifest, extracts files, and calls `InitializeAsync` + `ActivateAsync`.

## Plugin API Overview

### Lifecycle

Every plugin implements `IPlugin` and follows a strict lifecycle:

| Method | When | Purpose |
|---|---|---|
| `InitializeAsync(IPluginContext)` | Once after assembly load | Store context, read config, validate deps |
| `ActivateAsync()` | User enables plugin | Start background work, register extensions |
| `DeactivateAsync()` | User disables/uninstalls | Flush data, stop services, release resources |
| `Dispose()` | After deactivation | Free unmanaged resources |

The `IPluginContext` provides:
- **Services** -- scoped `IServiceProvider` with approved host services
- **PluginDataPath** -- per-plugin directory for config, caches, state
- **Logger** -- Serilog `ILogger` pre-enriched with plugin metadata

### Extension Points

Plugins declare a `PluginType` that determines how the host integrates them:

- **DocumentProcessor** -- adds support for new file formats
- **AiProvider** -- integrates a custom AI backend
- **QuickAction** -- adds single-click commands to the Quick Actions panel
- **WorkflowStep** -- provides custom step types for the Workflow builder
- **Theme** -- supplies color palettes and typography overrides
- **Custom** -- catch-all for plugins spanning multiple roles

### Permissions

Plugins declare required permissions in `manifest.json`. Current permission types:

- `Documents` -- read/write document files
- `FileSystem` -- access paths outside the plugin data sandbox
- `Network` -- make outbound network requests

## Code Structure

```
plugins/sample-plugin/
  SamplePlugin.csproj    -- .NET 8 class library referencing AgentX.Core
  manifest.json          -- plugin identity, compatibility, permissions
  SamplePlugin.cs        -- IPlugin implementation with lifecycle and state guards
  SampleDocumentProcessor.cs -- document processing logic (word count, frontmatter)
  README.md              -- this file
```

### Key Patterns

- **Defensive state checks**: `ObjectDisposedException` after `Dispose()`, `InvalidOperationException` if called before `InitializeAsync()`.
- **Serilog logging**: all events go through `IPluginContext.Logger`, which is pre-enriched with the plugin ID.
- **No base class**: plain POCO implementing `IPlugin` directly, consistent with AgentX conventions.
- **ConfigureAwait(false)**: all `await` calls use `ConfigureAwait(false)` to avoid capturing the synchronization context in library code.