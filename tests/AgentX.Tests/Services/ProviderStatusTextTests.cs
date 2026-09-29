using AgentX.App.Services;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// The status strip, the tray icon, the dashboard and chat name the active provider with the same
/// text. It used to be English whatever the user's language, and the tray's own copy was too.
/// </summary>
public sealed class ProviderStatusTextTests
{
    [Theory]
    [InlineData("local", "Built-in LLM", "built-in model")]
    [InlineData("ollama", "Ollama", "Ollama is running")]
    [InlineData("openai", "OpenAI", "OpenAI API key")]
    [InlineData("anthropic", "Anthropic Claude", "Anthropic Claude API key")]
    [InlineData(null, null, "Check the AI provider in Settings.")]
    public void CheckHint_NamesWhatToCheckForTheActiveProvider(string? providerId, string? providerName, string expected)
    {
        ProviderStatusText.CheckHint(null, providerId, providerName).Should().Contain(expected);
    }

    [Fact]
    public void CheckHint_ForACloudProviderWithoutADisplayName_NamesItsId()
    {
        ProviderStatusText.CheckHint(null, "openai", null)
            .Should().Be("Check the openai API key in Settings and your network connection.");
    }

    [Fact]
    public void Texts_AreReadFromTheUsersLanguage()
    {
        var german = ReswLocalization.For("de");

        ProviderStatusText.ConnectedTo(german, "Ollama").Should().Be("Verbunden mit Ollama");
        ProviderStatusText.NotAvailable(german, "Ollama").Should().Be("Ollama nicht verfügbar");
        ProviderStatusText.CheckHint(german, "ollama", "Ollama").Should().Be(
            "Prüfen Sie, ob Ollama läuft und ein Modell heruntergeladen ist und ob die Adresse in den Einstellungen stimmt.");
        ProviderStatusText.TrayTooltip(german, isConnected: true, "llama3.2", 42)
            .Should().Be("Agent-X | Verbunden | llama3.2 | 42 Dokumente");
    }

    [Fact]
    public void EnglishFallbacks_MatchTheShippedEnglishResources()
    {
        // Without a localization service (tests, early startup) the English fallback is shown, so it
        // must say exactly what the en-US resources say.
        var english = ReswLocalization.For("en-US");

        ProviderStatusText.GenericName(null).Should().Be(ProviderStatusText.GenericName(english));
        ProviderStatusText.ConnectedTo(null, "X").Should().Be(ProviderStatusText.ConnectedTo(english, "X"));
        ProviderStatusText.ConnectedToModel(null, "m").Should().Be(ProviderStatusText.ConnectedToModel(english, "m"));
        ProviderStatusText.NotAvailable(null, "X").Should().Be(ProviderStatusText.NotAvailable(english, "X"));
        foreach (var providerId in new[] { "local", "ollama", "openai", "anthropic", "other" })
        {
            ProviderStatusText.CheckHint(null, providerId, "X").Should().Be(ProviderStatusText.CheckHint(english, providerId, "X"));
        }

        ProviderStatusText.TrayTooltip(null, true, "m", 3).Should().Be(ProviderStatusText.TrayTooltip(english, true, "m", 3));
        ProviderStatusText.TrayTooltip(null, false, "m", 3).Should().Be(ProviderStatusText.TrayTooltip(english, false, "m", 3));
    }

    [Fact]
    public void TrayTooltip_NamesTheModelOnlyWhileConnectedAndTheDocumentsOnlyWhenThereAreAny()
    {
        ProviderStatusText.TrayTooltip(null, isConnected: true, "llama3.2", 42)
            .Should().Be("Agent-X | Connected | llama3.2 | 42 docs");
        ProviderStatusText.TrayTooltip(null, isConnected: false, "llama3.2", 42)
            .Should().Be("Agent-X | Disconnected | 42 docs");
        ProviderStatusText.TrayTooltip(null, isConnected: true, string.Empty, 0)
            .Should().Be("Agent-X | Connected");
    }
}
