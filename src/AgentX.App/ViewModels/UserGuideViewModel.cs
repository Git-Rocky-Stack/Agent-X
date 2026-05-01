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
            // Row 1: Welcome to Agent-X
            new UserGuideSection(
                TitleKey: "UserGuide_Welcome_Title",
                IconGlyph: "",
                TemplateKey: "WelcomeTemplate",
                DisplayOrder: 1
            ),

            // Row 2: Getting Started
            new UserGuideSection(
                TitleKey: "UserGuide_GettingStarted_Title",
                IconGlyph: "",
                TemplateKey: "GettingStartedTemplate",
                DisplayOrder: 2
            ),

            // Row 3: Dashboard
            new UserGuideSection(
                TitleKey: "UserGuide_Dashboard_Title",
                IconGlyph: "",
                TemplateKey: "DashboardTemplate",
                DisplayOrder: 3
            ),

            // Row 4: AI Chat
            new UserGuideSection(
                TitleKey: "UserGuide_AiChat_Title",
                IconGlyph: "",
                TemplateKey: "AiChatTemplate",
                DisplayOrder: 4
            ),

            // Row 5: Ask Your Files (RAG)
            new UserGuideSection(
                TitleKey: "UserGuide_AskYourFiles_Title",
                IconGlyph: "",
                TemplateKey: "AskYourFilesTemplate",
                DisplayOrder: 5
            ),

            // Row 6: Quick Actions
            new UserGuideSection(
                TitleKey: "UserGuide_QuickActions_Title",
                IconGlyph: "",
                TemplateKey: "QuickActionsTemplate",
                DisplayOrder: 6
            ),

            // Row 7: Knowledge Vault
            new UserGuideSection(
                TitleKey: "UserGuide_KnowledgeVault_Title",
                IconGlyph: "",
                TemplateKey: "KnowledgeVaultTemplate",
                DisplayOrder: 7
            ),

            // Row 8: Collections
            new UserGuideSection(
                TitleKey: "UserGuide_Collections_Title",
                IconGlyph: "",
                TemplateKey: "CollectionsTemplate",
                DisplayOrder: 8
            ),

            // Row 9: Knowledge Graph
            new UserGuideSection(
                TitleKey: "UserGuide_KnowledgeGraph_Title",
                IconGlyph: "",
                TemplateKey: "KnowledgeGraphTemplate",
                DisplayOrder: 9
            ),

            // Row 10: Weekly Digest
            new UserGuideSection(
                TitleKey: "UserGuide_WeeklyDigest_Title",
                IconGlyph: "",
                TemplateKey: "WeeklyDigestTemplate",
                DisplayOrder: 10
            ),

            // Row 11: Temporal Identity Coherence
            new UserGuideSection(
                TitleKey: "UserGuide_TemporalIdentity_Title",
                IconGlyph: "",
                TemplateKey: "TemporalIdentityTemplate",
                DisplayOrder: 11
            ),

            // Row 12: Past Self
            new UserGuideSection(
                TitleKey: "UserGuide_PastSelf_Title",
                IconGlyph: "",
                TemplateKey: "PastSelfTemplate",
                DisplayOrder: 12
            ),

            // Row 13: Generative Identity (Draft As Me)
            new UserGuideSection(
                TitleKey: "UserGuide_GenerativeIdentity_Title",
                IconGlyph: "",
                TemplateKey: "GenerativeIdentityTemplate",
                DisplayOrder: 13
            ),

            // Row 14: Insight Harvesting
            new UserGuideSection(
                TitleKey: "UserGuide_InsightHarvesting_Title",
                IconGlyph: "",
                TemplateKey: "InsightHarvestingTemplate",
                DisplayOrder: 14
            ),

            // Row 15: Semantic Search
            new UserGuideSection(
                TitleKey: "UserGuide_SemanticSearch_Title",
                IconGlyph: "",
                TemplateKey: "SemanticSearchTemplate",
                DisplayOrder: 15
            ),

            // Row 16: Model Manager
            new UserGuideSection(
                TitleKey: "UserGuide_ModelManager_Title",
                IconGlyph: "",
                TemplateKey: "ModelManagerTemplate",
                DisplayOrder: 16
            ),

            // Row 17: Hardware Advisor
            new UserGuideSection(
                TitleKey: "UserGuide_HardwareAdvisor_Title",
                IconGlyph: "",
                TemplateKey: "HardwareAdvisorTemplate",
                DisplayOrder: 17
            ),

            // Row 18: Settings
            new UserGuideSection(
                TitleKey: "UserGuide_Settings_Title",
                IconGlyph: "",
                TemplateKey: "SettingsTemplate",
                DisplayOrder: 18
            ),

            // Row 19: Keyboard Shortcuts
            new UserGuideSection(
                TitleKey: "UserGuide_KeyboardShortcuts_Title",
                IconGlyph: "",
                TemplateKey: "KeyboardShortcutsTemplate",
                DisplayOrder: 19
            ),

            // Row 20: Command Palette
            new UserGuideSection(
                TitleKey: "UserGuide_CommandPalette_Title",
                IconGlyph: "",
                TemplateKey: "CommandPaletteTemplate",
                DisplayOrder: 20
            ),

            // Row 21: Supported File Formats
            new UserGuideSection(
                TitleKey: "UserGuide_SupportedFileFormats_Title",
                IconGlyph: "",
                TemplateKey: "SupportedFileFormatsTemplate",
                DisplayOrder: 21
            ),

            // Row 22: Troubleshooting
            new UserGuideSection(
                TitleKey: "UserGuide_Troubleshooting_Title",
                IconGlyph: "",
                TemplateKey: "TroubleshootingTemplate",
                DisplayOrder: 22
            ),

            // Row 23: Privacy &amp; Security
            new UserGuideSection(
                TitleKey: "UserGuide_PrivacySecurity_Title",
                IconGlyph: "",
                TemplateKey: "PrivacyAndSecurityTemplate",
                DisplayOrder: 23
            ),

            // Row 24: Getting Help
            new UserGuideSection(
                TitleKey: "UserGuide_GettingHelp_Title",
                IconGlyph: "",
                TemplateKey: "GettingHelpTemplate",
                DisplayOrder: 24
            )
        };
    }
}
