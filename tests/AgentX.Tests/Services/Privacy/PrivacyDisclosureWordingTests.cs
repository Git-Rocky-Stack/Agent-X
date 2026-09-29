using AgentX.Core.Helpers;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Privacy;

/// <summary>
/// The dashboard lists the privacy disclosures as PrivacyStatusService words them, and they read
/// "Calendar sync" and "Your prompts and conversation content are sent to OpenAI" in every
/// language. Core words them through FormatHelper.LocalizedText, which the app sets at startup;
/// without it the English is exactly what it was. Setting it changes shared state, so these run
/// in the collection that runs on its own.
/// </summary>
[Collection(nameof(FormatHelperLocalizedTextCollection))]
public sealed class PrivacyDisclosureWordingTests
{
    private static readonly string[] Locales = ["en-US", "de", "es", "fr", "ja", "zh-CN"];

    [Fact]
    public void With_the_english_resources_every_disclosure_reads_as_it_does_without_them()
    {
        var without = EvaluateAll();

        var withEnglish = WithResources("en-US", EvaluateAll);

        withEnglish.Should().Equal(without);
        without.Select(disclosure => disclosure.Surface).Should().Equal(
            "AI model", "Model routing", "Web search", "Calendar sync", "Email sync", "AI model", "Web search");
    }

    [Fact]
    public void With_the_german_resources_the_disclosures_are_in_german()
    {
        var disclosures = WithResources("de", EvaluateAll);

        disclosures.Should().Equal(
            new PrivacyDisclosure(
                "KI-Modell",
                "Ihre Prompts und Unterhaltungsinhalte werden zur Verarbeitung an OpenAI gesendet."),
            new PrivacyDisclosure(
                "Modell-Routing",
                "Das intelligente Modell-Routing kann Prompts an Ihren konfigurierten Cloud-KI-Anbieter senden."),
            new PrivacyDisclosure(
                "Websuche",
                "Wenn der Recherchemodus im Chat aktiv ist, werden Ihre Fragen an Serper (Google Search) gesendet."),
            new PrivacyDisclosure(
                "Kalendersynchronisierung",
                "Die Kalendersynchronisierung tauscht Daten mit Ihrem verbundenen Google- oder Microsoft-Konto aus."),
            new PrivacyDisclosure(
                "E-Mail-Synchronisierung",
                "Die E-Mail-Synchronisierung tauscht Daten mit Ihrem verbundenen Gmail- oder Outlook-Konto aus."),
            new PrivacyDisclosure(
                "KI-Modell",
                "Ihre Prompts und Unterhaltungsinhalte werden an den Ollama-Server unter 192.168.1.40 gesendet."),
            new PrivacyDisclosure(
                "Websuche",
                "Wenn der Recherchemodus im Chat aktiv ist, werden Ihre Fragen an die SearXNG-Instanz unter " +
                "searx.example.org gesendet, die sie an öffentliche Suchmaschinen weiterleitet."));
    }

    [Fact]
    public void Every_language_words_every_disclosure_and_names_where_the_data_goes()
    {
        var english = EvaluateAll();

        foreach (var locale in Locales)
        {
            var disclosures = WithResources(locale, EvaluateAll);

            disclosures.Should().HaveSameCount(english);
            disclosures.Should().OnlyContain(
                disclosure => !disclosure.Surface.StartsWith("Dash_", StringComparison.Ordinal) &&
                              !disclosure.Detail.StartsWith("Dash_", StringComparison.Ordinal),
                "{0} must have a resource for every disclosure", locale);
            disclosures[0].Detail.Should().Contain("OpenAI", "in {0}", locale);
            disclosures[2].Detail.Should().Contain("Serper (Google Search)", "in {0}", locale);
            disclosures[5].Detail.Should().Contain("192.168.1.40", "in {0}", locale);
            disclosures[6].Detail.Should().Contain("searx.example.org", "in {0}", locale);

            if (locale != "en-US")
            {
                disclosures.Select(disclosure => disclosure.Detail)
                    .Should().NotIntersectWith(english.Select(disclosure => disclosure.Detail), "{0} is translated", locale);
            }
        }
    }

    /// <summary>
    /// Every kind of disclosure: a cloud provider with model routing, a hosted search provider and
    /// both connectors, then Ollama on another machine with a SearXNG instance.
    /// </summary>
    private static List<PrivacyDisclosure> EvaluateAll()
    {
        var service = new PrivacyStatusService(Mock.Of<ISettingsService>());

        var cloud = new AppSettings
        {
            ActiveProviderId = "openai",
            OpenAiApiKey = "sk-test",
            EnableModelRouting = true,
            WebSearchProvider = WebSearchProvider.Serper,
            WebSearchApiKey = "serper-key",
        };
        cloud.CalendarConnector.EnableCalendarSync = true;
        cloud.EmailConnector.EnableEmailSync = true;

        var remote = new AppSettings
        {
            ActiveProviderId = "ollama",
            OllamaEndpoint = "http://192.168.1.40:11434",
            WebSearchProvider = WebSearchProvider.SearXng,
            WebSearchApiKey = "https://searx.example.org",
        };

        return service.Evaluate(cloud).Disclosures.Concat(service.Evaluate(remote).Disclosures).ToList();
    }

    private static T WithResources<T>(string locale, Func<T> evaluate)
    {
        var previous = FormatHelper.LocalizedText;
        FormatHelper.LocalizedText = ReswLocalization.For(locale).GetString;
        try
        {
            return evaluate();
        }
        finally
        {
            FormatHelper.LocalizedText = previous;
        }
    }
}
