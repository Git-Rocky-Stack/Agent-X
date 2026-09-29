# Agent-X Plugin Development Guide

Version 1.2 | Last Updated: September 2026

---

## Overview

Agent-X loads third-party plugins from `.agentx-plugin` packages. A package is a ZIP archive; the Plugin Manager also accepts it under a `.zip` name. Two kinds of plugin have a host integration today:

- **Document processors** read file formats that no built-in processor reads, so those files can be imported into the Knowledge Vault and indexed.
- **Data connectors** push external items (events, messages, feed entries) into the Smart Inbox, which imports them into the Knowledge Vault.

The other plugin types are accepted, loaded and listed in the Plugin Manager, but nothing in Agent-X calls them yet; see [Not Yet Integrated](#not-yet-integrated).

**Plugins are not sandboxed.** A plugin runs inside the Agent-X process with the user's rights: it can read and write any file and reach any network address the user can. The host loads each plugin into its own collectible `AssemblyLoadContext`, which keeps the plugin's assemblies apart from other plugins and lets it be unloaded; that is not a security boundary. The permissions in a manifest are informational only. Install only plugins you trust.

This guide covers creating, building, packaging and installing a plugin. `plugins/sample-plugin` in this repository is a complete, working document processor.

---

## Quick Start

### 1. Create a Plugin Project

AgentX.Core is not published as a NuGet package, so a plugin compiles against the source in a clone of this repository. Create the project next to the sample plugin:

```bash
cd plugins
dotnet new classlib -n MyPlugin -f net8.0
```

This creates `plugins/MyPlugin/MyPlugin.csproj` and a `Class1.cs` you can delete. The `classlib` template accepts only plain target frameworks such as `net8.0`, so the Windows target that AgentX.Core uses is set in the next step. The repository's `global.json` selects the .NET 8 SDK (8.0.421, or a later 8.0 feature band), and its `Directory.Build.props` turns on nullable reference types and implicit usings for every project in the tree, including yours.

### 2. Set Up the Project File

Replace the contents of `MyPlugin.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <Platforms>x86;x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AgentX.Core\AgentX.Core.csproj">
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

</Project>
```

- The target framework and platforms match AgentX.Core (`src/AgentX.Core/AgentX.Core.csproj`).
- AgentX.Core and Serilog are referenced for compilation only (`Private=false`, `ExcludeAssets`), so the build output carries no copy of them. At run time the host supplies them: its loader always takes AgentX.Core, Serilog, `Microsoft.Extensions.*` and every other assembly the host ships from the host, even when a package contains its own copy, so the plugin and the host share one `IPlugin` type. A copy in your package is never used.
- `EnableDynamicLoading` makes the build copy your NuGet dependencies next to your DLL and write `MyPlugin.deps.json`, which the host's loader reads to find them.
- `manifest.json` is copied to the build output, so the output folder is ready to package.

`plugins/sample-plugin/SamplePlugin.csproj` is the same setup in a working project.

### 3. Implement IPlugin

```csharp
using AgentX.Core.Services.Plugins;

namespace MyCompany.MyPlugin;

public sealed class MyPlugin : IPlugin
{
    private IPluginContext? _context;
    private bool _isDisposed;

    public string Id => "com.mycompany.myplugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public string Author => "My Company";
    public string Description => "A sample Agent-X plugin.";
    public PluginType Type => PluginType.Custom;

    public Task InitializeAsync(IPluginContext context)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _context.Logger.Information("{PluginId} initialized", Id);
        return Task.CompletedTask;
    }

    public Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_context is null)
        {
            throw new InvalidOperationException("InitializeAsync has not been called.");
        }

        _context.Logger.Information("{PluginId} activated", Id);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        _context?.Logger.Information("{PluginId} deactivated", Id);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _isDisposed = true;
        _context = null;
    }
}
```

The entry assembly must contain exactly one public, non-abstract class that implements `IPlugin` and has a public parameterless constructor; the host creates it with `Activator.CreateInstance`. Zero or several such classes make the enable fail. Give `Id`, `Name`, `Version`, `Author` and `Description` the same values as the manifest: the host keys the plugin by the manifest `id` and shows the manifest values in the Plugin Manager.

### 4. Create manifest.json

```json
{
  "id": "com.mycompany.myplugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "author": "My Company",
  "description": "A sample Agent-X plugin.",
  "pluginType": "Custom",
  "minAppVersion": "2.2.0",
  "entryAssembly": "MyPlugin.dll",
  "dependencies": [],
  "permissions": [],
  "readme": "# My Plugin\n\nA sample Agent-X plugin."
}
```

Set `minAppVersion` to the Agent-X version you built and tested against (the `<Version>` in the repository's `Directory.Build.props`). See the [Manifest Reference](#manifest-reference) for every field and how it is checked.

### 5. Build and Package

```bash
cd plugins/MyPlugin
dotnet build -c Release -p:Platform=x64
```

The output is written to `bin/x64/Release/net8.0-windows10.0.22621.0/`. Create the package from that folder. It must have `manifest.json` at its root, next to:

- `MyPlugin.dll` and `MyPlugin.deps.json`
- any private dependency the build copied there (assemblies the host does not ship). Leave out `AgentX.Core.dll`, `Serilog.dll` and the Windows SDK projection assemblies (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`): the host always uses its own.
- optionally a `README.md` (see the `readme` field)

With the `zip` tool (for example in WSL, macOS or Linux):

```bash
cd bin/x64/Release/net8.0-windows10.0.22621.0
zip MyPlugin.agentx-plugin manifest.json MyPlugin.dll MyPlugin.deps.json
```

With PowerShell, `Compress-Archive` writes a `.zip` file, which the Plugin Manager accepts as it is (rename it to `.agentx-plugin` if you prefer):

```powershell
cd bin\x64\Release\net8.0-windows10.0.22621.0
Compress-Archive -Path manifest.json, MyPlugin.dll, MyPlugin.deps.json -DestinationPath MyPlugin.zip
```

Installation extracts the archive with its folder structure, so a dependency in a subfolder stays in that subfolder.

### 6. Install and Enable

1. In Agent-X, open **Plugin Manager** (SYSTEM section of the navigation pane).
2. Click **+** ("Install plugin from file"), or **Install Plugin** while no plugin is installed, and pick the `.agentx-plugin` or `.zip` file.
3. The Plugin Manager validates the manifest, checks `minAppVersion` and `dependencies`, extracts the package to `%LocalAppData%\AgentX\Plugins\{id}\` and records the plugin as **Disabled**. Nothing is loaded yet.
4. Select the plugin and turn its switch to **Active** (or select several plugins and click **Enable**). The host loads the plugin and calls `InitializeAsync` and `ActivateAsync`. When that fails, the reason appears in red under the Plugins header and the plugin is not activated.

An enabled plugin is activated again at every application start. To install a new version, uninstall the old one first: a package whose `id` is already installed is refused.

---

## Plugin Lifecycle

1. **Install** - The manifest is read from the archive root and validated, `minAppVersion` and `dependencies` are checked, the plugin ID must not be installed already, and the files are extracted to `%LocalAppData%\AgentX\Plugins\{id}\` (an entry that would land outside that folder aborts the install). The plugin is recorded as disabled; nothing is loaded.
2. **Load** - When the plugin is enabled, and at every application start for a plugin that was left enabled. The host re-reads the installed `manifest.json`, checks `minAppVersion` again and that every dependency is active, loads the entry assembly into a new collectible `AssemblyLoadContext` and creates the entry type.
3. **Initialize** - `IPlugin.InitializeAsync(context)` is called once for that instance.
4. **Activate** - `IPlugin.ActivateAsync()` is called right after initialization. The plugin is now active.
5. **Deactivate** - `IPlugin.DeactivateAsync()` is called when the plugin is disabled or uninstalled, at application shutdown, and after a failed activation.
6. **Dispose** - `IDisposable.Dispose()` is called after deactivation, and after a failed initialization.
7. **Unload** - The host unloads the load context. On uninstall it also waits for the context to be collected, then deletes the plugin folder, retrying while Windows still holds the DLL open. When files remain, the Plugin Manager names the folder to delete after restarting Agent-X.

`InitializeAsync` and `ActivateAsync` must each finish within 30 seconds, or the enable fails with "did not finish within 30 seconds". `DeactivateAsync` gets 10 seconds; after that, or when it or `Dispose` throws, the host logs the problem and unloads the plugin anyway.

At application start, enabled plugins are activated after the database is ready and before the indexing pipeline starts, so the document processors they contribute are available to imports. They are activated in name order, each plugin's dependencies first. A plugin that fails to activate is logged and marked disabled, so the Plugin Manager never shows a plugin as active when it is not; enabling it again retries and shows the error. At shutdown, plugins are deactivated in the reverse order, after the built-in connectors, the Local API and scheduled backups have stopped, and they stay enabled for the next start.

Every enable creates a new instance in a new load context, so static state does not survive a disable and enable. Stop your background work and remove every subscription to host objects in `DeactivateAsync`: anything the host still references keeps your assemblies loaded, and their files locked, until Agent-X restarts.

---

## Plugin Context

`IPluginContext` (namespace `AgentX.Core.Services.Plugins`) is passed to `InitializeAsync`:

| Member | Type | Description |
|--------|------|-------------|
| `Services` | `IServiceProvider` | A service provider built for this plugin that offers only the host services approved for plugins: currently `IInboxService` (the Smart Inbox) alone. Resolve it with `context.Services.GetService(typeof(IInboxService))`. |
| `PluginDataPath` | `string` | `%LocalAppData%\AgentX\Plugins\{id}\data`, created by the host before `InitializeAsync`. It is inside the install folder, so uninstalling the plugin deletes it. A convention, not a sandbox. |
| `Logger` | `Serilog.ILogger` | The host's logger with `PluginId` and `PluginVersion` properties attached. |

Plugins never receive the host's root `IServiceProvider`. The host's OAuth service is deliberately not offered, because it can return the user's stored Google and Microsoft refresh tokens; only the built-in Calendar and Email connectors use it.

The Agent-X log file (`%LocalAppData%\AgentX\Logs\agentx-YYYYMMDD.log`, kept for 7 days) writes the message text only, not the attached properties, so put your plugin ID in the messages you want to find there, as the examples in this guide do.

---

## Extension Points

### Document Processor

Adds a file format to the Knowledge Vault. The entry type implements `IDocumentProcessorPlugin`, which is an `IPlugin` that is also an `AgentX.Core.Documents.IDocumentProcessor`:

```csharp
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using AgentX.Core.Services.Plugins;

namespace MyCompany.RstPlugin;

public sealed class RstPlugin : IDocumentProcessorPlugin
{
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rst" };

    private IPluginContext? _context;
    private volatile bool _isActive;

    public string Id => "com.mycompany.rst";
    public string Name => "reStructuredText Reader";
    public string Version => "1.0.0";
    public string Author => "My Company";
    public string Description => "Imports .rst files into the Knowledge Vault.";
    public PluginType Type => PluginType.DocumentProcessor;

    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public bool CanProcess(string filePath) =>
        _isActive && Extensions.Contains(Path.GetExtension(filePath));

    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            // The message is shown to the user as the reason the document failed.
            throw new DocumentExtractionException("This reStructuredText file contains no text.");
        }

        return new ProcessedDocument
        {
            ExtractedText = text,
            ExtractedTitle = Path.GetFileNameWithoutExtension(filePath),
            PageCount = 1,
            WordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
        };
    }

    public Task InitializeAsync(IPluginContext context)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public Task ActivateAsync()
    {
        _isActive = true;
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        _isActive = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _isActive = false;
        _context = null;
    }
}
```

How the host uses it:

- **Selection.** For each file the built-in processors are asked first. Only for a file that none of them claims does the host ask the active processor plugins, in activation order, and the first whose `CanProcess` returns true reads the file. A plugin can add formats but cannot replace how a built-in format is read. The built-in formats are:
  - documents: `.pdf`, `.docx`
  - text: `.txt`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`
  - Markdown: `.md`, `.mdx`, `.markdown`
  - code and markup: `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.xaml`
  - images: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`
  - audio: `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm`
  - web shortcuts: `.url`, `.webloc`
- **Which plugins.** The host offers files to every active plugin whose entry type implements `IDocumentProcessor`; `pluginType` is only the label the Plugin Manager shows, but declare `DocumentProcessor` so it says what the plugin does.
- **Getting files in.** `SupportedExtensions` is added to the formats that the **Import Files** picker offers and that **Import Folder**, folders dropped on the Knowledge Vault and watch folders pick up, for as long as the plugin is active.
- **What is read.** `ProcessAsync` runs when a file is imported and when it is re-indexed. The host keeps `ExtractedText` (chunked, embedded and indexed; when `PageCount` is above 1 and the text separates pages with form feed characters, `\f`, chunks keep their page numbers), `WordCount`, `PageCount`, `ExtractedTitle`, `Language` and `Metadata` (saved with the document). The document's name, path, size, content hash and type (taken from the file extension) come from the file itself.
- **Failures.** Throw `DocumentExtractionException` (namespace `AgentX.Core.Documents`) with a message written for the user: the document is kept with the status Failed and that message as its reason, shown on its card and in its preview. Any other exception is recorded as "Text extraction failed: " followed by its message. Cancellation propagates. An exception from `CanProcess` counts as "no" and one from `SupportedExtensions` as "no formats"; both are logged, so a faulty plugin cannot break imports of other files.
- **Only while active.** Return false from `CanProcess` once the plugin is deactivated, as the example does: the host stops offering files to a deactivated plugin, but an import that already picked it may still be running.

Re-reading: the indexer reuses the text extracted at import. When that text is no longer at hand (a re-index, or a document still queued when Agent-X restarted), the indexer reads the file again with the same selection: built-in processors first, then active plugins. A plugin-format document is therefore re-read only while its plugin is active; otherwise it fails with "No processor found for file type".

### Data Connector

Pushes external items into the Smart Inbox through the `IInboxService` in `IPluginContext.Services`:

```csharp
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins;

namespace MyCompany.FeedConnector;

public sealed class FeedConnectorPlugin : IPlugin
{
    private IPluginContext? _context;
    private IInboxService? _inbox;

    public string Id => "com.mycompany.feed";
    public string Name => "Example Feed Connector";
    public string Version => "1.0.0";
    public string Author => "My Company";
    public string Description => "Pushes example feed entries into the Smart Inbox.";
    public PluginType Type => PluginType.DataConnector;

    public Task InitializeAsync(IPluginContext context)
    {
        _context = context;
        _inbox = context.Services.GetService(typeof(IInboxService)) as IInboxService;
        return Task.CompletedTask;
    }

    public Task ActivateAsync() => Task.CompletedTask;

    public Task DeactivateAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _inbox = null;
        _context = null;
    }

    // Call from your own timer or background loop.
    private async Task PushEntryAsync(string entryId, string title, string url, string text)
    {
        if (_inbox is null)
        {
            return;
        }

        var result = await _inbox.UpsertExternalAsync(
            fileName: title,
            fileType: "FeedEntry",
            sourceType: "feed-connector",
            sourceUrl: url,
            sourcePluginId: Id,
            sourceCategory: "news",
            externalId: $"example-feed:{entryId}",
            contentPreview: text.Length > 200 ? text[..200] : text,
            contentText: text).ConfigureAwait(false);

        _context?.Logger.Information("{PluginId}: entry {EntryId} {Outcome}", Id, entryId, result.Outcome);
    }
}
```

- **`UpsertExternalAsync`** keys an item by `sourcePluginId` (use your manifest `id`) and `externalId`. A new item has its `contentText` saved as a text file under `%LocalAppData%\AgentX\Inbox\External\`, gets an inbox row that is already accepted, and is imported into the Knowledge Vault as a document named `fileName` whose type is `fileType`. When the item exists and its text or details changed, the row is updated, and the vault document is renamed when `fileName` changed and re-indexed when the text changed; when nothing changed, nothing is written. The result's `Outcome` is `Created`, `Updated` or `Unchanged`. A failed vault import is logged and the inbox row is still returned.
- **`RemoveExternalAsync`** retires an item that is gone at its source: a row without a vault document is deleted; a vault document is never deleted, and your `markRemoved` function rewrites its stored name, preview and text instead. **`GetExternalItemsAsync`** lists your rows whose external ID starts with a prefix, so you can compare them with what the source still has.
- The host does not schedule connectors: start your own timer or loop in `ActivateAsync` and stop it in `DeactivateAsync`.

The built-in Calendar and Email connectors (`com.agentx.calendar`, `com.agentx.email`) use the same inbox calls, but they are part of Agent-X, not installed plugins.

### Not Yet Integrated

The remaining types are accepted, loaded and activated like any plugin, and receive the same context, but nothing in Agent-X calls them. They are labels reserved for future extension points:

| `pluginType` | Status |
|--------------|--------|
| `AiProvider` | Label only |
| `QuickAction` | Label only |
| `WorkflowStep` | Label only |
| `Theme` | Label only |
| `Custom` | Catch-all label; the default when `pluginType` is missing |

---

## Permissions

A manifest can list the permissions the plugin says it needs. They are **informational only**: the host does not store or show them, asks for no consent and enforces nothing, because plugins run in-process with the user's rights. Use them to tell users what your plugin does. The conventional tokens are:

| Permission | Description |
|------------|-------------|
| `FileSystem` | Read/write access outside the plugin data directory |
| `Network` | Outbound HTTP/HTTPS connections |
| `AI` | Access to host AI inference services |
| `Documents` | Read access to the user's document library |
| `Clipboard` | Access to the system clipboard |

Any other string is accepted and ignored.

---

## Manifest Reference

`manifest.json` must sit at the root of the package. Field names are matched without regard to case.

| Field | Required | Type | Description |
|-------|----------|------|-------------|
| `id` | Yes | string | Reverse-DNS identifier such as `com.vendor.myplugin`: two or more dot-separated segments of letters, digits and inner hyphens. The first segment must not be a Windows device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9`). Also the name of the install folder. |
| `name` | Yes | string | Display name, at most 100 characters |
| `version` | Yes | string | Semantic version: `major.minor.patch` with an optional pre-release and build suffix (`1.2.0`, `2.0.0-beta.1`) |
| `author` | No | string | Author or organization shown in the Plugin Manager (empty when missing) |
| `description` | No | string | Short description shown in the Plugin Manager (empty when missing) |
| `pluginType` | No | string | Label shown in the Plugin Manager: `DocumentProcessor`, `DataConnector`, `AiProvider`, `QuickAction`, `WorkflowStep`, `Theme` or `Custom` (default `Custom`). It is not validated and does not decide what the host calls. |
| `minAppVersion` | No | string | Lowest Agent-X version the plugin runs on (default `1.0.0`). Checked at install and at every enable and start: an older Agent-X, or a value that is not a version, is refused. Only the numeric part is compared (`2.1.9-beta` counts as `2.1.9`). |
| `entryAssembly` | Yes | string | File name, without a folder, of the DLL that contains the `IPlugin` class; must end with `.dll` |
| `dependencies` | No | string[] | IDs of other plugins. Each must be installed before this plugin can be installed, and active before it can be enabled; at application start dependencies are activated first. |
| `permissions` | No | string[] | Permission tokens; informational only |
| `readme` | No | string | Markdown shown in the plugin's Documentation card in the Plugin Manager. A file at the archive root whose name starts with `README` replaces it. Either is skipped when larger than 10 KB (10,240 bytes). |

---

## Best Practices

1. **Defensive state management** - Check for disposal and for a missing context before doing work, as the sample does.
2. **Thread safety** - The host may call a document processor from background threads; protect shared state with locks or concurrent collections.
3. **ConfigureAwait(false)** - Use it on `await` calls in plugin code.
4. **Logging** - Use the logger from `IPluginContext`, and include your plugin ID in messages.
5. **Honest permissions** - List what your plugin actually does; users rely on it because nothing is enforced.
6. **Graceful degradation** - Handle a missing service (for example `GetService` returning null) without throwing.
7. **Small packages** - Never bundle AgentX.Core, Serilog or other assemblies the host ships; they are never used.
8. **Prompt deactivation** - Finish `DeactivateAsync` well within 10 seconds, and release host event subscriptions so the plugin can unload.

---

*For a complete, working plugin, see `plugins/sample-plugin/` in the Agent-X repository.*
