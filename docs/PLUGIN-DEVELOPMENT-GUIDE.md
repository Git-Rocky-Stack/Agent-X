# Agent-X Plugin Development Guide

Version 1.1 | Last Updated: September 2026

---

## Overview

Agent-X supports third-party plugins via the `.agentx-plugin` package format. Today the host integrates two kinds of plugin: document processors (new file formats for the Knowledge Vault) and data connectors (items pushed into the Smart Inbox). The other plugin types are accepted and listed in the Plugin Manager, but the host does not call them yet; see [Extension Points](#extension-points).

Plugins run in-process with the user's rights. The host isolates their assemblies but does not sandbox file-system or network access, and manifest permissions are informational. Install only plugins you trust.

This guide covers everything you need to create, build, package, and distribute Agent-X plugins.

---

## Quick Start

### 1. Create a Plugin Project

```bash
mkdir MyPlugin && cd MyPlugin
dotnet new classlib -n MyPlugin -f net8.0-windows10.0.22621.0
```

### 2. Add a Reference to AgentX.Core

AgentX.Core (and Serilog) are provided by the host at run time. Reference them for compilation only, so your build output never carries its own copy; a second `AgentX.Core.dll` would give your plugin a different `IPlugin` type than the host's and the plugin would fail to load. Set `EnableDynamicLoading` so the build writes the `deps.json` the host uses to resolve your private dependencies:

```xml
<PropertyGroup>
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="../../src/AgentX.Core/AgentX.Core.csproj">
    <Private>false</Private>
    <ExcludeAssets>runtime;native;contentfiles;build;buildtransitive</ExcludeAssets>
  </ProjectReference>
  <PackageReference Include="Serilog" Version="4.0.2">
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
  <None Update="manifest.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`plugins/sample-plugin/SamplePlugin.csproj` is a working example.

### 3. Implement IPlugin

```csharp
using AgentX.Core.Services.Plugins;
using Serilog;

namespace MyCompany.MyPlugin;

public sealed class MyPlugin : IPlugin
{
    private IPluginContext? _context;
    private bool _isActive;
    private bool _isDisposed;

    public string Id => "com.mycompany.myplugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "My Company";
    public string Description => "A sample AgentX plugin.";
    public PluginType Type => PluginType.Custom;

    public Task InitializeAsync(IPluginContext context)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _context = context;
        _context.Logger.Information("MyPlugin initialized");
        return Task.CompletedTask;
    }

    public Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        InvalidOperationException.ThrowIf(_context is null, "Plugin not initialized");
        _isActive = true;
        _context.Logger.Information("MyPlugin activated");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        _isActive = false;
        _context?.Logger.Information("MyPlugin deactivated");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _isActive = false;
    }
}
```

### 4. Create manifest.json

```json
{
  "id": "com.mycompany.myplugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "author": "My Company",
  "description": "A sample AgentX plugin.",
  "pluginType": "Custom",
  "minAppVersion": "1.3.0",
  "entryAssembly": "MyPlugin.dll",
  "dependencies": [],
  "permissions": [],
  "readme": "# My Plugin\n\nA sample AgentX plugin."
}
```

### 5. Build and Package

```bash
dotnet build -c Release -p:Platform=x64
```

Create a `.agentx-plugin` ZIP file containing, at its root:
- `manifest.json`
- `MyPlugin.dll` and `MyPlugin.deps.json`
- Any private dependency DLLs (never AgentX.Core.dll or Serilog.dll)

```bash
cd bin/x64/Release/net8.0-windows10.0.22621.0
zip MyPlugin.agentx-plugin manifest.json MyPlugin.dll MyPlugin.deps.json
```

### 6. Install

In Agent-X, go to **Plugin Manager > Install Plugin** and select the `.agentx-plugin` file. Installing only extracts the package and records the plugin as disabled; enable it to load and activate it.

---

## Plugin Lifecycle

Every plugin follows this lifecycle:

1. **Install** - Package validated (manifest, `minAppVersion`, `dependencies`) and extracted to `%LocalAppData%\AgentX\Plugins\{id}\`; the plugin is recorded as disabled and nothing is loaded
2. **Initialize** - `IPlugin.InitializeAsync(context)` called once after assembly load, when the plugin is enabled (or at application start for a plugin that was left enabled)
3. **Activate** - `IPlugin.ActivateAsync()` called right after initialization
4. **Deactivate** - `IPlugin.DeactivateAsync()` called before disable or uninstall, and at application shutdown
5. **Dispose** - `IDisposable.Dispose()` called after deactivation

Initialization and activation must each finish within 30 seconds or the enable fails. Deactivation gets 10 seconds; after that the host disposes and unloads the plugin anyway. A plugin that fails to activate at application start is marked disabled, so the Plugin Manager never shows a plugin as running when it is not.

---

## Plugin Context

`IPluginContext` provides safe access to host resources:

| Member | Type | Description |
|--------|------|-------------|
| `Services` | `IServiceProvider` | Scoped service provider with approved services (currently `IInboxService` only) |
| `PluginDataPath` | `string` | Per-plugin data directory (created by host); a convention, not a sandbox |
| `Logger` | `Serilog.ILogger` | Pre-enriched logger with plugin ID and version |

**Important:** Plugins never receive the root `IServiceProvider`. The scoped provider exposes only services approved for plugin consumption. The host's OAuth service is deliberately not offered, because it can return the user's stored Google and Microsoft refresh tokens.

---

## Extension Points

### Document Processor

Handle custom file formats for import into the Knowledge Vault. Implement `IDocumentProcessorPlugin` (an `IPlugin` that is also an `AgentX.Core.Documents.IDocumentProcessor`):

```csharp
public sealed class MyPlugin : IDocumentProcessorPlugin
{
    public PluginType Type => PluginType.DocumentProcessor;
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dwg" };
    public bool CanProcess(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath));
    public Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        // Return the extracted text in ProcessedDocument.ExtractedText.
    }
    // ... IPlugin members ...
}
```

While the plugin is active the host offers it every file that no built-in processor claims. Built-in processors take precedence, so a plugin can add formats but cannot replace how PDF, DOCX, text, Markdown, code, image, audio, or web files are read. `plugins/sample-plugin` is a working example.

### Data Connector

Push external items (calendar events, messages, feeds) into the Smart Inbox through the `IInboxService` in `IPluginContext.Services`. `UpsertExternalAsync` creates or refreshes an item keyed by your plugin ID and the provider's external ID, and imports it into the Knowledge Vault.

```csharp
public PluginType Type => PluginType.DataConnector;
```

### Not Yet Integrated

The following types are accepted and shown in the Plugin Manager, but the host does not call such plugins yet. They are reserved for future extension points.

### AI Provider

Integrate additional AI backends (e.g., Anthropic, Google, local models).

```csharp
public PluginType Type => PluginType.AiProvider;
```

### Quick Action

Add single-click commands to the Quick Actions panel.

```csharp
public PluginType Type => PluginType.QuickAction;
```

### Workflow Step

Extend the workflow builder with custom step types.

```csharp
public PluginType Type => PluginType.WorkflowStep;
```

### Theme

Apply custom visual themes via WinUI 3 ResourceDictionary injection.

```csharp
public PluginType Type => PluginType.Theme;
```

---

## Permissions

You can list the permissions your plugin needs in the manifest. They are **informational only**: the host records nothing, asks for no consent, and enforces nothing, because plugins run in-process with the user's rights. Use them to tell users what your plugin does.

| Permission | Description |
|------------|-------------|
| `FileSystem` | Read/write access outside the plugin data directory |
| `Network` | Outbound HTTP/HTTPS connections |
| `AI` | Access to host AI inference services |
| `Documents` | Read access to the user's document library |
| `Clipboard` | Access to the system clipboard |

Unknown permission strings are ignored.

---

## Manifest Reference

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `id` | Yes | string | Reverse-DNS identifier (e.g., `com.vendor.myplugin`) |
| `name` | Yes | string | Human-readable display name |
| `version` | Yes | string | Semantic version (e.g., `1.2.0`) |
| `author` | Yes | string | Author or organization name |
| `description` | Yes | string | Short description for the Plugin Manager UI |
| `pluginType` | Yes | string | One of: `DocumentProcessor`, `DataConnector`, `AiProvider`, `QuickAction`, `WorkflowStep`, `Theme`, `Custom` |
| `minAppVersion` | No | string | Minimum AgentX version required (defaults to `1.0.0`); install and enable are refused on an older host |
| `entryAssembly` | Yes | string | DLL filename containing the `IPlugin` implementation |
| `dependencies` | No | string[] | Plugin IDs that must be installed before install, and enabled before activation (activated first at application start) |
| `permissions` | No | string[] | Permission tokens; informational only |
| `readme` | No | string | Inline README content (overridden by `README.md` file in archive) |

---

## Best Practices

1. **Defensive state management** - Always check `_isDisposed` and `_context is null` before operations
2. **Thread safety** - Use locks or `ConcurrentDictionary` for shared state
3. **ConfigureAwait(false)** - Use on all `await` calls in library code
4. **Structured logging** - Use the provided `ILogger` (pre-enriched with plugin metadata)
5. **Honest permissions** - List what your plugin actually does; users rely on it because nothing is enforced
6. **Graceful degradation** - Handle missing services or unavailable features without crashing
7. **Small package size** - Keep your plugin lean; never bundle AgentX.Core or Serilog, which the host provides
8. **Prompt deactivation** - Finish `DeactivateAsync` well within 10 seconds, and release host event subscriptions so the plugin can unload


---

*For the complete sample plugin, see `plugins/sample-plugin/` in the Agent-X repository.*