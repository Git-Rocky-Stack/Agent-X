using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Privacy;

/// <summary>
/// Default <see cref="IPrivacyStatusService"/>. <see cref="Evaluate"/> is a pure function of an
/// <see cref="AppSettings"/> snapshot with no I/O, so it is exhaustively unit-testable; the async
/// member only loads the current settings before delegating to it. The dashboard shows the
/// disclosures as they are, so they are worded in the user's language through
/// <see cref="FormatHelper.LocalizedText"/> (English until the app sets it).
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

        var words = LocalizedWords.Current;

        // 1-3) Everything a prompt can reach: the AI model, model routing and web search. Research
        //      Mode is switched on per conversation in chat, so any configured search provider
        //      counts here whatever the settings toggle says.
        var disclosures = PromptRecipients(settings, settings.ActiveProviderId, includeWebSearch: true)
            .Select(recipient => Disclose(recipient, words))
            .ToList();

        // 4) Calendar connector exchanges data with Google/Microsoft.
        if (settings.CalendarConnector.EnableCalendarSync)
        {
            disclosures.Add(new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceCalendarSync", "Calendar sync"),
                words.GetString(
                    "Dash_PrivacyDetailCalendarSync",
                    "Calendar sync exchanges data with your connected Google or Microsoft account.")));
        }

        // 5) Email connector exchanges data with Gmail/Outlook.
        if (settings.EmailConnector.EnableEmailSync)
        {
            disclosures.Add(new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceEmailSync", "Email sync"),
                words.GetString(
                    "Dash_PrivacyDetailEmailSync",
                    "Email sync exchanges data with your connected Gmail or Outlook account.")));
        }

        return disclosures.Count == 0
            ? PrivacyStatus.FullyLocal
            : new PrivacyStatus(false, disclosures);
    }

    public async Task<IReadOnlyList<PromptRecipient>> GetChatMessageRecipientsAsync(
        string? activeProviderId,
        bool researchModeOn,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
        return EvaluateChatMessage(settings, activeProviderId, researchModeOn);
    }

    public IReadOnlyList<PromptRecipient> EvaluateChatMessage(AppSettings settings, string? activeProviderId, bool researchModeOn)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        // Chat searches the web only while Research Mode is on for the message and switched on in
        // Settings (MessagingCoordinator.BuildResearchContextAsync), so only then does the search
        // provider receive the message.
        return PromptRecipients(
            settings,
            string.IsNullOrWhiteSpace(activeProviderId) ? settings.ActiveProviderId : activeProviderId,
            includeWebSearch: researchModeOn && settings.EnableResearchMode);
    }

    /// <summary>
    /// Where a prompt goes off this computer, for the provider <paramref name="providerId"/>: the
    /// AI model when it is hosted or on another machine, model routing when it can reach a cloud
    /// provider, and, with <paramref name="includeWebSearch"/>, the configured web search provider.
    /// Shared by <see cref="Evaluate"/> and <see cref="EvaluateChatMessage"/> so the dashboard, the
    /// privacy lamp and chat can never disagree about it.
    /// </summary>
    private static List<PromptRecipient> PromptRecipients(AppSettings settings, string? providerId, bool includeWebSearch)
    {
        var recipients = new List<PromptRecipient>();

        // The AI model: a hosted provider always leaves the machine. Ollama runs wherever its
        // endpoint points; only a loopback endpoint keeps inference on this machine.
        if (CloudAiProviderName(providerId) is { } cloudProviderName)
        {
            recipients.Add(new PromptRecipient(PromptRecipientKind.CloudAiProvider, cloudProviderName));
        }
        else if (string.Equals(providerId?.Trim(), "ollama", StringComparison.OrdinalIgnoreCase)
                 && OffMachineHost(settings.OllamaEndpoint) is { } ollamaHost)
        {
            recipients.Add(new PromptRecipient(PromptRecipientKind.RemoteOllama, ollamaHost));
        }

        // Multi-model routing can dispatch requests to a configured cloud provider. Only a concern
        // when routing is on AND at least one cloud provider key is configured to route to.
        if (settings.EnableModelRouting && HasCloudAiKey(settings))
        {
            recipients.Add(new PromptRecipient(PromptRecipientKind.ModelRouting, null));
        }

        // Web search uses the same configuration the search service uses. SearXNG counts too:
        // even a local instance forwards the queries to public search engines.
        var webSearch = WebSearchConfiguration.FromSettings(settings);
        if (includeWebSearch && webSearch.IsConfigured)
        {
            recipients.Add(webSearch.Provider == WebSearchProvider.SearXng
                ? new PromptRecipient(PromptRecipientKind.SearXng, webSearch.SearXngUrl!.Host)
                : new PromptRecipient(PromptRecipientKind.WebSearch, WebSearchProviderName(webSearch.Provider)));
        }

        return recipients;
    }

    /// <summary>The dashboard's disclosure for a place prompts go.</summary>
    private static PrivacyDisclosure Disclose(PromptRecipient recipient, LocalizedWords words)
    {
        var name = recipient.Name ?? string.Empty;
        return recipient.Kind switch
        {
            PromptRecipientKind.CloudAiProvider => new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceAiModel", "AI model"),
                words.GetString(
                    "Dash_PrivacyDetailCloudAi",
                    "Your prompts and conversation content are sent to {0} for processing.",
                    name)),
            PromptRecipientKind.RemoteOllama => new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceAiModel", "AI model"),
                words.GetString(
                    "Dash_PrivacyDetailRemoteOllama",
                    "Your prompts and conversation content are sent to the Ollama server at {0}.",
                    name)),
            PromptRecipientKind.ModelRouting => new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceModelRouting", "Model routing"),
                words.GetString(
                    "Dash_PrivacyDetailModelRouting",
                    "Smart model routing may send prompts to your configured cloud AI provider.")),
            PromptRecipientKind.SearXng => new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceWebSearch", "Web search"),
                words.GetString(
                    "Dash_PrivacyDetailSearXng",
                    "When Research Mode is on in chat, your questions are sent to the SearXNG instance at {0}, which forwards them to public search engines.",
                    name)),
            _ => new PrivacyDisclosure(
                words.GetString("Dash_PrivacySurfaceWebSearch", "Web search"),
                words.GetString(
                    "Dash_PrivacyDetailWebSearch",
                    "When Research Mode is on in chat, your questions are sent to {0}.",
                    name)),
        };
    }

    /// <summary>
    /// Returns the display name of a hosted cloud AI provider, or null for on-machine providers
    /// ("local" LLamaSharp, "ollama") whose inference never leaves the device.
    /// </summary>
    private static string? CloudAiProviderName(string? providerId) => providerId?.Trim().ToLowerInvariant() switch
    {
        "openai" => "OpenAI",
        "anthropic" => "Anthropic",
        _ => null
    };

    private static bool HasCloudAiKey(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiApiKey) ||
        !string.IsNullOrWhiteSpace(settings.AnthropicApiKey);

    /// <summary>The name of a hosted web search provider that receives research-mode queries.</summary>
    private static string WebSearchProviderName(WebSearchProvider provider) => provider switch
    {
        WebSearchProvider.Serper => "Serper (Google Search)",
        _ => "Brave Search",
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
