using System.Xml.Linq;
using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using Moq;

namespace AgentX.Tests.Helpers;

/// <summary>
/// A real <see cref="LocalizationService"/> serving the strings the app ships for one locale, so a
/// test asserts on the text a user reads and fails when a key is missing from the resources.
/// </summary>
internal static class ReswLocalization
{
    /// <summary>The service for <paramref name="locale"/>, read from src/AgentX.App/Strings.</summary>
    public static ILocalizationService For(string locale = "en-US")
    {
        var resw = Path.Combine(ResolveSourceRoot(), "AgentX.App", "Strings", locale, "Resources.resw");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in XDocument.Load(resw).Root!.Elements("data"))
        {
            values[(string)data.Attribute("name")!] = (string?)data.Element("value") ?? string.Empty;
        }

        return new LocalizationService(
            Mock.Of<ISettingsService>(),
            Mock.Of<IPluralRuleProvider>(),
            new DictionaryResourceLoader(values));
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

    /// <summary>Serves resources from a dictionary, answering a missing one with null as MRT Core does.</summary>
    private sealed class DictionaryResourceLoader : IResourceLoaderAdapter
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public DictionaryResourceLoader(IReadOnlyDictionary<string, string> values) => _values = values;

        public void SetLanguageOverride(string? languageCode)
        {
        }

        public string GetActiveLanguage() => "en-US";

        public void Initialize()
        {
        }

        public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;
    }
}
