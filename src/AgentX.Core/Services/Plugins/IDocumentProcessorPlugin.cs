using AgentX.Core.Documents;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// A plugin that adds text extraction for one or more file formats. The entry type of a
/// <see cref="PluginType.DocumentProcessor"/> plugin implements this interface (or
/// <see cref="IDocumentProcessor"/> directly) and, while the plugin is active, the host
/// offers it to the document pipeline through <see cref="IPluginDocumentProcessorSource"/>.
/// </summary>
/// <remarks>
/// Built-in processors take precedence: a plugin processor is only consulted for a file
/// that no built-in processor claims, so a plugin can add formats but cannot replace how
/// PDF, DOCX, text, Markdown, code, image, audio, or web files are read.
/// </remarks>
public interface IDocumentProcessorPlugin : IPlugin, IDocumentProcessor
{
}
