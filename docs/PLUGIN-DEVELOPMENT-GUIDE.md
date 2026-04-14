# Agent-X Plugin Development Guide

Version 1.0 | Last Updated: April 2026

---

## Overview

Agent-X supports third-party plugins via the `.agentx-plugin` package format. Plugins extend Agent-X with new document processors, AI providers, quick actions, workflow steps, themes, and custom functionality.

This guide covers everything you need to create, build, package, and distribute Agent-X plugins.

---

## Quick Start

### 1. Create a Plugin Project

```bash
mkdir MyPlugin && cd MyPlugin
dotnet new classlib -n MyPlugin -f net8.0-windows10.0.22621.0
```

### 2. Add a Reference to AgentX.Core

```bash
dotnet add reference ../../src/AgentX.Core/AgentX.Core.csproj
```

Or, when distributing as a NuGet package:

```xml
<PackageReference Include="AgentX.Core" Version="1.3.0" />
```

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
dotnet build -c Release
```

Create a `.agentx-plugin` ZIP file containing:
- `manifest.json`
- `MyPlugin.dll`
- Any dependency DLLs

```bash
cd bin/Release/net8.0-windows10.0.22621.0
zip MyPlugin.agentx-plugin manifest.json MyPlugin.dll
```

### 6. Install

In Agent-X, go to **Plugin Manager > Install Plugin** and select the `.agentx-plugin` file.

---

## Plugin Lifecycle

Every plugin follows this lifecycle:

1. **Install** — Package extracted to `%LocalAppData%\AgentX\Plugins\{id}\`
2. **Initialize** — `IPlugin.InitializeAsync(context)` called once after assembly load
3. **Activate** — `IPlugin.ActivateAsync()` called when user enables the plugin
4. **Deactivate** — `IPlugin.DeactivateAsync()` called before disable/uninstall
5. **Dispose** — `IDisposable.Dispose()` called after deactivation

---

## Plugin Context

`IPluginContext` provides safe access to host resources:

| Member | Type | Description |
|--------|------|-------------|
| `Services` | `IServiceProvider` | Scoped service provider with approved services |
| `PluginDataPath` | `string` | Per-plugin data directory (created by host) |
| `Logger` | `Serilog.ILogger` | Pre-enriched logger with plugin ID and version |

**Important:** Plugins must NOT receive the root `IServiceProvider`. The scoped provider exposes only services approved for plugin consumption.

---

## Extension Points

### Document Processor

Handle custom file formats for import into the Knowledge Vault.

```csharp
public PluginType Type => PluginType.DocumentProcessor;
```

Implement a document processor class that reads files and returns structured text content.

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

Declare required permissions in your manifest:

| Permission | Description |
|------------|-------------|
| `FileSystem` | Read/write access outside the plugin data directory |
| `Network` | Outbound HTTP/HTTPS connections |
| `AI` | Access to host AI inference services |
| `Documents` | Read access to the user's document library |
| `Clipboard` | Access to the system clipboard |

Unknown permission strings are silently ignored for forward compatibility.

---

## Manifest Reference

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `id` | Yes | string | Reverse-DNS identifier (e.g., `com.vendor.myplugin`) |
| `name` | Yes | string | Human-readable display name |
| `version` | Yes | string | Semantic version (e.g., `1.2.0`) |
| `author` | Yes | string | Author or organization name |
| `description` | Yes | string | Short description for the Plugin Manager UI |
| `pluginType` | Yes | string | One of: `DocumentProcessor`, `AiProvider`, `QuickAction`, `WorkflowStep`, `Theme`, `Custom` |
| `minAppVersion` | No | string | Minimum AgentX version required (defaults to `1.0.0`) |
| `entryAssembly` | Yes | string | DLL filename containing the `IPlugin` implementation |
| `dependencies` | No | string[] | Plugin IDs that must be installed first |
| `permissions` | No | string[] | Required permission tokens |
| `readme` | No | string | Inline README content (overridden by `README.md` file in archive) |

---

## Best Practices

1. **Defensive state management** — Always check `_isDisposed` and `_context is null` before operations
2. **Thread safety** — Use locks or `ConcurrentDictionary` for shared state
3. **ConfigureAwait(false)** — Use on all `await` calls in library code
4. **Structured logging** — Use the provided `ILogger` (pre-enriched with plugin metadata)
5. **Minimal permissions** — Only request permissions your plugin actually needs
6. **Graceful degradation** — Handle missing services or unavailable features without crashing
7. **Small package size** — Keep your plugin lean; avoid bundling large dependencies

---

*For the complete sample plugin, see `plugins/sample-plugin/` in the Agent-X repository.*