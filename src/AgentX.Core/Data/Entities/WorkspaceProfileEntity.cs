namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a saved workspace profile that captures a user-defined operating
/// context: which Ollama model is active, which document collections are in scope,
/// and any bespoke UI preferences (font size, theme overrides, panel layout, etc.).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActiveCollectionIds"/> is stored as a comma-separated string of
/// <c>long</c> IDs (e.g. <c>"1,4,12"</c>) so that the entity remains self-contained
/// without a join table.  Services reading this field should split on <c>','</c> and
/// parse to <c>long</c>, ignoring blank or malformed segments.
/// </para>
/// <para>
/// <see cref="CustomSettings"/> is an opaque JSON blob.  Callers are responsible for
/// serialisation and deserialisation using <see cref="System.Text.Json.JsonSerializer"/>
/// or a typed options record.  The schema is intentionally not enforced at the
/// entity layer so that new UI preferences can be added without a migration.
/// </para>
/// <para>
/// Only one profile should have <see cref="IsDefault"/> set to <c>true</c> at any
/// given time.  <see cref="Services.Workspace.WorkspaceProfileService.SetDefaultProfileAsync"/>
/// atomically clears all other profiles before promoting the target profile.
/// </para>
/// </remarks>
public class WorkspaceProfileEntity
{
    /// <summary>Gets or sets the primary key.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the display name shown in the profile picker.
    /// Must not be null or empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional user-supplied description of the profile's purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the Ollama model identifier this profile activates on load
    /// (e.g. <c>"llama3.1:8b"</c>, <c>"mistral:latest"</c>).
    /// <c>null</c> means the application's global default model is used.
    /// </summary>
    public string? ActiveModelId { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of <see cref="CollectionEntity.Id"/> values
    /// that are in scope when this profile is loaded (e.g. <c>"1,4,12"</c>).
    /// <c>null</c> or empty means no specific collections are pre-selected.
    /// </summary>
    public string? ActiveCollectionIds { get; set; }

    /// <summary>
    /// Gets or sets an opaque JSON blob holding UI-level preferences specific to
    /// this profile (e.g. font size, sidebar width, accent colour override).
    /// The structure is defined and interpreted by the ViewModel / settings layer.
    /// </summary>
    public string? CustomSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this profile is loaded automatically
    /// on application start.  Exactly one profile should carry this flag at a time.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this profile was first created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the most recent update to any field on
    /// this profile, including <see cref="IsDefault"/> changes.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
