using AgentX.Core.Documents;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Supplies the document processors contributed by currently active plugins, so the
/// import and indexing pipeline can read file formats that no built-in processor handles.
/// </summary>
/// <remarks>
/// <para>Consult it after the built-in processors, at the point a processor is chosen for a
/// file, for example at the end of <c>FindProcessorFor</c>:</para>
/// <code>
/// return _pluginProcessors?.GetDocumentProcessors().FirstOrDefault(p => p.CanProcess(filePath));
/// </code>
/// <para>Do not cache the returned list: plugins are enabled, disabled, and unloaded at run
/// time, and a processor from an unloaded plugin must not be called. The returned
/// processors guard <see cref="IDocumentProcessor.CanProcess"/> and
/// <see cref="IDocumentProcessor.SupportedExtensions"/> so a faulty plugin cannot break
/// processor selection for other files; exceptions from
/// <see cref="IDocumentProcessor.ProcessAsync"/> propagate and fail only that import.</para>
/// </remarks>
public interface IPluginDocumentProcessorSource
{
    /// <summary>
    /// Returns a snapshot of the processors exposed by active plugins whose entry type
    /// implements <see cref="IDocumentProcessor"/> (normally through
    /// <see cref="IDocumentProcessorPlugin"/>). Empty when no such plugin is active.
    /// </summary>
    IReadOnlyList<IDocumentProcessor> GetDocumentProcessors();
}
