using AgentX.Core.Documents.Models;
using AgentX.Core.Services.Plugins;
using Serilog;
using HostProcessedDocument = AgentX.Core.Documents.Models.ProcessedDocument;

namespace AgentX.Plugins.Sample;

/// <summary>
/// Sample plugin demonstrating the AgentX plugin lifecycle and the document-processor
/// extension point. Implements <see cref="IDocumentProcessorPlugin"/>, so while the plugin is
/// active the host offers it to the import pipeline for files no built-in processor handles.
/// </summary>
/// <remarks>
/// This plugin is intended as a reference implementation. It shows the correct order of
/// lifecycle calls, defensive state-checking patterns, and Serilog integration expected
/// from a production-quality AgentX plugin. Agent-X's built-in processors already read
/// <c>.txt</c> and <c>.md</c> and take precedence, so in practice the host routes only
/// <c>.text</c> files here.
/// </remarks>
public sealed class SamplePlugin : IDocumentProcessorPlugin
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

    // -- IDocumentProcessor ----------------------------------------------------

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => SampleDocumentProcessor.Extensions;

    /// <inheritdoc />
    public bool CanProcess(string filePath)
    {
        // Only while running: the host may still hold this instance briefly after disabling it.
        return _isActive
            && !string.IsNullOrWhiteSpace(filePath)
            && SampleDocumentProcessor.Extensions.Contains(Path.GetExtension(filePath));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="SampleDocumentProcessor"/> and maps its result onto the host's
    /// document model: the text is what gets chunked and indexed, and the counts and
    /// frontmatter become document metadata.
    /// </remarks>
    public async Task<HostProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        var processor = Processor
            ?? throw new InvalidOperationException($"Plugin '{Id}' must be initialized before it can process documents.");

        var result = await processor.ProcessDocumentAsync(filePath).ConfigureAwait(false);
        var fileInfo = new FileInfo(filePath);

        var metadata = new DocumentMetadata();
        foreach (var (key, value) in result.Frontmatter)
        {
            metadata.Custom[key] = value;
        }

        metadata.Custom["lineCount"] = result.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata.Custom["characterCount"] = result.CharacterCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new HostProcessedDocument
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileType = fileInfo.Extension.TrimStart('.').ToLowerInvariant(),
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            ExtractedText = result.Content,
            ExtractedTitle = result.Frontmatter.TryGetValue("title", out var title)
                ? title
                : Path.GetFileNameWithoutExtension(filePath),
            PageCount = 1,
            WordCount = result.WordCount,
            Metadata = metadata,
        };
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