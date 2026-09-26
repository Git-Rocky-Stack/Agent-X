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
        else if (string.Equals(settings.ActiveProviderId, "ollama", StringComparison.OrdinalIgnoreCase)
                 && OffMachineHost(settings.OllamaEndpoint) is { } ollamaHost)
        {
            // Ollama runs wherever its endpoint points; only a loopback endpoint keeps inference
            // on this machine.
            disclosures.Add(new PrivacyDisclosure(
                "AI model",
                $"Your prompts and conversation content are sent to the Ollama server at {ollamaHost}."));
        }

        // 2) Multi-model routing can dispatch requests to a configured cloud provider. Only a concern
        //    when routing is on AND at least one cloud provider key is configured to route to.
        if (settings.EnableModelRouting && HasCloudAiKey(settings))
        {
            disclosures.Add(new PrivacyDisclosure(
                "Model routing",
                "Smart model routing may send prompts to your configured cloud AI provider."));
        }

        // 3) Web search. Research Mode is switched on per conversation in chat, independently of
        //    the settings toggle, so any configured provider can receive queries. The check uses
        //    the same configuration the search service uses. SearXNG is disclosed too: even a
        //    local instance forwards the queries to public search engines.
        var webSearch = WebSearchConfiguration.FromSettings(settings);
        if (webSearch.IsConfigured)
        {
            disclosures.Add(new PrivacyDisclosure("Web search", WebSearchDetail(webSearch)));
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

    /// <summary>Where research-mode queries go for a configured web search provider.</summary>
    private static string WebSearchDetail(WebSearchConfiguration webSearch) => webSearch.Provider switch
    {
        WebSearchProvider.SearXng =>
            $"When Research Mode is on in chat, your questions are sent to the SearXNG instance at {webSearch.SearXngUrl!.Host}, which forwards them to public search engines.",
        WebSearchProvider.Serper =>
            "When Research Mode is on in chat, your questions are sent to Serper (Google Search).",
        _ =>
            "When Research Mode is on in chat, your questions are sent to Brave Search.",
    };

    /// <summary>
    /// Returns the host of an endpoint that is not on this machine, or null for a loopback
    /// endpoint (localhost, 127.0.0.0/8, ::1) or one that cannot be parsed.
    /// </summary>
    private static string? OffMachineHost(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.Trim('[', ']').TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address)))
        {
            return null;
        }

        return host;
    }
}
