using AgentX.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Selectors;

/// <summary>
/// DataTemplateSelector for User Guide sections.
/// Returns the appropriate DataTemplate based on the section's TemplateKey.
/// </summary>
public class UserGuideSectionSelector : DataTemplateSelector
{
    // Core Sections
    public DataTemplate? WelcomeTemplate { get; set; }
    public DataTemplate? GettingStartedTemplate { get; set; }
    public DataTemplate? DashboardTemplate { get; set; }

    // Getting Started Tutorials (NEW)
    public DataTemplate? FirstChatTemplate { get; set; }
    public DataTemplate? FirstDocumentTemplate { get; set; }

    // Core Features
    public DataTemplate? AiChatTemplate { get; set; }
    public DataTemplate? AskYourFilesTemplate { get; set; }
    public DataTemplate? QuickActionsTemplate { get; set; }
    public DataTemplate? KnowledgeVaultTemplate { get; set; }
    public DataTemplate? CollectionsTemplate { get; set; }
    public DataTemplate? KnowledgeGraphTemplate { get; set; }
    public DataTemplate? SemanticSearchTemplate { get; set; }

    // Research & Web (NEW)
    public DataTemplate? WebSearchTemplate { get; set; }
    public DataTemplate? WebImportTemplate { get; set; }
    public DataTemplate? CitationsTemplate { get; set; }
    public DataTemplate? BrowserExtensionTemplate { get; set; }
    public DataTemplate? MobileCompanionTemplate { get; set; }

    // Temporal Identity
    public DataTemplate? WeeklyDigestTemplate { get; set; }
    public DataTemplate? TemporalIdentityTemplate { get; set; }
    public DataTemplate? PastSelfTemplate { get; set; }
    public DataTemplate? GenerativeIdentityTemplate { get; set; }
    public DataTemplate? InsightHarvestingTemplate { get; set; }

    // Automation (NEW)
    public DataTemplate? WorkflowsTemplate { get; set; }

    // Configuration
    public DataTemplate? ModelManagerTemplate { get; set; }
    public DataTemplate? HardwareAdvisorTemplate { get; set; }
    public DataTemplate? SettingsTemplate { get; set; }

    // Power User (NEW)
    public DataTemplate? CommandPaletteTemplate { get; set; }
    public DataTemplate? KeyboardShortcutsTemplate { get; set; }
    public DataTemplate? AdvancedQueriesTemplate { get; set; }
    public DataTemplate? PerformanceTuningTemplate { get; set; }

    // Reference
    public DataTemplate? SupportedFileFormatsTemplate { get; set; }
    public DataTemplate? ExportTemplate { get; set; }
    public DataTemplate? TroubleshootingTemplate { get; set; }
    public DataTemplate? PrivacyAndSecurityTemplate { get; set; }
    public DataTemplate? GettingHelpTemplate { get; set; }

    // Extended (NEW): Document management, integrations, extensibility
    public DataTemplate? AnnotationsTemplate { get; set; }
    public DataTemplate? AudioTranscriptionTemplate { get; set; }
    public DataTemplate? WorkspaceProfilesTemplate { get; set; }
    public DataTemplate? PluginsTemplate { get; set; }
    public DataTemplate? IntegrationsTemplate { get; set; }
    public DataTemplate? BackupRestoreTemplate { get; set; }

    // Insights & operations: pages shipped without guide coverage until 2026-07
    public DataTemplate? ComparisonTemplate { get; set; }
    public DataTemplate? InboxTemplate { get; set; }
    public DataTemplate? AnalyticsTemplate { get; set; }
    public DataTemplate? OperationsTemplate { get; set; }
    public DataTemplate? SyncTemplate { get; set; }

    // NOTE: WinUI 3's ItemsControl invokes the *two-argument* SelectTemplateCore
    // overload (item + container), NOT the single-argument one. If only the
    // single-arg overload is overridden, ItemsControl never calls the selector
    // and falls back to rendering item.ToString() (e.g. the raw record string).
    // We override the two-arg overload and delegate to the single-arg logic so
    // template selection works regardless of which overload the host calls.
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is UserGuideSection section)
        {
            return section.TemplateKey switch
            {
                // Core Sections
                "WelcomeTemplate" => WelcomeTemplate,
                "GettingStartedTemplate" => GettingStartedTemplate,
                "DashboardTemplate" => DashboardTemplate,

                // Getting Started Tutorials
                "FirstChatTemplate" => FirstChatTemplate,
                "FirstDocumentTemplate" => FirstDocumentTemplate,

                // Core Features
                "AiChatTemplate" => AiChatTemplate,
                "AskYourFilesTemplate" => AskYourFilesTemplate,
                "QuickActionsTemplate" => QuickActionsTemplate,
                "KnowledgeVaultTemplate" => KnowledgeVaultTemplate,
                "CollectionsTemplate" => CollectionsTemplate,
                "KnowledgeGraphTemplate" => KnowledgeGraphTemplate,
                "SemanticSearchTemplate" => SemanticSearchTemplate,

                // Research & Web
                "WebSearchTemplate" => WebSearchTemplate,
                "WebImportTemplate" => WebImportTemplate,
                "CitationsTemplate" => CitationsTemplate,
                "BrowserExtensionTemplate" => BrowserExtensionTemplate,
                "MobileCompanionTemplate" => MobileCompanionTemplate,

                // Temporal Identity
                "WeeklyDigestTemplate" => WeeklyDigestTemplate,
                "TemporalIdentityTemplate" => TemporalIdentityTemplate,
                "PastSelfTemplate" => PastSelfTemplate,
                "GenerativeIdentityTemplate" => GenerativeIdentityTemplate,
                "InsightHarvestingTemplate" => InsightHarvestingTemplate,

                // Automation
                "WorkflowsTemplate" => WorkflowsTemplate,

                // Configuration
                "ModelManagerTemplate" => ModelManagerTemplate,
                "HardwareAdvisorTemplate" => HardwareAdvisorTemplate,
                "SettingsTemplate" => SettingsTemplate,

                // Power User
                "CommandPaletteTemplate" => CommandPaletteTemplate,
                "KeyboardShortcutsTemplate" => KeyboardShortcutsTemplate,
                "AdvancedQueriesTemplate" => AdvancedQueriesTemplate,
                "PerformanceTuningTemplate" => PerformanceTuningTemplate,

                // Reference
                "SupportedFileFormatsTemplate" => SupportedFileFormatsTemplate,
                "ExportTemplate" => ExportTemplate,
                "TroubleshootingTemplate" => TroubleshootingTemplate,
                "PrivacyAndSecurityTemplate" => PrivacyAndSecurityTemplate,
                "GettingHelpTemplate" => GettingHelpTemplate,

                // Extended
                "AnnotationsTemplate" => AnnotationsTemplate,
                "AudioTranscriptionTemplate" => AudioTranscriptionTemplate,
                "WorkspaceProfilesTemplate" => WorkspaceProfilesTemplate,
                "PluginsTemplate" => PluginsTemplate,
                "IntegrationsTemplate" => IntegrationsTemplate,
                "BackupRestoreTemplate" => BackupRestoreTemplate,

                // Insights & operations
                "ComparisonTemplate" => ComparisonTemplate,
                "InboxTemplate" => InboxTemplate,
                "AnalyticsTemplate" => AnalyticsTemplate,
                "OperationsTemplate" => OperationsTemplate,
                "SyncTemplate" => SyncTemplate,

                _ => null
            };
        }
        return null;
    }
}
