using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels;

/// <summary>
/// Represents a system prompt for selection in the UI.
/// </summary>
public class SystemPromptItem : ObservableObject
{
    private bool _isFavorite;

    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "General";

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool IsBuiltIn { get; set; }

    public string CategoryIcon => Category switch
    {
        "General" => "\uE8BD",
        "Code" => "\uE943",
        "Writing" => "\uE70F",
        "Analysis" => "\uE9D9",
        "Creative" => "\uE790",
        _ => "\uE8BD"
    };
}
