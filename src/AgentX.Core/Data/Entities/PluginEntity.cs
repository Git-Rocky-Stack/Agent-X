namespace AgentX.Core.Data.Entities;

/// <summary>
/// Persists a record for each plugin installed into AgentX.
/// One row per plugin ID; the record is created during installation and removed during uninstallation.
/// </summary>
/// <remarks>
/// <see cref="PluginId"/> is the stable reverse-DNS identifier declared in the plugin manifest
/// (e.g. <c>com.vendor.myplugin</c>) and is distinct from the surrogate <see cref="Id"/> primary key.
/// Use <see cref="PluginId"/> for business-logic lookups; use <see cref="Id"/> only for EF Core relations.
/// </remarks>
public class PluginEntity
{
    /// <summary>Surrogate primary key managed by EF Core / SQLite AUTOINCREMENT.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Globally unique reverse-DNS plugin identifier, sourced from the manifest
    /// (e.g. <c>com.vendor.myplugin</c>). Indexed with a unique constraint.
    /// </summary>
    public string PluginId { get; set; } = string.Empty;

    /// <summary>Human-readable display name as declared in the manifest.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Semantic version string of the installed release (e.g. <c>1.2.0</c>).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Author name as declared in the manifest.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Short description sourced from the manifest.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// String representation of the <see cref="AgentX.Core.Services.Plugins.PluginType"/> enum member
    /// (e.g. <c>"DocumentProcessor"</c>). Stored as a string so the schema remains stable
    /// when new enum values are added in future application versions.
    /// </summary>
    public string PluginType { get; set; } = "Custom";

    /// <summary>
    /// Absolute path to the directory on disk where the plugin's files were extracted.
    /// Typically <c>%LocalAppData%\AgentX\Plugins\{PluginId}\</c>.
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, the plugin will be loaded and activated on application start.
    /// When <see langword="false"/>, the plugin assembly is not loaded at all.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>UTC timestamp when the plugin package was installed.</summary>
    public DateTime InstalledAt { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent successful call to
    /// <see cref="AgentX.Core.Services.Plugins.IPlugin.ActivateAsync"/>,
    /// or <see langword="null"/> if the plugin has never been activated.
    /// </summary>
    public DateTime? LastActivatedAt { get; set; }

    /// <summary>
    /// Optional JSON blob containing plugin-specific settings persisted by the host on behalf
    /// of the plugin. Plugins should not parse this directly; the host round-trips it through
    /// <see cref="IPluginService"/> using the plugin's declared settings schema.
    /// </summary>
    public string? SettingsJson { get; set; }
}
