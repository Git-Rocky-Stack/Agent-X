namespace AgentX.Core.Services.Screen;

/// <summary>
/// Represents a detected IDE from the active window title.
/// Contains structured context about the IDE, active file, project,
/// and programming language.
/// </summary>
public class IdeDetection
{
    /// <summary>
    /// Name of the detected IDE (e.g., "VS Code", "Visual Studio", "JetBrains Rider").
    /// Empty if detection failed.
    /// </summary>
    public string IdeName { get; init; } = string.Empty;

    /// <summary>
    /// Name of the active file in the editor, including its extension.
    /// Extracted from the first segment of the window title.
    /// </summary>
    public string ActiveFileName { get; init; } = string.Empty;

    /// <summary>
    /// Name of the project or workspace folder currently open in the IDE.
    /// Extracted from the second segment of the window title.
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// Inferred programming language based on the active file's extension.
    /// Empty if the extension is not mapped to a known language.
    /// </summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>
    /// The raw, unmodified window title that was analyzed for IDE detection.
    /// Preserved for debugging and fallback purposes.
    /// </summary>
    public string RawTitle { get; init; } = string.Empty;
}