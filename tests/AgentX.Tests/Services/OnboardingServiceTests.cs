using AgentX.App.Services;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Covers the onboarding session handoff (SH13): the wizard suppresses the rail while it
/// owns the shell, and leaving it by ANY route must hand the rail back and record the
/// wizard as done. Before the fix only Finish did that; a shortcut, the palette, Jump-To,
/// the tray menu or a lamp left SuppressNavigation set for the whole session.
/// </summary>
public sealed class OnboardingServiceTests
{
    private readonly FakeNavigationGate _gate = new();
    private readonly AppSettings _settings = new();
    private readonly Mock<ISettingsService> _settingsService = new();

    public OnboardingServiceTests()
    {
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(_settings);
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
    }

    private OnboardingService CreateSut() => new(_settingsService.Object, _gate);

    [Fact]
    public void BeginOnboarding_SuppressesTheRailAndMarksTheSessionActive()
    {
        var sut = CreateSut();

        sut.BeginOnboarding().Should().BeTrue();

        sut.IsOnboardingActive.Should().BeTrue();
        _gate.SuppressNavigation.Should().BeTrue();
    }

    [Fact]
    public async Task OnNavigatedAsync_AwayFromTheWizard_HandsTheRailBackAndMarksOnboardingDone()
    {
        var sut = CreateSut();
        sut.BeginOnboarding();

        await sut.OnNavigatedAsync(destinationIsOnboarding: false);

        sut.IsOnboardingActive.Should().BeFalse();
        _gate.SuppressNavigation.Should().BeFalse("a rail click after leaving the wizard must navigate");
        _gate.EnsureNavPaneVisibleCalls.Should().Be(1, "the wizard hid the pane, so leaving it must show it again");
        _settings.OnboardingCompleted.Should().BeTrue("leaving counts as a skip, so the wizard does not come back next launch");
        _settingsService.Verify(s => s.SaveSettingsAsync(_settings), Times.Once);
    }

    [Fact]
    public async Task OnNavigatedAsync_ToTheWizardItself_KeepsTheSessionActive()
    {
        var sut = CreateSut();
        sut.BeginOnboarding();

        await sut.OnNavigatedAsync(destinationIsOnboarding: true);

        sut.IsOnboardingActive.Should().BeTrue();
        _gate.SuppressNavigation.Should().BeTrue();
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task OnNavigatedAsync_WhenOnboardingIsNotActive_TouchesNothing()
    {
        var sut = CreateSut();

        await sut.OnNavigatedAsync(destinationIsOnboarding: false);

        _gate.SuppressNavigation.Should().BeFalse();
        _gate.EnsureNavPaneVisibleCalls.Should().Be(0);
        _settingsService.Verify(s => s.GetSettingsAsync(), Times.Never);
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task OnNavigatedAsync_OnlyTheFirstNavigationAwayEndsTheSession()
    {
        var sut = CreateSut();
        sut.BeginOnboarding();

        await sut.OnNavigatedAsync(destinationIsOnboarding: false);
        await sut.OnNavigatedAsync(destinationIsOnboarding: false);

        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Once);
        _gate.EnsureNavPaneVisibleCalls.Should().Be(1);
    }

    [Fact]
    public async Task OnNavigatedAsync_WhenSavingFails_StillHandsTheRailBackAndDoesNotThrow()
    {
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()))
            .ThrowsAsync(new IOException("settings file locked"));
        var sut = CreateSut();
        sut.BeginOnboarding();

        var act = () => sut.OnNavigatedAsync(destinationIsOnboarding: false);

        await act.Should().NotThrowAsync();
        _gate.SuppressNavigation.Should().BeFalse();
        sut.IsOnboardingActive.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteOnboardingAsync_HandsTheRailBackPersistsAndRaisesCompleted()
    {
        var sut = CreateSut();
        var completedRaised = 0;
        sut.OnboardingCompleted += () => completedRaised++;
        sut.BeginOnboarding();

        await sut.CompleteOnboardingAsync();

        sut.IsOnboardingActive.Should().BeFalse();
        _gate.SuppressNavigation.Should().BeFalse();
        _settings.OnboardingCompleted.Should().BeTrue();
        completedRaised.Should().Be(1);

        // Finish navigates to the Dashboard afterwards; that navigation must not run
        // the skip path a second time.
        await sut.OnNavigatedAsync(destinationIsOnboarding: false);
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Once);
    }

    [Fact]
    public async Task SkipOnboardingAsync_WhenTheWizardCannotBeShown_ReleasesTheRail()
    {
        var sut = CreateSut();
        sut.BeginOnboarding();

        await sut.SkipOnboardingAsync();

        sut.IsOnboardingActive.Should().BeFalse();
        _gate.SuppressNavigation.Should().BeFalse();
        _settings.OnboardingCompleted.Should().BeTrue();
    }

    private sealed class FakeNavigationGate : INavigationGate
    {
        public bool SuppressNavigation { get; set; }

        public int EnsureNavPaneVisibleCalls { get; private set; }

        public void EnsureNavPaneVisible() => EnsureNavPaneVisibleCalls++;
    }
}
