using AgentX.Core.Services.Plugins;
using Serilog;

namespace AgentX.Plugins.Sample;

/// <summary>
/// Sample plugin demonstrating the AgentX plugin lifecycle.
/// Implements <see cref="IPlugin"/> as a <see cref="PluginType.DocumentProcessor"/>
/// that handles plain-text and Markdown files with word counting and frontmatter extraction.
/// </summary>
/// <remarks>
/// This plugin is intended as a reference implementation. It shows the correct order of
/// lifecycle calls, defensive state-checking patterns, and Serilog integration expected
/// from a production-quality AgentX plugin.
/// </remarks>
public sealed class SamplePlugin : IPlugin
{
    private IPluginContext? _context;
    private bool _isInitialized;
    private bool _isActive;
    private bool _isDisposed;

    /// <inheritdoc />
    public string Id => "com.agentx.sample-plugin";

    /// <inheritdoc />
    public string Name => "Sample Document Processor";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "AgentX Team";

    /// <inheritdoc />
    public string Description =>
        "A sample plugin demonstrating the AgentX plugin system. Implements a document " +
        "processor that handles .txt and .md files with word counting and metadata extraction.";

    /// <inheritdoc />
    public PluginType Type => PluginType.DocumentProcessor;

    /// <summary>
    /// Gets the plugin context provided during initialization.
    /// Returns <c>null</c> before <see cref="InitializeAsync"/> is called.
    /// </summary>
    internal IPluginContext? Context => _context;

    /// <summary>
    /// Gets whether the plugin is currently active.
    /// </summary>
    internal bool IsActive => _isActive;

    /// <summary>
    /// Gets the document processor instance, available after initialization.
    /// </summary>
    internal SampleDocumentProcessor? Processor { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Stores the provided <paramref name="context"/> for later use and logs
    /// the initialization event. Does not start any background work --
    /// that belongs in <see cref="ActivateAsync"/>.
    /// </remarks>
    public Task InitializeAsync(IPluginContext context)
    {
        ThrowIfDisposed();

        if (_isInitialized)
        {
            throw new InvalidOperationException(
                $"Plugin '{Id}' has already been initialized. InitializeAsync must not be called more than once.");
        }

        _context = context ?? throw new ArgumentNullException(nameof(context));
        _isInitialized = true;

        Processor = new SampleDocumentProcessor(context.Logger);

        _context.Logger.Information(
            "Plugin {PluginId} v{PluginVersion} initialized. Data path: {PluginDataPath}",
            Id, Version, _context.PluginDataPath);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Marks the plugin as active and logs the activation event.
    /// This is where background services or extension point registrations would start.
    /// </remarks>
    public Task ActivateAsync()
    {
        ThrowIfDisposed();

        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"Plugin '{Id}' cannot be activated before initialization. Call InitializeAsync first.");
        }

        if (_isActive)
        {
            _context!.Logger.Warning("Plugin {PluginId} is already active. ActivateAsync was called redundantly.", Id);
            return Task.CompletedTask;
        }

        _isActive = true;

        _context!.Logger.Information("Plugin {PluginId} v{PluginVersion} activated.", Id, Version);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Marks the plugin as inactive and logs the deactivation event.
    /// Flush any pending data or release shared resources here.
    /// </remarks>
    public Task DeactivateAsync()
    {
        ThrowIfDisposed();

        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                $"Plugin '{Id}' cannot be deactivated before initialization.");
        }

        if (!_isActive)
        {
            _context!.Logger.Warning("Plugin {PluginId} is already inactive. DeactivateAsync was called redundantly.", Id);
            return Task.CompletedTask;
        }

        _isActive = false;

        _context!.Logger.Information("Plugin {PluginId} v{PluginVersion} deactivated.", Id, Version);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Releases all resources held by the plugin. After disposal, every public
    /// method on this instance will throw <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isActive = false;
        _isInitialized = false;
        _isDisposed = true;

        _context?.Logger.Information("Plugin {PluginId} v{PluginVersion} disposed.", Id, Version);

        _context = null;
        Processor = null;
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if the plugin has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().Name,
                $"Plugin '{Id}' has been disposed and cannot be used.");
        }
    }
}