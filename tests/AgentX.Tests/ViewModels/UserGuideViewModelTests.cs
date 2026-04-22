using AgentX.App.Models;
using AgentX.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class UserGuideViewModelTests
{
    [Fact]
    public void Initial_state_populates_all_20_sections()
    {
        var sut = new UserGuideViewModel();

        sut.Sections.Should().HaveCount(20);
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

        var validTemplateKeys = new[]
        {
            "WelcomeTemplate",
            "GettingStartedTemplate",
            "DashboardTemplate",
            "AiChatTemplate",
            "AskYourFilesTemplate",
            "QuickActionsTemplate",
            "KnowledgeVaultTemplate",
            "CollectionsTemplate",
            "KnowledgeGraphTemplate",
            "WeeklyDigestTemplate",
            "SemanticSearchTemplate",
            "ModelManagerTemplate",
            "HardwareAdvisorTemplate",
            "SettingsTemplate",
            "KeyboardShortcutsTemplate",
            "CommandPaletteTemplate",
            "SupportedFileFormatsTemplate",
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
        sut.Sections.Last().DisplayOrder.Should().Be(20);
    }
}
