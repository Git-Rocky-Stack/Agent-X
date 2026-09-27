using System.Globalization;
using AgentX.Core.Services.Localization;

namespace AgentX.App.Services;

/// <summary>
/// Status and advice text that names the active AI provider, shared by the status strip, the tray
/// icon, the dashboard and chat so they say the same thing, in the user's language. Without a
/// localization service (unit tests, early startup), or when a resource is missing, the English
/// text is used.
/// </summary>
public static class ProviderStatusText
{
    /// <summary>What the provider is called when it has no display name.</summary>
    public static string GenericName(ILocalizationService? localization) =>
        Resolve(localization?.GetString("Provider_GenericName"), "Provider_GenericName", "AI provider");

    /// <summary>The provider is reachable but no model is named.</summary>
    public static string ConnectedTo(ILocalizationService? localization, string providerName) =>
        Resolve(
            localization?.GetString("Provider_ConnectedTo", providerName),
            "Provider_ConnectedTo",
            "Connected to {0}",
            providerName);

    /// <summary>The provider is reachable with <paramref name="modelId"/> active.</summary>
    public static string ConnectedToModel(ILocalizationService? localization, string modelId) =>
        Resolve(
            localization?.GetString("Provider_ConnectedModel", modelId),
            "Provider_ConnectedModel",
            "Connected: {0}",
            modelId);

    /// <summary>The provider cannot be reached.</summary>
    public static string NotAvailable(ILocalizationService? localization, string providerName) =>
        Resolve(
            localization?.GetString("Provider_NotAvailable", providerName),
            "Provider_NotAvailable",
            "{0} not available",
            providerName);

    /// <summary>
    /// What to check when the provider cannot answer: the built-in model and free memory, Ollama and
    /// its address, or the cloud provider's API key and the network.
    /// </summary>
    public static string CheckHint(ILocalizationService? localization, string? providerId, string? providerName)
    {
        var id = providerId?.Trim().ToLowerInvariant();
        switch (id)
        {
            case "local":
                return Resolve(
                    localization?.GetString("Provider_CheckLocal"),
                    "Provider_CheckLocal",
                    "Check that the built-in model is installed and that there is enough free memory to load it.");

            case "ollama":
                return Resolve(
                    localization?.GetString("Provider_CheckOllama"),
                    "Provider_CheckOllama",
                    "Check that Ollama is running with a model downloaded, and that its address in Settings is correct.");

            case "openai":
            case "anthropic":
                var name = string.IsNullOrWhiteSpace(providerName) ? id : providerName;
                return Resolve(
                    localization?.GetString("Provider_CheckCloud", name),
                    "Provider_CheckCloud",
                    "Check the {0} API key in Settings and your network connection.",
                    name);

            default:
                return Resolve(
                    localization?.GetString("Provider_CheckGeneric"),
                    "Provider_CheckGeneric",
                    "Check the AI provider in Settings.");
        }
    }

    /// <summary>
    /// The tray icon's tooltip, for example "Agent-X | Connected | llama3.2 | 42 docs". The model is
    /// named only while connected, and the document count only when there are documents.
    /// </summary>
    public static string TrayTooltip(ILocalizationService? localization, bool isConnected, string? modelName, long documentCount)
    {
        var parts = new List<string>
        {
            "Agent-X",
            isConnected
                ? Resolve(localization?.GetString("Provider_TrayConnected"), "Provider_TrayConnected", "Connected")
                : Resolve(localization?.GetString("Provider_TrayDisconnected"), "Provider_TrayDisconnected", "Disconnected")
        };

        if (isConnected && !string.IsNullOrEmpty(modelName))
        {
            parts.Add(modelName);
        }

        if (documentCount > 0)
        {
            parts.Add(Resolve(
                localization?.GetString("Provider_TrayDocuments", documentCount),
                "Provider_TrayDocuments",
                "{0} docs",
                documentCount));
        }

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Returns <paramref name="localized"/>, the text the localization service found, unless there
    /// was no service or it found no resource (it then answers with the key itself); in that case
    /// returns <paramref name="english"/>, formatted with <paramref name="args"/>.
    /// </summary>
    internal static string Resolve(string? localized, string key, string english, params object[] args)
    {
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
        {
            return localized;
        }

        return args.Length == 0 ? english : string.Format(CultureInfo.CurrentCulture, english, args);
    }
}
