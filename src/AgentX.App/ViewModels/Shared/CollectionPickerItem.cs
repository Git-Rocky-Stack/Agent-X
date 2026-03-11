namespace AgentX.App.ViewModels.Shared;

/// <summary>
/// Lightweight display model for collection pickers/dropdowns.
/// Replaces direct use of <c>CollectionEntity</c> in ViewModels
/// to decouple the view layer from the data layer.
/// </summary>
public sealed class CollectionPickerItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? IconGlyph { get; init; }
    public int DocumentCount { get; init; }
}
