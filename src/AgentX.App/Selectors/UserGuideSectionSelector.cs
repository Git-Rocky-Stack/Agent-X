using Microsoft.UI.Xaml.Controls;
using AgentX.App.Models;

namespace AgentX.App.Selectors;

/// <summary>
/// DataTemplateSelector for User Guide sections.
/// Returns the appropriate DataTemplate based on the section's TemplateKey.
/// </summary>
public class UserGuideSectionSelector : DataTemplateSelector
{
    public DataTemplate? WelcomeTemplate { get; set; }
    public DataTemplate? GettingStartedTemplate { get; set; }
    public DataTemplate? DashboardTemplate { get; set; }
    public DataTemplate? AiChatTemplate { get; set; }
    public DataTemplate? AskYourFilesTemplate { get; set; }
    public DataTemplate? QuickActionsTemplate { get; set; }
    public DataTemplate? KnowledgeVaultTemplate { get; set; }
    public DataTemplate? CollectionsTemplate { get; set; }
    public DataTemplate? KnowledgeGraphTemplate { get; set; }
    public DataTemplate? WeeklyDigestTemplate { get; set; }
    public DataTemplate? SemanticSearchTemplate { get; set; }
    public DataTemplate? ModelManagerTemplate { get; set; }
    public DataTemplate? HardwareAdvisorTemplate { get; set; }
    public DataTemplate? SettingsTemplate { get; set; }
    public DataTemplate? KeyboardShortcutsTemplate { get; set; }
    public DataTemplate? CommandPaletteTemplate { get; set; }
    public DataTemplate? SupportedFileFormatsTemplate { get; set; }
    public DataTemplate? TroubleshootingTemplate { get; set; }
    public DataTemplate? PrivacyAndSecurityTemplate { get; set; }
    public DataTemplate? GettingHelpTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is UserGuideSection section)
        {
            return section.TemplateKey switch
            {
                "WelcomeTemplate" => WelcomeTemplate,
                "GettingStartedTemplate" => GettingStartedTemplate,
                "DashboardTemplate" => DashboardTemplate,
                "AiChatTemplate" => AiChatTemplate,
                "AskYourFilesTemplate" => AskYourFilesTemplate,
                "QuickActionsTemplate" => QuickActionsTemplate,
                "KnowledgeVaultTemplate" => KnowledgeVaultTemplate,
                "CollectionsTemplate" => CollectionsTemplate,
                "KnowledgeGraphTemplate" => KnowledgeGraphTemplate,
                "WeeklyDigestTemplate" => WeeklyDigestTemplate,
                "SemanticSearchTemplate" => SemanticSearchTemplate,
                "ModelManagerTemplate" => ModelManagerTemplate,
                "HardwareAdvisorTemplate" => HardwareAdvisorTemplate,
                "SettingsTemplate" => SettingsTemplate,
                "KeyboardShortcutsTemplate" => KeyboardShortcutsTemplate,
                "CommandPaletteTemplate" => CommandPaletteTemplate,
                "SupportedFileFormatsTemplate" => SupportedFileFormatsTemplate,
                "TroubleshootingTemplate" => TroubleshootingTemplate,
                "PrivacyAndSecurityTemplate" => PrivacyAndSecurityTemplate,
                "GettingHelpTemplate" => GettingHelpTemplate,
                _ => null
            };
        }
        return null;
    }
}
