# Sample Document Processor Plugin

A reference plugin for the Agent-X plugin system. It implements the `DocumentProcessor` extension point (`IDocumentProcessorPlugin`): it reads plain-text and Markdown files, counts words, lines and characters, and extracts YAML-style frontmatter from Markdown.

For the full plugin contract, see [docs/PLUGIN-DEVELOPMENT-GUIDE.md](../../docs/PLUGIN-DEVELOPMENT-GUIDE.md).

## What It Does

- Claims `.txt`, `.text` and `.md` files
- Counts words, lines and characters
- Extracts frontmatter from Markdown files: simple `key: value` lines between `---` delimiters at the start of the file, which are removed from the text
- Returns the text to the host's import pipeline. The word count becomes the document's word count; the line count, the character count and any frontmatter keys are stored as document metadata; a frontmatter `title` becomes the document title (otherwise the file name without its extension)

Agent-X's built-in processors already read `.txt` and `.md`, and built-in processors always take precedence, so the host routes only `.text` files to this sample. That is by design: a plugin can add formats, not replace how built-in formats are read. It also means the frontmatter code shows the technique but does not run inside Agent-X.

## Build

```bash
cd plugins/sample-plugin
dotnet build -p:Platform=x64
```

The output is written to `bin/x64/Debug/net8.0-windows10.0.22621.0/` (or `bin/x64/Release/...` for release builds): `SamplePlugin.dll`, `SamplePlugin.deps.json`, `SamplePlugin.pdb`, `SamplePlugin.runtimeconfig.json` and `manifest.json`, plus the Windows SDK projection assemblies (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) that the host also ships. AgentX.Core and Serilog are compile-time references only, so the output contains no `AgentX.Core.dll` or `Serilog.dll`. The host provides them at run time, and its loader always uses its own copies of the assemblies it ships, so leaving them out only keeps the package small.

## Package as .agentx-plugin

1. Build in Release configuration:
   ```bash
   dotnet build -c Release -p:Platform=x64
   ```
2. Create a ZIP archive with `manifest.json` at its root, from the build output:
   ```bash
   cd bin/x64/Release/net8.0-windows10.0.22621.0
   zip sample-plugin.agentx-plugin manifest.json SamplePlugin.dll SamplePlugin.deps.json SamplePlugin.pdb
   ```
   Or, in PowerShell (the Plugin Manager accepts the `.zip` as it is):
   ```powershell
   cd bin\x64\Release\net8.0-windows10.0.22621.0
   Compress-Archive -Path manifest.json, SamplePlugin.dll, SamplePlugin.deps.json, SamplePlugin.pdb -DestinationPath sample-plugin.zip
   ```
   A plugin of your own also includes its private dependency DLLs; the host resolves them from the plugin folder.
3. The resulting file is ready for distribution.

## Install

1. Open Agent-X.
2. Open **Plugin Manager** (SYSTEM section of the navigation pane).
3. Click **+** ("Install plugin from file"), or **Install Plugin** while no plugin is installed, and select the `.agentx-plugin` or `.zip` file.
4. The Plugin Manager validates the manifest (including `minAppVersion` and `dependencies`), extracts the files to `%LocalAppData%\AgentX\Plugins\com.agentx.sample-plugin\`, and records the plugin as **Disabled**. Nothing is loaded yet.
5. Select the plugin and turn its switch to **Active**. The host loads the assembly, then calls `InitializeAsync` and `ActivateAsync`. An enabled plugin is activated again on every application start.

## Try It

1. Create a text file with the extension `.text`, for example `meeting-notes.text`.
2. Drag it onto the Knowledge Vault page, or put it in a folder and use **Import Folder**. The **Import Files** dialog lists only built-in file types, so it does not show `.text` files.
3. The document is imported with the sample's word count and metadata, then indexed like any other document.

## Plugin API Overview

### Lifecycle

Every plugin implements `IPlugin` and follows a strict lifecycle:

| Method | When | Purpose |
|---|---|---|
| `InitializeAsync(IPluginContext)` | Once after the assembly is loaded (on enable, or at start for an enabled plugin) | Store the context, read configuration, validate dependencies |
| `ActivateAsync()` | Right after initialization | Start background work, register extensions |
| `DeactivateAsync()` | The user disables or uninstalls the plugin, or the app shuts down | Flush data, stop services, release resources (the host waits at most 10 seconds) |
| `Dispose()` | After deactivation | Free unmanaged resources |

`InitializeAsync` and `ActivateAsync` must each finish within 30 seconds, or the enable fails.

The `IPluginContext` provides:
- **Services**: a service provider with the approved host services (currently the Smart Inbox service, `IInboxService`)
- **PluginDataPath**: the plugin's own folder for configuration, caches and state (`%LocalAppData%\AgentX\Plugins\{id}\data`, deleted on uninstall)
- **Logger**: the host's Serilog `ILogger` with the plugin ID and version attached

### Extension Points

Plugins declare a `PluginType`. Two kinds of plugin have a host integration today:

- **DocumentProcessor**: adds support for new file formats. Implement `IDocumentProcessorPlugin`; while the plugin is active the host offers it every file no built-in processor claims.
- **DataConnector**: pushes external items into the Smart Inbox through the inbox service in `IPluginContext.Services`.

The remaining types (**AiProvider**, **QuickAction**, **WorkflowStep**, **Theme**, **Custom**) are labels for the Plugin Manager; the host does not yet call such plugins.

### Permissions

`manifest.json` can list the permissions a plugin says it needs (`Documents`, `FileSystem`, `Network`, ...). They are informational only: the host does not ask for consent and does not enforce them. Plugins run in-process with the user's rights and are not sandboxed, so install only plugins you trust.

## Code Structure

```
plugins/sample-plugin/
  SamplePlugin.csproj         .NET 8 class library referencing AgentX.Core (compile time only)
  manifest.json               plugin identity, compatibility, permissions
  SamplePlugin.cs             IDocumentProcessorPlugin implementation with lifecycle and state guards
  SampleDocumentProcessor.cs  document processing logic (counts, frontmatter)
  README.md                   this file
```

### Key Patterns

- **Defensive state checks**: `ObjectDisposedException` after `Dispose()`; `InvalidOperationException` when `ActivateAsync`, `DeactivateAsync` or `ProcessAsync` runs before `InitializeAsync`, or `InitializeAsync` runs twice.
- **Only while active**: `CanProcess` answers true only while the plugin is active, because an import that already picked the plugin may still hold it briefly after it is disabled.
- **Serilog logging**: all events go through `IPluginContext.Logger`; the lifecycle messages name the plugin ID in their text, which is what the Agent-X log file shows.
- **No base class**: a plain class implementing `IDocumentProcessorPlugin` directly.
- **ConfigureAwait(false)**: every `await` uses `ConfigureAwait(false)` to avoid capturing the synchronization context in library code.
