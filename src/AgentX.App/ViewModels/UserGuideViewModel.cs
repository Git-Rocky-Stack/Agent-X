using System.Collections.ObjectModel;
using AgentX.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

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
            // ===============================================================
            // WELCOME & OVERVIEW
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_Welcome_Title",
                IconGlyph: "\uE819",
                TemplateKey: "WelcomeTemplate",
                DisplayOrder: 1
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_GettingStarted_Title",
                IconGlyph: "\uE7B5",
                TemplateKey: "GettingStartedTemplate",
                DisplayOrder: 2
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Dashboard_Title",
                IconGlyph: "\uE80F",
                TemplateKey: "DashboardTemplate",
                DisplayOrder: 3
            ),

            // ===============================================================
            // GETTING STARTED TUTORIALS (NEW)
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_FirstChat_Title",
                IconGlyph: "\uE8BD",
                TemplateKey: "FirstChatTemplate",
                DisplayOrder: 4
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_FirstDocument_Title",
                IconGlyph: "\uE82D",
                TemplateKey: "FirstDocumentTemplate",
                DisplayOrder: 5
            ),

            // ===============================================================
            // CORE FEATURES
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_AiChat_Title",
                IconGlyph: "\uE8BD",
                TemplateKey: "AiChatTemplate",
                DisplayOrder: 6
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_AskYourFiles_Title",
                IconGlyph: "\uE82D",
                TemplateKey: "AskYourFilesTemplate",
                DisplayOrder: 7
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_QuickActions_Title",
                IconGlyph: "\uE945",
                TemplateKey: "QuickActionsTemplate",
                DisplayOrder: 8
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_KnowledgeVault_Title",
                IconGlyph: "\uE8F1",
                TemplateKey: "KnowledgeVaultTemplate",
                DisplayOrder: 9
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Collections_Title",
                IconGlyph: "\uE8B7",
                TemplateKey: "CollectionsTemplate",
                DisplayOrder: 10
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_KnowledgeGraph_Title",
                IconGlyph: "\uE9D9",
                TemplateKey: "KnowledgeGraphTemplate",
                DisplayOrder: 11
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_SemanticSearch_Title",
                IconGlyph: "\uE721",
                TemplateKey: "SemanticSearchTemplate",
                DisplayOrder: 12
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Comparison_Title",
                IconGlyph: "\uE8F4",
                TemplateKey: "ComparisonTemplate",
                DisplayOrder: 13
            ),

            // ===============================================================
            // RESEARCH & WEB (NEW SECTIONS)
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_WebSearch_Title",
                IconGlyph: "\uE773",
                TemplateKey: "WebSearchTemplate",
                DisplayOrder: 14
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_WebImport_Title",
                IconGlyph: "\uE895",
                TemplateKey: "WebImportTemplate",
                DisplayOrder: 15
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Citations_Title",
                IconGlyph: "\uE8D7",
                TemplateKey: "CitationsTemplate",
                DisplayOrder: 16
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_BrowserExtension_Title",
                IconGlyph: "\uE774",
                TemplateKey: "BrowserExtensionTemplate",
                DisplayOrder: 17
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_MobileCompanion_Title",
                IconGlyph: "\uE8EA",
                TemplateKey: "MobileCompanionTemplate",
                DisplayOrder: 18
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Inbox_Title",
                IconGlyph: "\uE8B7",
                TemplateKey: "InboxTemplate",
                DisplayOrder: 19
            ),

            // ===============================================================
            // DOCUMENT MANAGEMENT (NEW)
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_Annotations_Title",
                IconGlyph: "\uED63",
                TemplateKey: "AnnotationsTemplate",
                DisplayOrder: 20
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_AudioTranscription_Title",
                IconGlyph: "\uE720",
                TemplateKey: "AudioTranscriptionTemplate",
                DisplayOrder: 21
            ),

            // ===============================================================
            // TEMPORAL IDENTITY
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_TemporalIdentity_Title",
                IconGlyph: "\uE8FD",
                TemplateKey: "TemporalIdentityTemplate",
                DisplayOrder: 22
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_PastSelf_Title",
                IconGlyph: "\uE8B7",
                TemplateKey: "PastSelfTemplate",
                DisplayOrder: 23
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_GenerativeIdentity_Title",
                IconGlyph: "\uE8C8",
                TemplateKey: "GenerativeIdentityTemplate",
                DisplayOrder: 24
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_InsightHarvesting_Title",
                IconGlyph: "\uE90F",
                TemplateKey: "InsightHarvestingTemplate",
                DisplayOrder: 25
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_WeeklyDigest_Title",
                IconGlyph: "\uE9D2",
                TemplateKey: "WeeklyDigestTemplate",
                DisplayOrder: 26
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Analytics_Title",
                IconGlyph: "\uE9D2",
                TemplateKey: "AnalyticsTemplate",
                DisplayOrder: 27
            ),

            // ===============================================================
            // AUTOMATION (NEW SECTIONS)
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_Workflows_Title",
                IconGlyph: "\uE9C9",
                TemplateKey: "WorkflowsTemplate",
                DisplayOrder: 28
            ),

            // ===============================================================
            // CONFIGURATION
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_ModelManager_Title",
                IconGlyph: "\uE74C",
                TemplateKey: "ModelManagerTemplate",
                DisplayOrder: 29
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_HardwareAdvisor_Title",
                IconGlyph: "\uE977",
                TemplateKey: "HardwareAdvisorTemplate",
                DisplayOrder: 30
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Settings_Title",
                IconGlyph: "\uE713",
                TemplateKey: "SettingsTemplate",
                DisplayOrder: 31
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_WorkspaceProfiles_Title",
                IconGlyph: "\uE7EE",
                TemplateKey: "WorkspaceProfilesTemplate",
                DisplayOrder: 32
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Plugins_Title",
                IconGlyph: "\uEA86",
                TemplateKey: "PluginsTemplate",
                DisplayOrder: 33
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Integrations_Title",
                IconGlyph: "\uE787",
                TemplateKey: "IntegrationsTemplate",
                DisplayOrder: 34
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Sync_Title",
                IconGlyph: "\uE895",
                TemplateKey: "SyncTemplate",
                DisplayOrder: 35
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Operations_Title",
                IconGlyph: "\uE7BA",
                TemplateKey: "OperationsTemplate",
                DisplayOrder: 36
            ),

            // ===============================================================
            // POWER USER
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_CommandPalette_Title",
                IconGlyph: "\uE733",
                TemplateKey: "CommandPaletteTemplate",
                DisplayOrder: 37
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_KeyboardShortcuts_Title",
                IconGlyph: "\uE765",
                TemplateKey: "KeyboardShortcutsTemplate",
                DisplayOrder: 38
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_AdvancedQueries_Title",
                IconGlyph: "\uE947",
                TemplateKey: "AdvancedQueriesTemplate",
                DisplayOrder: 39
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_PerformanceTuning_Title",
                IconGlyph: "\uE9C9",
                TemplateKey: "PerformanceTuningTemplate",
                DisplayOrder: 40
            ),

            // ===============================================================
            // REFERENCE
            // ===============================================================

            new UserGuideSection(
                TitleKey: "UserGuide_SupportedFileFormats_Title",
                IconGlyph: "\uE825",
                TemplateKey: "SupportedFileFormatsTemplate",
                DisplayOrder: 41
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Export_Title",
                IconGlyph: "\uE89C",
                TemplateKey: "ExportTemplate",
                DisplayOrder: 42
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_BackupRestore_Title",
                IconGlyph: "\uE777",
                TemplateKey: "BackupRestoreTemplate",
                DisplayOrder: 43
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_Troubleshooting_Title",
                IconGlyph: "\uE90F",
                TemplateKey: "TroubleshootingTemplate",
                DisplayOrder: 44
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_PrivacySecurity_Title",
                IconGlyph: "\uE72E",
                TemplateKey: "PrivacyAndSecurityTemplate",
                DisplayOrder: 45
            ),

            new UserGuideSection(
                TitleKey: "UserGuide_GettingHelp_Title",
                IconGlyph: "\uE897",
                TemplateKey: "GettingHelpTemplate",
                DisplayOrder: 46
            )
        };
    }
}
