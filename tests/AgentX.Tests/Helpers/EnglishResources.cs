using System.Xml.Linq;
using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using Moq;

namespace AgentX.Tests.Helpers;

/// <summary>
/// A localization service serving the en-US resources the app ships, so a view-model test can
/// check the English a page shows and that each resource's placeholders format as intended.
/// </summary>
internal static class EnglishResources
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Values = new(Load);

    public static ILocalizationService Create() => new LocalizationService(
        Mock.Of<ISettingsService>(),
        Mock.Of<IPluralRuleProvider>(),
        new DictionaryResourceLoader(Values.Value));

    private static IReadOnlyDictionary<string, string> Load()
    {
        var resw = Path.Combine(ResolveSourceRoot(), "AgentX.App", "Strings", "en-US", "Resources.resw");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in XDocument.Load(resw).Root!.Elements("data"))
        {
            values[(string)data.Attribute("name")!] = (string?)data.Element("value") ?? string.Empty;
        }

        return values;
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

        throw new DirectoryNotFoundException("Could not locate Agent-X source root from test output directory.");
    }

    /// <summary>Serves resources from a dictionary, answering a missing one with null as MRT Core does.</summary>
    private sealed class DictionaryResourceLoader(IReadOnlyDictionary<string, string> values) : IResourceLoaderAdapter
    {
        public void SetLanguageOverride(string? languageCode)
        {
        }

        public string GetActiveLanguage() => "en-US";

        public void Initialize()
        {
        }

        public string? GetString(string key) => values.TryGetValue(key, out var value) ? value : null;
    }
}
