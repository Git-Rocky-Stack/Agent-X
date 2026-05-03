using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AgentX.App.Models;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the User Guide page.
/// Exposes a collection of guide sections that the view renders via a DataTemplateSelector.
/// </summary>
public partial class UserGuideViewModel : ObservableObject
{
    /// <summary>
    /// All user guide sections in display order.
    /// </summary>
    public ObservableCollection<UserGuideSection> Sections { get; }

    public UserGuideViewModel()
    {
        Sections = new ObservableCollection<UserGuideSection>
        {
            // ═══════════════════════════════════════════════════════════════
            // WELCOME & OVERVIEW
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_Welcome_Title",
                IconGlyph: "",
                TemplateKey: "WelcomeTemplate",
                DisplayOrder: 1
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_GettingStarted_Title",
                IconGlyph: "\uDE75",
                TemplateKey: "GettingStartedTemplate",
                DisplayOrder: 2
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Dashboard_Title",
                IconGlyph: "",
                TemplateKey: "DashboardTemplate",
                DisplayOrder: 3
            ),

            // ═══════════════════════════════════════════════════════════════
            // GETTING STARTED TUTORIALS (NEW)
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_FirstChat_Title",
                IconGlyph: "",
                TemplateKey: "FirstChatTemplate",
                DisplayOrder: 4
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_FirstDocument_Title",
                IconGlyph: "",
                TemplateKey: "FirstDocumentTemplate",
                DisplayOrder: 5
            ),

            // ═══════════════════════════════════════════════════════════════
            // CORE FEATURES
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_AiChat_Title",
                IconGlyph: "",
                TemplateKey: "AiChatTemplate",
                DisplayOrder: 6
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_AskYourFiles_Title",
                IconGlyph: "",
                TemplateKey: "AskYourFilesTemplate",
                DisplayOrder: 7
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_QuickActions_Title",
                IconGlyph: "",
                TemplateKey: "QuickActionsTemplate",
                DisplayOrder: 8
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_KnowledgeVault_Title",
                IconGlyph: "",
                TemplateKey: "KnowledgeVaultTemplate",
                DisplayOrder: 9
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Collections_Title",
                IconGlyph: "",
                TemplateKey: "CollectionsTemplate",
                DisplayOrder: 10
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_KnowledgeGraph_Title",
                IconGlyph: "",
                TemplateKey: "KnowledgeGraphTemplate",
                DisplayOrder: 11
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_SemanticSearch_Title",
                IconGlyph: "",
                TemplateKey: "SemanticSearchTemplate",
                DisplayOrder: 12
            ),

            // ═══════════════════════════════════════════════════════════════
            // RESEARCH & WEB (NEW SECTIONS)
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_WebSearch_Title",
                IconGlyph: "",
                TemplateKey: "WebSearchTemplate",
                DisplayOrder: 13
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_WebImport_Title",
                IconGlyph: "",
                TemplateKey: "WebImportTemplate",
                DisplayOrder: 14
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Citations_Title",
                IconGlyph: "",
                TemplateKey: "CitationsTemplate",
                DisplayOrder: 15
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_BrowserExtension_Title",
                IconGlyph: "",
                TemplateKey: "BrowserExtensionTemplate",
                DisplayOrder: 16
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_MobileCompanion_Title",
                IconGlyph: "",
                TemplateKey: "MobileCompanionTemplate",
                DisplayOrder: 17
            ),

            // ═══════════════════════════════════════════════════════════════
            // TEMPORAL IDENTITY
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_TemporalIdentity_Title",
                IconGlyph: "",
                TemplateKey: "TemporalIdentityTemplate",
                DisplayOrder: 18
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_PastSelf_Title",
                IconGlyph: "",
                TemplateKey: "PastSelfTemplate",
                DisplayOrder: 19
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_GenerativeIdentity_Title",
                IconGlyph: "",
                TemplateKey: "GenerativeIdentityTemplate",
                DisplayOrder: 20
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_InsightHarvesting_Title",
                IconGlyph: "",
                TemplateKey: "InsightHarvestingTemplate",
                DisplayOrder: 21
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_WeeklyDigest_Title",
                IconGlyph: "",
                TemplateKey: "WeeklyDigestTemplate",
                DisplayOrder: 22
            ),

            // ═══════════════════════════════════════════════════════════════
            // AUTOMATION (NEW SECTIONS)
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_Workflows_Title",
                IconGlyph: "",
                TemplateKey: "WorkflowsTemplate",
                DisplayOrder: 23
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_ScheduledQueries_Title",
                IconGlyph: "",
                TemplateKey: "ScheduledQueriesTemplate",
                DisplayOrder: 24
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_AutomationRules_Title",
                IconGlyph: "",
                TemplateKey: "AutomationRulesTemplate",
                DisplayOrder: 25
            ),

            // ═══════════════════════════════════════════════════════════════
            // CONFIGURATION
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_ModelManager_Title",
                IconGlyph: "",
                TemplateKey: "ModelManagerTemplate",
                DisplayOrder: 26
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_HardwareAdvisor_Title",
                IconGlyph: "",
                TemplateKey: "HardwareAdvisorTemplate",
                DisplayOrder: 27
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Settings_Title",
                IconGlyph: "",
                TemplateKey: "SettingsTemplate",
                DisplayOrder: 28
            ),

            // ═══════════════════════════════════════════════════════════════
            // POWER USER
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_CommandPalette_Title",
                IconGlyph: "",
                TemplateKey: "CommandPaletteTemplate",
                DisplayOrder: 29
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_KeyboardShortcuts_Title",
                IconGlyph: "",
                TemplateKey: "KeyboardShortcutsTemplate",
                DisplayOrder: 30
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_AdvancedQueries_Title",
                IconGlyph: "",
                TemplateKey: "AdvancedQueriesTemplate",
                DisplayOrder: 31
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_PerformanceTuning_Title",
                IconGlyph: "",
                TemplateKey: "PerformanceTuningTemplate",
                DisplayOrder: 32
            ),

            // ═══════════════════════════════════════════════════════════════
            // REFERENCE
            // ═══════════════════════════════════════════════════════════════

            new UserGuideSection(
                TitleKey: "UserGuide_SupportedFileFormats_Title",
                IconGlyph: "",
                TemplateKey: "SupportedFileFormatsTemplate",
                DisplayOrder: 33
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Export_Title",
                IconGlyph: "",
                TemplateKey: "ExportTemplate",
                DisplayOrder: 34
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Troubleshooting_Title",
                IconGlyph: "",
                TemplateKey: "TroubleshootingTemplate",
                DisplayOrder: 35
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_PrivacySecurity_Title",
                IconGlyph: "",
                TemplateKey: "PrivacyAndSecurityTemplate",
                DisplayOrder: 36
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_GettingHelp_Title",
                IconGlyph: "",
                TemplateKey: "GettingHelpTemplate",
                DisplayOrder: 37
            )
        };
    }
}
