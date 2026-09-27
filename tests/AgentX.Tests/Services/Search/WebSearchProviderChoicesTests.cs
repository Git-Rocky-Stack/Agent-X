using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

/// <summary>
/// The Settings page listed the WebSearchProvider members themselves, so SearXNG read "SearXng".
/// The picker now shows the product names and still saves the enum value.
/// </summary>
public sealed class WebSearchProviderChoicesTests
{
    [Fact]
    public void Every_provider_is_offered_once_by_its_product_name()
    {
        WebSearchProviderChoices.All.Select(choice => choice.Provider)
            .Should().BeEquivalentTo(Enum.GetValues<WebSearchProvider>(), "a provider the enum gains must reach the picker");
        WebSearchProviderChoices.All.Select(choice => choice.Provider).Should().OnlyHaveUniqueItems();
        WebSearchProviderChoices.DisplayNames.Should().Equal("Brave", "Serper", "SearXNG");
    }

    [Theory]
    [InlineData(WebSearchProvider.Brave)]
    [InlineData(WebSearchProvider.Serper)]
    [InlineData(WebSearchProvider.SearXng)]
    public void Every_provider_round_trips_through_the_picker(WebSearchProvider provider)
    {
        var index = WebSearchProviderChoices.IndexOf(provider);

        index.Should().BeGreaterThanOrEqualTo(0);
        WebSearchProviderChoices.ResolveSelection(index, WebSearchProvider.Brave).Should().Be(provider);
    }

    [Fact]
    public void The_default_provider_is_selected_and_an_unknown_saved_value_is_kept()
    {
        var defaultProvider = new AppSettings().WebSearchProvider;
        WebSearchProviderChoices.IndexOf(defaultProvider).Should().Be(0);

        // A value this build does not know (a newer settings file) shows no selection and is
        // saved back as it was.
        var unknown = (WebSearchProvider)42;
        WebSearchProviderChoices.IndexOf(unknown).Should().Be(-1);
        WebSearchProviderChoices.ResolveSelection(-1, unknown).Should().Be(unknown);
        WebSearchProviderChoices.ResolveSelection(WebSearchProviderChoices.All.Count, unknown).Should().Be(unknown);
    }

    [Fact]
    public void The_settings_page_shows_the_names_and_saves_the_provider_by_index()
    {
        // SettingsPage and SettingsViewModel live in the WinUI project, which this test project
        // cannot compile, so the binding is checked on their source.
        var app = Path.Combine(ResolveSourceRoot(), "AgentX.App");
        var xaml = File.ReadAllText(Path.Combine(app, "Views", "SettingsPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(app, "ViewModels", "SettingsViewModel.cs"));

        xaml.Should().Contain("ItemsSource=\"{x:Bind ViewModel.WebSearchProviderOptions}\"")
            .And.Contain("SelectedIndex=\"{x:Bind ViewModel.WebSearchProviderIndex, Mode=TwoWay}\"");
        viewModel.Should().Contain("WebSearchProviderOptions { get; } = WebSearchProviderChoices.DisplayNames;")
            .And.Contain("WebSearchProviderIndex = WebSearchProviderChoices.IndexOf(settings.WebSearchProvider);")
            .And.Contain("settings.WebSearchProvider = WebSearchProviderChoices.ResolveSelection(WebSearchProviderIndex, settings.WebSearchProvider);")
            .And.NotContain("Enum.GetValues<WebSearchProvider>()");
    }

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
