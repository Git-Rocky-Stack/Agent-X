using System.Text.RegularExpressions;
using AgentX.App.ViewModels;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Page code-behind built its dialogs, status lines and shortcut names from English literals,
/// and some rows showed stored tokens ("manual", "Research"), so those texts stayed English in
/// every language. The pages live in the WinUI project, which this test project cannot compile,
/// so the rules are checked on their source: the texts are read from the localization service,
/// the rows bind the translated names, and every text that names something keeps its
/// placeholder in all six languages.
/// </summary>
public sealed class PageTextsAreLocalizedTests
{
    private static readonly string[] Locales = { "en-US", "de", "es", "fr", "ja", "zh-CN" };

    /// <summary>A dialog or control text assigned a string literal.</summary>
    private static readonly Regex LiteralText = new(
        @"\b(Title|Content|PrimaryButtonText|CloseButtonText|SecondaryButtonText|PlaceholderText|Header|Text|StatusMessage)\s*=\s*\$?""");

    [Theory]
    [InlineData("BackupRestorePage.xaml.cs")]
    [InlineData("WorkflowBuilderPage.xaml.cs")]
    [InlineData("SettingsPage.xaml.cs")]
    [InlineData("EmailSettingsPage.xaml.cs")]
    [InlineData("CalendarSettingsPage.xaml.cs")]
    [InlineData("ChatPage.xaml.cs")]
    [InlineData("CollectionManagerPage.xaml.cs")]
    [InlineData("QuickChatWindow.xaml.cs")]
    public void Dialog_texts_come_from_the_resources(string codeBehind)
    {
        var source = ReadView(codeBehind);

        LiteralText.Matches(source).Select(match => match.Value).Should().BeEmpty(
            "{0} shows these texts to the user, so it reads them from the localization service", codeBehind);
    }

    [Fact]
    public void Texts_passed_to_dialogs_and_shortcuts_come_from_the_resources()
    {
        ReadView("WorkflowBuilderPage.xaml.cs").Should().NotContain("\"Export Workflow Result\"")
            .And.NotContain("Export Stored Run (")
            .And.NotContain("new TextBlock { Text = \"Format\" }");
        ReadView("SettingsPage.xaml.cs").Should().NotContain("\"Save settings\"")
            .And.NotContain("\"Settings\")");
        ReadView("EmailSettingsPage.xaml.cs").Should().NotContain("\"Outlook Email\"");
        ReadView("CalendarSettingsPage.xaml.cs").Should().NotContain("\"Google Calendar\"")
            .And.NotContain("\"Outlook Calendar\"");
        ReadView("KnowledgeVaultPage.xaml.cs").Should().NotContain("\"Refresh documents\"")
            .And.NotContain("\"Documents\")")
            .And.NotContain("\"Drop to import\"");
        ReadView("PluginManagerPage.xaml.cs").Should().NotContain("\"No description provided.\"");
        ReadView("ChatPage.xaml.cs").Should().NotContain("\"New conversation\"")
            .And.NotContain("\"Toggle conversation pane\"")
            .And.NotContain("\"Chat\")")
            .And.NotContain("\"Conversation\")");
    }

    [Theory]
    [InlineData("ChatPage.xaml.cs")]
    [InlineData("CollectionManagerPage.xaml.cs")]
    public void Export_notices_are_one_sentence_from_the_resources(string codeBehind)
    {
        // They appended an English ". Saved to {path}" to the translated export status.
        ReadView(codeBehind).Should().NotContain("Saved to")
            .And.NotContain("\"Export complete\"")
            .And.NotContain("\"Export failed\"")
            .And.Contain("GetString(\"Export_CompleteTitle\")")
            .And.Contain("GetString(\"Export_FailedTitle\")");
    }

    [Fact]
    public void Settings_local_api_texts_come_from_the_resources_in_plain_ascii()
    {
        var xaml = ReadView("SettingsPage.xaml");
        var english = ReswLocalization.For("en-US");

        // The description had no x:Uid, so it read English in every language.
        var description = english.GetString("Settings_LocalApiDescription.Text");
        xaml.Should().Contain("x:Uid=\"Settings_LocalApiDescription\"")
            .And.Contain($"Text=\"{description}\"", "the XAML fallback is the en-US text");

        var placeholder = english.GetString("Settings_NoTokenYetClickPh.PlaceholderText");
        xaml.Should().Contain($"PlaceholderText=\"{placeholder}\"", "the XAML fallback is the en-US text");

        new[] { description, placeholder }.Should().OnlyContain(
            text => text.All(c => c >= ' ' && c <= '~'), "the English texts are plain ASCII");

        foreach (var locale in Locales)
        {
            ReswLocalization.For(locale).GetString("Settings_NoTokenYetClickPh.PlaceholderText")
                .Should().NotContain("—", "{0} must not use an em dash", locale);
        }
    }

    [Fact]
    public void Theme_names_come_from_the_resources_and_match_the_caption()
    {
        var viewModel = File.ReadAllText(
            Path.Combine(ResolveSourceRoot(), "AgentX.App", "ViewModels", "SettingsViewModel.cs"));
        viewModel.Should().NotContain("\"System Default\"")
            .And.Contain("GetString(\"Settings_ThemeDark\")")
            .And.Contain("GetString(\"Settings_ThemeLight\")")
            .And.Contain("GetString(\"Settings_ThemeSystemDefault\")");

        // The caption under the picker quotes the System Default name, so both say the same.
        foreach (var locale in Locales)
        {
            var localization = ReswLocalization.For(locale);
            localization.GetString("Settings_ChangesApplyImmediatelySystem.Text")
                .Should().Contain(localization.GetString("Settings_ThemeSystemDefault"), "in {0}", locale);
        }
    }

    [Theory]
    [InlineData("Export_ConversationsSavedOne")]
    [InlineData("Export_ConversationsSavedMany")]
    [InlineData("Export_CollectionSaved")]
    [InlineData("Ops_SummaryAttentionMany")]
    [InlineData("Ops_SummaryMore")]
    [InlineData("Ops_RefreshSummariesError")]
    [InlineData("Ops_GeneratePreviewsError")]
    [InlineData("Ops_EnableConnectorError")]
    [InlineData("Ops_ReindexDocumentError")]
    [InlineData("Ops_FixRetryIndexingTitle")]
    [InlineData("Ops_FixEnableConnectorTitle")]
    [InlineData("Ops_DrillInConversation")]
    [InlineData("Ops_DrillInInboxItem")]
    [InlineData("Ops_DrillInDocument")]
    [InlineData("Ops_DrillInWorkflowRun")]
    [InlineData("Ops_DrillInSyncEntry")]
    [InlineData("Ops_DrillInConnector")]
    [InlineData("Ops_DrillInRecommendation")]
    [InlineData("Chat_StoryLeadStaleMany")]
    [InlineData("Chat_StoryRecallMany")]
    [InlineData("Chat_StorySentence")]
    [InlineData("Chat_StorySentenceOne")]
    [InlineData("Chat_StorySentenceTwo")]
    [InlineData("Chat_ChipRecallMatchesMany")]
    [InlineData("Chat_ExplainCompressionSkipped")]
    [InlineData("Chat_ExplainRecallAddedMany")]
    [InlineData("Chat_ExplainRecallSkipped")]
    public void Templates_keep_every_placeholder_in_every_language(string key)
    {
        var english = Placeholders(ReswLocalization.For("en-US").GetString(key));
        english.Should().NotBeEmpty("{0} is a template", key);

        foreach (var locale in Locales)
        {
            Placeholders(ReswLocalization.For(locale).GetString(key))
                .Should().BeEquivalentTo(english, "{0} in {1} must fill the same values", key, locale);
        }
    }

    private static IReadOnlyList<string> Placeholders(string template) =>
        Regex.Matches(template, @"\{\d+\}").Select(match => match.Value).Distinct().ToList();

    [Fact]
    public void Knowledge_graph_names_node_types_and_counts_in_the_users_language()
    {
        var source = ReadView("KnowledgeGraphPage.xaml.cs");

        source.Should().NotContain("node.NodeType.ToString()", "the enum member name is English");
        source.Should().NotContain(" connection{", "the tooltip count comes from Graph_ConnectionCount*");
        source.Should().NotContain("} found\"", "the match count comes from Graph_MatchCount*");
        source.Should().Contain("GetString(\"Graph_ConnectionCountOne\"")
            .And.Contain("GetString(\"Graph_MatchCountMany\"")
            .And.Contain("GetString(\"Graph_NodeTypeDocument\")");
    }

    [Fact]
    public void Backup_history_rows_show_the_translated_backup_type()
    {
        var xaml = ReadView("BackupRestorePage.xaml");

        xaml.Should().Contain($"Text=\"{{x:Bind {nameof(BackupHistoryItem.BackupTypeLabel)}}}\"");
        xaml.Should().NotContain("Text=\"{x:Bind BackupType}\"", "that is the stored type token");
    }

    [Fact]
    public void Workflow_categories_are_shown_by_their_translated_names()
    {
        var xaml = ReadView("WorkflowBuilderPage.xaml");

        // WorkflowListItem rows and WorkflowStarterTemplateDisplayItem cards both carry CategoryLabel.
        Regex.Matches(xaml, Regex.Escape($"Text=\"{{x:Bind {nameof(WorkflowListItem.CategoryLabel)}}}\""))
            .Should().HaveCount(2, "the workflow list and the template cards both name the category");
        xaml.Should().NotContain("Text=\"{x:Bind Category}\"", "that is the stored category");
        xaml.Should().Contain($"ItemsSource=\"{{x:Bind ViewModel.{nameof(WorkflowBuilderViewModel.CategoryOptions)}}}\"");
        xaml.Should().Contain(
            $"SelectedIndex=\"{{x:Bind ViewModel.{nameof(WorkflowBuilderViewModel.SelectedCategoryIndex)}, Mode=TwoWay}}\"");
        xaml.Should().NotContain("SelectedItem=\"{x:Bind ViewModel.EditCategory");
    }

    [Fact]
    public void Texts_that_name_something_keep_their_placeholder_in_every_language()
    {
        var keys = new[]
        {
            "EmailSet_DisconnectConfirmTitle",
            "EmailSet_DisconnectConfirmMessage",
            "CalSet_DisconnectConfirmTitle",
            "CalSet_DisconnectConfirmMessage",
            "WfBuilder_WorkflowCopied",
            "WfBuilder_ExportStoredRunTitle",
            "Graph_MatchCountOne",
            "Graph_MatchCountMany",
        };

        foreach (var locale in Locales)
        {
            var localization = ReswLocalization.For(locale);
            foreach (var key in keys)
            {
                localization.GetString(key, "[[name]]").Should().Contain(
                    "[[name]]", "the {0} text of {1} must show the name it is given", locale, key);
            }
        }
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Views", fileName));

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Agent-X source root from the test output directory.");
    }
}
