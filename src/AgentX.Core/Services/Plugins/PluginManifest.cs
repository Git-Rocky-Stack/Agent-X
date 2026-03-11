using System.Text.Json.Serialization;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Describes a plugin package. Every .agentx-plugin archive must contain a
/// <c>manifest.json</c> file at its root that deserializes cleanly into this class.
/// </summary>
/// <remarks>
/// The manifest is the single source of truth for plugin identity and compatibility.
/// <see cref="IPluginService.InstallPluginAsync"/> reads and validates the manifest
/// before extracting any files or loading any assemblies, so an invalid or missing
/// manifest causes installation to fail fast with a clear error message.
/// </remarks>
public sealed class PluginManifest
{
    /// <summary>
    /// Globally unique reverse-DNS style identifier for this plugin.
    /// Must be stable across versions (e.g. <c>com.vendor.myplugin</c>).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable display name shown in the Plugin Manager UI.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Semantic version string of this plugin release (e.g. <c>1.2.0</c>).
    /// Must follow SemVer 2.0 conventions.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Display name of the individual or organization that authored the plugin.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>Short description of what the plugin does, shown in the Plugin Manager UI.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The primary extension point this plugin targets. Stored as the string name
    /// of the <see cref="PluginType"/> enum member (e.g. <c>"DocumentProcessor"</c>).
    /// </summary>
    [JsonPropertyName("pluginType")]
    public string PluginType { get; set; } = "Custom";

    /// <summary>
    /// Minimum AgentX application version required to run this plugin (e.g. <c>1.2.0</c>).
    /// The host rejects installation if its own version is lower than this value.
    /// </summary>
    [JsonPropertyName("minAppVersion")]
    public string MinAppVersion { get; set; } = "1.0.0";

    /// <summary>
    /// List of other plugin IDs that must be installed and enabled before this plugin
    /// can be activated. The host validates these at activation time and surfaces a
    /// descriptive error if any dependency is missing.
    /// </summary>
    /// <example><c>["com.agentx.core-utils", "com.vendor.shared-models"]</c></example>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    /// <summary>
    /// File name (not path) of the primary DLL inside the plugin archive that the host
    /// should load into its <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// (e.g. <c>"MyPlugin.dll"</c>).
    /// </summary>
    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>
    /// Optional inline README / documentation content. If present, the host stores this in the
    /// database for rendering in the Plugin Manager detail panel. When both this field and a
    /// standalone <c>README.md</c> file exist in the archive, the file takes precedence.
    /// </summary>
    [JsonPropertyName("readme")]
    public string? Readme { get; set; }

    /// <summary>
    /// Declared permissions this plugin requires. The host validates these against
    /// the user's consent record before activation. Unknown permission strings are
    /// silently ignored to maintain forward compatibility.
    /// </summary>
    /// <remarks>
    /// Well-known permission tokens:
    /// <list type="bullet">
    ///   <item><c>"FileSystem"</c> — read/write access outside the plugin data directory.</item>
    ///   <item><c>"Network"</c> — outbound HTTP/HTTPS connections.</item>
    ///   <item><c>"AI"</c> — access to the host AI inference services.</item>
    ///   <item><c>"Documents"</c> — read access to the user's document library.</item>
    ///   <item><c>"Clipboard"</c> — access to the system clipboard.</item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("permissions")]
    public List<string> Permissions { get; set; } = [];
}
