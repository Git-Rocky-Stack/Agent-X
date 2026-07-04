using AgentX.App.Models;
using AgentX.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class UserGuideViewModelTests
{
    [Fact]
    public void Initial_state_populates_all_46_sections()
    {
        var sut = new UserGuideViewModel();

        sut.Sections.Should().HaveCount(46);
    }

    [Fact]
    public void Sections_are_in_display_order()
    {
        var sut = new UserGuideViewModel();

        var displayOrders = sut.Sections.Select(s => s.DisplayOrder).ToList();
        displayOrders.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Each_section_has_valid_template_key()
    {
        var sut = new UserGuideViewModel();

        // Every registered template key, in display order. Each key has a matching
        // property on UserGuideSectionSelector and a backing DataTemplate in the
        // Styles/UserGuideSections*.xaml resource dictionaries.
        var validTemplateKeys = new[]
        {
            // Welcome & Overview
            "WelcomeTemplate",
            "GettingStartedTemplate",
            "DashboardTemplate",
            // Getting Started Tutorials
            "FirstChatTemplate",
            "FirstDocumentTemplate",
            // Core Features
            "AiChatTemplate",
            "AskYourFilesTemplate",
            "QuickActionsTemplate",
            "KnowledgeVaultTemplate",
            "CollectionsTemplate",
            "KnowledgeGraphTemplate",
            "SemanticSearchTemplate",
            "ComparisonTemplate",
            // Research & Web
            "WebSearchTemplate",
            "WebImportTemplate",
            "CitationsTemplate",
            "BrowserExtensionTemplate",
            "MobileCompanionTemplate",
            // Document Management
            "InboxTemplate",
            "AnnotationsTemplate",
            "AudioTranscriptionTemplate",
            // Temporal Identity
            "TemporalIdentityTemplate",
            "PastSelfTemplate",
            "GenerativeIdentityTemplate",
            "InsightHarvestingTemplate",
            "WeeklyDigestTemplate",
            "AnalyticsTemplate",
            // Automation (on-demand pipelines only — no scheduler/rule engine exists)
            "WorkflowsTemplate",
            // Configuration
            "ModelManagerTemplate",
            "HardwareAdvisorTemplate",
            "SettingsTemplate",
            "WorkspaceProfilesTemplate",
            "PluginsTemplate",
            "IntegrationsTemplate",
            "SyncTemplate",
            "OperationsTemplate",
            // Power User
            "CommandPaletteTemplate",
            "KeyboardShortcutsTemplate",
            "AdvancedQueriesTemplate",
            "PerformanceTuningTemplate",
            // Reference
            "SupportedFileFormatsTemplate",
            "ExportTemplate",
            "BackupRestoreTemplate",
            "TroubleshootingTemplate",
            "PrivacyAndSecurityTemplate",
            "GettingHelpTemplate"
        };

        foreach (var section in sut.Sections)
        {
            section.TemplateKey.Should().BeOneOf(validTemplateKeys);
        }
    }

    [Fact]
    public void Each_section_has_title_key()
    {
        var sut = new UserGuideViewModel();

        foreach (var section in sut.Sections)
        {
            section.TitleKey.Should().NotBeNullOrEmpty();
            section.TitleKey.Should().StartWith("UserGuide_");
        }
    }

    [Fact]
    public void Each_section_has_icon_glyph()
    {
        var sut = new UserGuideViewModel();

        foreach (var section in sut.Sections)
        {
            section.IconGlyph.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Welcome_section_is_first()
    {
        var sut = new UserGuideViewModel();

        sut.Sections.First().TemplateKey.Should().Be("WelcomeTemplate");
        sut.Sections.First().DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void Getting_Help_section_is_last()
    {
        var sut = new UserGuideViewModel();

        sut.Sections.Last().TemplateKey.Should().Be("GettingHelpTemplate");
        sut.Sections.Last().DisplayOrder.Should().Be(46);
    }
}
