namespace AgentX.App.Models;

/// <summary>
/// Represents a single section in the User Guide.
/// Each section has a localized title, an icon, a template key for DataTemplate resolution,
/// and a display order for sorting.
/// </summary>
/// <param name="TitleKey">Localization resource key for the section title.</param>
/// <param name="IconGlyph">Segoe MDL2 Assets glyph code.</param>
/// <param name="TemplateKey">Resource key for the DataTemplate to render this section.</param>
/// <param name="DisplayOrder">Sort order (lower values appear first).</param>
public record UserGuideSection(
    string TitleKey,
    string IconGlyph,
    string TemplateKey,
    int DisplayOrder);

/// <summary>
/// Well-known section types for User Guide sections.
/// Maps to TemplateKey values for DataTemplate selection.
/// </summary>
public enum UserGuideSectionType
{
    Welcome,
    GettingStarted,
    Dashboard,
    AiChat,
    AskYourFiles,
    QuickActions,
    KnowledgeVault,
    Collections,
    KnowledgeGraph,
    WeeklyDigest,
    SemanticSearch,
    ModelManager,
    HardwareAdvisor,
    Settings,
    KeyboardShortcuts,
    CommandPalette,
    SupportedFileFormats,
    Troubleshooting,
    PrivacyAndSecurity,
    GettingHelp,
    TemporalIdentity,
    PastSelf,
    GenerativeIdentity,
    InsightHarvesting
}
