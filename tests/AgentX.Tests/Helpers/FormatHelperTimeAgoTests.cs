using System.Globalization;
using System.Xml.Linq;
using AgentX.App.Services;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Helpers;

/// <summary>
/// FormatHelper.TimeAgo and TimeAgoWithMonths said "5m ago" and "just now" in every language,
/// on every page that shows when something happened. They now take their wording from the app's
/// resources, which the app hands them at startup, and keep the English exactly as it was.
/// </summary>
public sealed class FormatHelperTimeAgoTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Locales = ["en-US", "de", "es", "fr", "ja", "zh-CN"];

    private static readonly string[] UnitKeys =
        ["TimeAgo_Minute", "TimeAgo_Hour", "TimeAgo_Day", "TimeAgo_Week", "TimeAgo_Month"];

    /// <summary>Minutes before now, across every boundary of both helpers.</summary>
    public static TheoryData<double> Spans => new()
    {
        -5, 0, 0.5, 0.99, 1, 1.5, 2, 59, 59.99, 60, 61, 119, 120, 1439, 1440, 2879, 2880,
        10079, 10080, 20159, 20160, 43199, 43200, 44640, 86399, 86400, 525599, 525600, 1_500_000,
    };

    // --- English stays byte-identical ---

    [Theory]
    [MemberData(nameof(Spans))]
    public void TimeAgo_InEnglish_IsWhatItAlwaysWas(double minutesAgo)
    {
        var date = Now.AddMinutes(-minutesAgo);

        FormatHelper.TimeAgo(date, Now, localizedText: null).Should().Be(PreviousTimeAgo(date, Now));
        FormatHelper.TimeAgo(date, Now, EnglishResources.Create().GetString).Should().Be(PreviousTimeAgo(date, Now));
    }

    [Theory]
    [MemberData(nameof(Spans))]
    public void TimeAgoWithMonths_InEnglish_IsWhatItAlwaysWas(double minutesAgo)
    {
        var date = Now.AddMinutes(-minutesAgo);

        FormatHelper.TimeAgoWithMonths(date, Now, localizedText: null)
            .Should().Be(PreviousTimeAgoWithMonths(date, Now));
        FormatHelper.TimeAgoWithMonths(date, Now, EnglishResources.Create().GetString)
            .Should().Be(PreviousTimeAgoWithMonths(date, Now));
    }

    [Fact]
    public void TimeAgo_InEnglish_ReadsAsBefore()
    {
        var english = EnglishResources.Create();

        Ago(minutes: 0.2, english).Should().Be("just now");
        Ago(minutes: 1, english).Should().Be("1m ago");
        Ago(minutes: 5, english).Should().Be("5m ago");
        Ago(minutes: 60, english).Should().Be("1h ago");
        Ago(minutes: 3 * 60, english).Should().Be("3h ago");
        Ago(minutes: 2 * 1440, english).Should().Be("2d ago");
        WithMonths(days: 14, english).Should().Be("2w ago");
        WithMonths(days: 60, english).Should().Be("2mo ago");
    }

    // --- Another language, through the app's localization service ---

    [Fact]
    public void TimeAgo_InGerman_ReadsInGerman()
    {
        var german = Resources("de");

        Ago(minutes: 0.2, german).Should().Be("gerade eben");
        Ago(minutes: 1, german).Should().Be("vor 1 Min.");
        Ago(minutes: 5, german).Should().Be("vor 5 Min.");
        Ago(minutes: 60, german).Should().Be("vor 1 Std.");
        Ago(minutes: 3 * 60, german).Should().Be("vor 3 Std.");
        Ago(minutes: 1440, german).Should().Be("vor 1 Tag");
        Ago(minutes: 3 * 1440, german).Should().Be("vor 3 Tagen");
    }

    [Fact]
    public void TimeAgoWithMonths_InGerman_CountsWeeksAndMonthsInGerman()
    {
        var german = Resources("de");

        WithMonths(days: 7, german).Should().Be("vor 1 Woche");
        WithMonths(days: 15, german).Should().Be("vor 2 Wochen");
        WithMonths(days: 31, german).Should().Be("vor 1 Monat");
        WithMonths(days: 95, german).Should().Be("vor 3 Monaten");
    }

    [Fact]
    public void TimeAgo_PastItsRange_WritesTheDateInTheLanguagesOrder()
    {
        var date = new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc);

        FormatHelper.TimeAgo(date, Now, Resources("de").GetString)
            .Should().Be(date.ToString("d. MMM yyyy", CultureInfo.CurrentCulture));
        FormatHelper.TimeAgo(date, Now, Resources("ja").GetString)
            .Should().Be("2026\u5E747\u67085\u65E5"); // 2026 nen 7 gatsu 5 nichi
        FormatHelper.TimeAgo(date, Now, EnglishResources.Create().GetString)
            .Should().Be(date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void TimeAgo_WordsOneAndManySeparately()
    {
        string? Lookup(string key) => key switch
        {
            "TimeAgo_MinuteAgo" => "one minute back",
            "TimeAgo_MinutesAgo" => "{0} minutes back",
            _ => null,
        };

        FormatHelper.TimeAgo(Now.AddMinutes(-1), Now, Lookup).Should().Be("one minute back");
        FormatHelper.TimeAgo(Now.AddMinutes(-2), Now, Lookup).Should().Be("2 minutes back");
    }

    // --- What a lookup cannot answer reads in English ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TimeAgo_MinutesAgo")]
    public void TimeAgo_WithoutAResource_FallsBackToEnglish(string? answer)
    {
        FormatHelper.TimeAgo(Now.AddMinutes(-5), Now, _ => answer).Should().Be("5m ago");
    }

    [Fact]
    public void TimeAgo_WithAMalformedTranslation_FallsBackToEnglish()
    {
        FormatHelper.TimeAgo(Now.AddMinutes(-5), Now, key => key == "TimeAgo_MinutesAgo" ? "vor {0 Min." : null)
            .Should().Be("5m ago");
        FormatHelper.TimeAgo(Now.AddDays(-60), Now, key => key == "TimeAgo_DateFormat" ? "%" : null)
            .Should().Be(Now.AddDays(-60).ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
    }

    // --- The resources every language ships ---

    [Fact]
    public void EveryLanguage_WordsEveryUnitInTheSingularAndThePlural()
    {
        var date = new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc);

        foreach (var locale in Locales)
        {
            var values = ReswValues(locale);
            values.Should().ContainKey("TimeAgo_JustNow", locale).And.ContainKey("TimeAgo_DateFormat", locale);
            date.ToString(values["TimeAgo_DateFormat"], CultureInfo.CurrentCulture).Should().Contain("2026", locale);

            foreach (var unit in UnitKeys)
            {
                var one = string.Format(CultureInfo.CurrentCulture, values[unit + "Ago"], 1);
                var many = string.Format(CultureInfo.CurrentCulture, values[unit + "sAgo"], 7);

                one.Should().Contain("1", $"{locale} {unit}Ago").And.NotContain("{");
                many.Should().Contain("7", $"{locale} {unit}sAgo").And.NotContain("{");
            }
        }
    }

    private static string Ago(double minutes, ILocalizationService localization) =>
        FormatHelper.TimeAgo(Now.AddMinutes(-minutes), Now, localization.GetString);

    private static string WithMonths(double days, ILocalizationService localization) =>
        FormatHelper.TimeAgoWithMonths(Now.AddDays(-days), Now, localization.GetString);

    /// <summary>TimeAgo as it was before it was localized.</summary>
    private static string PreviousTimeAgo(DateTime dateTime, DateTime now)
    {
        var span = now - dateTime;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 43200 => $"{(int)span.TotalDays}d ago",
            _ => dateTime.ToString("MMM d, yyyy")
        };
    }

    /// <summary>TimeAgoWithMonths as it was before it was localized.</summary>
    private static string PreviousTimeAgoWithMonths(DateTime dateTime, DateTime now)
    {
        var span = now - dateTime;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 10080 => $"{(int)span.TotalDays}d ago",
            < 43200 => $"{(int)(span.TotalDays / 7)}w ago",
            < 525600 => $"{(int)(span.TotalDays / 30)}mo ago",
            _ => dateTime.ToString("MMM d, yyyy")
        };
    }

    /// <summary>
    /// The app's localization service over one language's shipped resources, answering a missing
    /// resource with its key as the app does.
    /// </summary>
    internal static ILocalizationService Resources(string locale) => new LocalizationService(
        Mock.Of<ISettingsService>(),
        Mock.Of<IPluralRuleProvider>(),
        new DictionaryResourceLoader(ReswValues(locale)));

    private static Dictionary<string, string> ReswValues(string locale)
    {
        var resw = Path.Combine(SourceRoot(), "AgentX.App", "Strings", locale, "Resources.resw");
        return XDocument.Load(resw).Root!.Elements("data").ToDictionary(
            data => (string)data.Attribute("name")!,
            data => (string?)data.Element("value") ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Agent-X source root from the test output directory.");
    }

    private sealed class DictionaryResourceLoader(IReadOnlyDictionary<string, string> values) : IResourceLoaderAdapter
    {
        public void SetLanguageOverride(string? languageCode)
        {
        }

        public string GetActiveLanguage() => "de";

        public void Initialize()
        {
        }

        public string? GetString(string key) => values.TryGetValue(key, out var value) ? value : null;
    }
}

[CollectionDefinition(nameof(FormatHelperLocalizedTextCollection), DisableParallelization = true)]
public sealed class FormatHelperLocalizedTextCollection
{
}

/// <summary>
/// The app sets FormatHelper.LocalizedText once at startup. This changes shared state, so it runs
/// on its own, after the tests that run in parallel.
/// </summary>
[Collection(nameof(FormatHelperLocalizedTextCollection))]
public sealed class FormatHelperLocalizedTextTests
{
    [Fact]
    public void TimeAgo_AndTimeAgoWithMonths_UseTheWordingTheAppSetsAtStartup()
    {
        var previous = FormatHelper.LocalizedText;
        FormatHelper.LocalizedText = FormatHelperTimeAgoTests.Resources("de").GetString;
        try
        {
            FormatHelper.TimeAgo(DateTime.UtcNow.AddMinutes(-5.5)).Should().Be("vor 5 Min.");
            FormatHelper.TimeAgoWithMonths(DateTime.UtcNow.AddDays(-15)).Should().Be("vor 2 Wochen");
        }
        finally
        {
            FormatHelper.LocalizedText = previous;
        }

        FormatHelper.TimeAgo(DateTime.UtcNow.AddMinutes(-5.5)).Should().Be("5m ago");
    }
}
