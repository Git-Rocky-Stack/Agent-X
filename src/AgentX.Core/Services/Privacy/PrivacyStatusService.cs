using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Privacy;

/// <summary>
/// Default <see cref="IPrivacyStatusService"/>. <see cref="Evaluate"/> is a pure function of an
/// <see cref="AppSettings"/> snapshot — no I/O — so it is exhaustively unit-testable; the async
/// member only loads the current settings before delegating to it.
/// </summary>
public sealed class PrivacyStatusService : IPrivacyStatusService
{
    private readonly ISettingsService _settingsService;

    public PrivacyStatusService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public async Task<PrivacyStatus> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
        return Evaluate(settings);
    }

    public PrivacyStatus Evaluate(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var disclosures = new List<PrivacyDisclosure>();

        // 1) Active AI provider is a hosted cloud model — prompts and conversation content leave.
        var cloudProviderName = CloudAiProviderName(settings.ActiveProviderId);
        if (cloudProviderName is not null)
        {
            disclosures.Add(new PrivacyDisclosure(
                "AI model",
                $"Your prompts and conversation content are sent to {cloudProviderName} for processing."));
        }

        // 2) Multi-model routing can dispatch requests to a configured cloud provider. Only a concern
        //    when routing is on AND at least one cloud provider key is configured to route to.
        if (settings.EnableModelRouting && HasCloudAiKey(settings))
        {
            disclosures.Add(new PrivacyDisclosure(
                "Model routing",
                "Smart model routing may send prompts to your configured cloud AI provider."));
        }

        // 3) Deep Research web search via a hosted provider sends queries off-machine. A self-hosted
        //    SearXNG instance stays local, so it is not disclosed.
        var cloudSearchName = settings.EnableResearchMode
            ? CloudSearchProviderName(settings.WebSearchProvider)
            : null;
        if (cloudSearchName is not null)
        {
            disclosures.Add(new PrivacyDisclosure(
                "Web search",
                $"Research mode sends your search queries to {cloudSearchName}."));
        }

        // 4) Calendar connector exchanges data with Google/Microsoft.
        if (settings.CalendarConnector.EnableCalendarSync)
        {
            disclosures.Add(new PrivacyDisclosure(
                "Calendar sync",
                "Calendar sync exchanges data with your connected Google or Microsoft account."));
        }

        // 5) Email connector exchanges data with Gmail/Outlook.
        if (settings.EmailConnector.EnableEmailSync)
        {
            disclosures.Add(new PrivacyDisclosure(
                "Email sync",
                "Email sync exchanges data with your connected Gmail or Outlook account."));
        }

        return disclosures.Count == 0
            ? PrivacyStatus.FullyLocal
            : new PrivacyStatus(false, disclosures);
    }

    /// <summary>
    /// Returns the display name of a hosted cloud AI provider, or null for on-machine providers
    /// ("local" LLamaSharp, "ollama") whose inference never leaves the device.
    /// </summary>
    private static string? CloudAiProviderName(string? providerId) => providerId?.ToLowerInvariant() switch
    {
        "openai" => "OpenAI",
        "anthropic" => "Anthropic",
        _ => null
    };

    private static bool HasCloudAiKey(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiApiKey) ||
        !string.IsNullOrWhiteSpace(settings.AnthropicApiKey);

    /// <summary>
    /// Returns the display name of a hosted web-search provider, or null for the self-hosted
    /// <see cref="WebSearchProvider.SearXng"/> option, which can run entirely on the user's network.
    /// </summary>
    private static string? CloudSearchProviderName(WebSearchProvider provider) => provider switch
    {
        WebSearchProvider.Brave => "Brave Search",
        WebSearchProvider.Serper => "Serper (Google Search)",
        _ => null
    };
}
