namespace AgentX.Core.Exceptions;

/// <summary>
/// Thrown when a plugin encounters an error during loading, execution, or teardown.
/// Carries the plugin identifier so callers can identify which plugin failed.
/// </summary>
public class PluginException : AgentXException
{
    private const string Code = "PLUGIN_ERROR";

    /// <summary>
    /// The unique identifier of the plugin that encountered the error, or <c>null</c> if unknown.
    /// </summary>
    public string? PluginId { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PluginException"/>.
    /// </summary>
    /// <param name="pluginId">The identifier of the plugin that failed.</param>
    /// <param name="message">A technical description of what went wrong.</param>
    public PluginException(string? pluginId, string message)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: $"Plugin '{pluginId}' encountered an error: {message}",
            inner: null)
    {
        PluginId = pluginId;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="PluginException"/> with an inner exception.
    /// </summary>
    /// <param name="pluginId">The identifier of the plugin that failed.</param>
    /// <param name="message">A technical description of what went wrong.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public PluginException(string? pluginId, string message, Exception inner)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: $"Plugin '{pluginId}' encountered an error: {message}",
            inner: inner)
    {
        PluginId = pluginId;
    }
}
