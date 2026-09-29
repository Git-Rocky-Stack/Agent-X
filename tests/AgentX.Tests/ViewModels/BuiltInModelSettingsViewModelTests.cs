using AgentX.App.ViewModels;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The built-in model's GPU offload (AppSettings.LocalGpuLayers) had no control in the app; the
/// guide sent users to settings.json. The Settings page now offers Automatic or a number of
/// layers, where 0 keeps the model on the CPU. The saved value 0 already meant Automatic, so a
/// chosen 0 is saved as -1.
/// </summary>
public sealed class BuiltInModelSettingsViewModelTests
{
    [Fact]
    public void Load_TheDefault_IsAutomatic()
    {
        var viewModel = new BuiltInModelSettingsViewModel();

        viewModel.Load(new AppSettings());

        viewModel.IsGpuLayersAutomatic.Should().BeTrue();
    }

    [Theory]
    [InlineData(28, 28)]
    [InlineData(-1, 0)]
    [InlineData(-5, 0)]
    [InlineData(5000, BuiltInModelSettingsViewModel.MaximumGpuLayers)]
    public void Load_ASavedCount_ShowsItAsAFixedNumber(int saved, int shown)
    {
        var viewModel = new BuiltInModelSettingsViewModel();

        viewModel.Load(new AppSettings { LocalGpuLayers = saved });

        viewModel.IsGpuLayersAutomatic.Should().BeFalse();
        viewModel.GpuLayers.Should().Be(shown);
    }

    [Theory]
    [InlineData(true, 20, 0)]
    [InlineData(false, 20, 20)]
    [InlineData(false, 0, -1)]
    [InlineData(false, -3, -1)]
    [InlineData(false, 100000, BuiltInModelSettingsViewModel.MaximumGpuLayers)]
    public void ApplyTo_SavesAutomaticAsZero_AFixedCountAsIs_AndCpuOnlyAsMinusOne(
        bool automatic, int layers, int saved)
    {
        var viewModel = new BuiltInModelSettingsViewModel { IsGpuLayersAutomatic = automatic, GpuLayers = layers };
        var settings = new AppSettings { LocalGpuLayers = 7 };

        viewModel.ApplyTo(settings);

        settings.LocalGpuLayers.Should().Be(saved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    [InlineData(-1)]
    public void LoadThenApply_KeepsTheSavedValue(int saved)
    {
        var viewModel = new BuiltInModelSettingsViewModel();
        var settings = new AppSettings { LocalGpuLayers = saved };

        viewModel.Load(settings);
        viewModel.ApplyTo(settings);

        settings.LocalGpuLayers.Should().Be(saved, "opening and saving the page must not change the setting");
    }

    [Fact]
    public void Reset_GoesBackToAutomatic()
    {
        var viewModel = new BuiltInModelSettingsViewModel { IsGpuLayersAutomatic = false, GpuLayers = 12 };
        var settings = new AppSettings();

        viewModel.Reset();
        viewModel.ApplyTo(settings);

        viewModel.IsGpuLayersAutomatic.Should().BeTrue();
        settings.LocalGpuLayers.Should().Be(0);
    }
}
