using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// SH21: code-built UI (the command palette's ACTIONS / ON THIS PAGE headers and action
/// labels) showed raw resource keys whenever the resource loader was never built. These
/// tests pin the initialization order and make sure a failure while reading or applying
/// the language preference no longer skips building the loader.
/// </summary>
public sealed class LocalizationServiceInitializationTests
{
    [Fact]
    public async Task InitializeAsync_WhenSettingsCannotBeRead_StillBuildsTheLoader()
    {
        var loader = new RecordingResourceLoader(("Palette_Actions", "ACTIONS"));
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ThrowsAsync(new IOException("settings unavailable"));
        var sut = new LocalizationService(settings.Object, new CldrPluralRuleProvider(), loader);

        await sut.InitializeAsync();

        loader.Calls.Should().Contain("Initialize");
        sut.GetString("Palette_Actions").Should().Be("ACTIONS", "the palette must not fall back to its raw key");
        sut.CurrentLanguage.Should().Be("en-US");
    }

    [Fact]
    public async Task InitializeAsync_WhenTheOverrideCannotBeApplied_StillBuildsTheLoader()
    {
        var loader = new RecordingResourceLoader(("Palette_Actions", "AKTIONEN")) { ThrowOnOverride = true };
        var sut = new LocalizationService(SettingsWithOverride("de"), new CldrPluralRuleProvider(), loader);

        await sut.InitializeAsync();

        loader.Calls.Should().Contain("Initialize");
        sut.GetString("Palette_Actions").Should().Be("AKTIONEN");
    }

    [Fact]
    public async Task InitializeAsync_AppliesThePersistedOverrideBeforeBuildingTheLoader()
    {
        var loader = new RecordingResourceLoader();
        var sut = new LocalizationService(SettingsWithOverride("ja"), new CldrPluralRuleProvider(), loader);

        await sut.InitializeAsync();

        loader.Calls.Should().Equal("SetLanguageOverride:ja", "Initialize");
        sut.CurrentLanguage.Should().Be("ja");
    }

    [Fact]
    public async Task InitializeAsync_WithoutAnOverride_ReportsTheActiveLanguage()
    {
        var loader = new RecordingResourceLoader { ActiveLanguage = "fr-FR" };
        var sut = new LocalizationService(SettingsWithOverride(null), new CldrPluralRuleProvider(), loader);

        await sut.InitializeAsync();

        loader.Calls.Should().Equal("GetActiveLanguage", "Initialize");
        sut.CurrentLanguage.Should().Be("fr-FR");
    }

    [Fact]
    public async Task InitializeAsync_WhenTheLoaderItselfFails_DoesNotThrowAndFallsBackToKeys()
    {
        var loader = new RecordingResourceLoader { ThrowOnInitialize = true };
        var sut = new LocalizationService(SettingsWithOverride(null), new CldrPluralRuleProvider(), loader);

        var act = () => sut.InitializeAsync();

        await act.Should().NotThrowAsync();
        sut.GetString("Palette_Actions").Should().Be("Palette_Actions");
    }

    [Fact]
    public async Task SetLanguageAsync_SavesTheChoiceSoTheNextLaunchAppliesIt()
    {
        // The choice used to be written only when the settings object was an
        // AppSettingsExtended, which the settings service never returns.
        var saved = new AppSettings();
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(saved);
        var loader = new RecordingResourceLoader();
        var sut = new LocalizationService(settings.Object, new CldrPluralRuleProvider(), loader);

        await sut.SetLanguageAsync("de");

        settings.Verify(s => s.SaveSettingsAsync(It.Is<AppSettings>(a => a.LanguageOverride == "de")), Times.Once);
        sut.CurrentLanguage.Should().Be("de");

        var nextLaunch = new LocalizationService(SettingsWithOverride(saved.LanguageOverride), new CldrPluralRuleProvider(), new RecordingResourceLoader());
        await nextLaunch.InitializeAsync();
        nextLaunch.CurrentLanguage.Should().Be("de");
    }

    [Fact]
    public async Task SetLanguageAsync_WithNoLanguage_ClearsTheSavedChoice()
    {
        var saved = new AppSettings { LanguageOverride = "ja" };
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(saved);
        var sut = new LocalizationService(settings.Object, new CldrPluralRuleProvider(), new RecordingResourceLoader());

        await sut.SetLanguageAsync(null);

        saved.LanguageOverride.Should().BeNull();
        settings.Verify(s => s.SaveSettingsAsync(saved), Times.Once);
    }

    private static ISettingsService SettingsWithOverride(string? languageOverride)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { LanguageOverride = languageOverride });
        return settings.Object;
    }

    /// <summary>
    /// Fake adapter that records the order of lifecycle calls and, like the production
    /// adapter, serves strings only after Initialize and returns null on a miss.
    /// </summary>
    private sealed class RecordingResourceLoader : IResourceLoaderAdapter
    {
        private readonly Dictionary<string, string> _strings;
        private bool _initialized;

        public RecordingResourceLoader(params (string Key, string Value)[] strings) =>
            _strings = strings.ToDictionary(s => s.Key, s => s.Value);

        public List<string> Calls { get; } = new();
        public string ActiveLanguage { get; init; } = "en-US";
        public bool ThrowOnOverride { get; init; }
        public bool ThrowOnInitialize { get; init; }

        public void SetLanguageOverride(string? languageCode)
        {
            Calls.Add($"SetLanguageOverride:{languageCode}");
            if (ThrowOnOverride)
            {
                throw new InvalidOperationException("override rejected");
            }
        }

        public string GetActiveLanguage()
        {
            Calls.Add("GetActiveLanguage");
            return ActiveLanguage;
        }

        public void Initialize()
        {
            Calls.Add("Initialize");
            if (ThrowOnInitialize)
            {
                throw new InvalidOperationException("resources.pri missing");
            }

            _initialized = true;
        }

        public string? GetString(string key) =>
            _initialized && _strings.TryGetValue(key, out var value) ? value : null;
    }
}
