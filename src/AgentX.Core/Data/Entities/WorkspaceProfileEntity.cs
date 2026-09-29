namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a saved workspace profile: a named preset recording an Ollama model
/// identifier, a list of collection IDs, and free-form custom settings. Profiles are
/// stored for reference only; nothing applies them to the running application.
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
/// promotes the target and clears every other profile in a single UPDATE statement.
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
    /// Gets or sets the Ollama model identifier saved with this profile
    /// (e.g. <c>"llama3.1:8b"</c>, <c>"mistral:latest"</c>), or <c>null</c> for none.
    /// Stored only; selecting the profile does not switch models.
    /// </summary>
    public string? ActiveModelId { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of <see cref="CollectionEntity.Id"/> values
    /// saved with this profile (e.g. <c>"1,4,12"</c>), or <c>null</c> for none.
    /// Stored only; selecting the profile does not change the collections in scope.
    /// </summary>
    public string? ActiveCollectionIds { get; set; }

    /// <summary>
    /// Gets or sets free-form text (typically JSON) saved with this profile.
    /// Nothing in the application reads or interprets it.
    /// </summary>
    public string? CustomSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user marked this profile as the
    /// default. At most one profile carries this flag. The mark is informational:
    /// nothing loads the default profile at application start.
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
