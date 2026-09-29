using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Localization;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The Hardware Advisor built its GPU and performance tier names, the fallbacks for values
/// Windows did not report, the advisory, the model descriptions and its errors in English code,
/// so the page stayed English in every language. They now come from the resources.
/// </summary>
public sealed class HardwareAdvisorViewModelTests
{
    private readonly Mock<IHardwareDetector> _detector = new();
    private readonly Mock<IModelManager> _modelManager = new();

    public HardwareAdvisorViewModelTests()
    {
        _modelManager.Setup(manager => manager.GetInstalledModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());
    }

    [Fact]
    public async Task InitializeAsync_TakesTheTierNamesAndTheAdviceFromTheResources()
    {
        Detect(new HardwareCapability
        {
            GpuName = "NVIDIA GeForce RTX 4070",
            GpuVramBytes = 12_000_000_000,
            CpuName = "AMD Ryzen 7 7700",
            CpuCores = 8,
            TotalRamBytes = 32_000_000_000,
            AvailableRamBytes = 20_000_000_000,
        });
        var viewModel = CreateViewModel(KeyEchoingLocalization());

        await viewModel.InitializeAsync();

        viewModel.GpuName.Should().Be("NVIDIA GeForce RTX 4070");
        viewModel.GpuTier.Should().Be("[HwAdvisor_GpuTierPerformance]");
        viewModel.PerformanceTier.Should().Be("[HwAdvisor_PerfTierPerformance]");
        viewModel.RecommendedModelSize.Should().Be("[HwAdvisor_UpToModelSize] 34B");
        viewModel.AdvisoryMessage.Should().StartWith("[HwAdvisor_AdviceGpu] NVIDIA GeForce RTX 4070, ")
            .And.Contain("[HwAdvisor_AdviceUnder16]");
        viewModel.RecommendedModels.Should().NotBeEmpty()
            .And.OnlyContain(model => model.Description.StartsWith("[HwAdvisor_Model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_WithoutAGpu_SaysSoFromTheResources()
    {
        Detect(new HardwareCapability
        {
            GpuName = "Intel(R) UHD Graphics",
            GpuVramBytes = 0,
            HasNpu = true,
            NpuName = "Intel(R) AI Boost",
            CpuName = "Intel(R) Core(TM) Ultra 7",
            CpuCores = 16,
            TotalRamBytes = 16_000_000_000,
            AvailableRamBytes = 6_000_000_000,
        });
        var viewModel = CreateViewModel(KeyEchoingLocalization());

        await viewModel.InitializeAsync();

        viewModel.GpuVram.Should().Be("[HwAdvisor_NoDedicatedGpu]");
        viewModel.GpuTier.Should().Be("[HwAdvisor_NoDedicatedGpu]");
        viewModel.PerformanceTier.Should().Be("[HwAdvisor_PerfTierStandard]");
        viewModel.NpuName.Should().Be("Intel(R) AI Boost");
        viewModel.AdvisoryMessage.Should().Be(
            "[HwAdvisor_AdviceNoGpu] [HwAdvisor_AdviceUnder8] [HwAdvisor_AdviceNpu] Intel(R) AI Boost");
    }

    [Fact]
    public async Task InitializeAsync_NamesWhatWindowsDidNotReportFromTheResources()
    {
        Detect(new HardwareCapability
        {
            GpuName = "Microsoft Basic Display Adapter",
            CpuName = "",
            TotalRamBytes = 0,
        });
        var viewModel = CreateViewModel(KeyEchoingLocalization());

        await viewModel.InitializeAsync();

        viewModel.GpuName.Should().Be("[HwAdvisor_GpuNotDetected]");
        viewModel.CpuName.Should().Be("[HwAdvisor_CpuNotDetected]");
        viewModel.TotalRam.Should().Be("[HwAdvisor_RamNotDetected]");
        viewModel.AvailableRam.Should().Be("[HwAdvisor_RamNotDetected]");
        viewModel.IsDetectionIncomplete.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WhenDetectionFails_FillsTheFallbacksFromTheResources()
    {
        _detector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WMI is unavailable."));
        var viewModel = CreateViewModel(KeyEchoingLocalization());

        await viewModel.InitializeAsync();

        viewModel.ErrorMessage.Should().Be("[HwAdvisor_DetectionFailedError]");
        viewModel.GpuName.Should().Be("[HwAdvisor_DetectionFailed]");
        viewModel.GpuVram.Should().Be("[HwAdvisor_Unknown]");
        viewModel.GpuTier.Should().Be("[HwAdvisor_Unknown]");
        viewModel.CpuName.Should().Be($"[HwAdvisor_CpuCoreCount] {Environment.ProcessorCount}");
        viewModel.TotalRam.Should().Be("[HwAdvisor_Unknown]");
        viewModel.RecommendedModelSize.Should().Be("[HwAdvisor_UnableToDetermine]");
        viewModel.AdvisoryMessage.Should().Be("[HwAdvisor_AdviceDetectionFailed]");
        viewModel.PerformanceTier.Should().Be("[HwAdvisor_Unknown]");
    }

    [Fact]
    public async Task PullRecommendedModelCommand_WhenTheDownloadFails_ReportsItFromTheResources()
    {
        _modelManager.Setup(manager => manager.PullModelAsync(
                "phi3:mini", It.IsAny<IProgress<ModelDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Ollama is not running."));
        var viewModel = CreateViewModel(KeyEchoingLocalization());

        await viewModel.PullRecommendedModelCommand.ExecuteAsync("phi3:mini");

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Be("[HwAdvisor_InstallFailed] phi3:mini");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(40)]
    public async Task InitializeAsync_WithTheEnglishResources_ShowsNoBareResourceKey(int gigabytes)
    {
        // A key the resources lack comes back as the key itself.
        Detect(new HardwareCapability
        {
            GpuName = "Test GPU",
            GpuVramBytes = gigabytes * 1_000_000_000L,
            HasNpu = true,
            NpuName = "Test NPU",
            CpuName = "Test CPU",
            CpuCores = 8,
            TotalRamBytes = 64_000_000_000,
            AvailableRamBytes = gigabytes * 1_000_000_000L,
        });
        var viewModel = CreateViewModel(EnglishResourceLocalization.Create());

        await viewModel.InitializeAsync();

        var shown = new[]
            {
                viewModel.GpuTier, viewModel.GpuVram, viewModel.PerformanceTier,
                viewModel.RecommendedModelSize, viewModel.AdvisoryMessage,
            }
            .Concat(viewModel.RecommendedModels.Select(model => model.Description))
            .ToList();
        shown.Should().OnlyContain(text => text.Length > 0 && !text.Contains("HwAdvisor_"));
    }

    [Fact]
    public async Task InitializeAsync_WithTheEnglishResources_ShowsTheEnglishTierNames()
    {
        Detect(new HardwareCapability
        {
            GpuName = "NVIDIA GeForce RTX 4070",
            GpuVramBytes = 12_000_000_000,
            CpuName = "AMD Ryzen 7 7700",
            TotalRamBytes = 32_000_000_000,
            AvailableRamBytes = 20_000_000_000,
        });
        var viewModel = CreateViewModel(EnglishResourceLocalization.Create());

        await viewModel.InitializeAsync();

        viewModel.GpuTier.Should().Be("Performance");
        viewModel.PerformanceTier.Should().Be("Performance");
        viewModel.RecommendedModelSize.Should().Be("Up to 34B parameter models");
    }

    private void Detect(HardwareCapability capability) =>
        _detector.Setup(detector => detector.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(capability);

    private HardwareAdvisorViewModel CreateViewModel(ILocalizationService localization) =>
        new(_detector.Object, _modelManager.Object, localization);

    /// <summary>Returns "[key]" for a text and "[key] arg1, arg2" for a formatted one.</summary>
    private static ILocalizationService KeyEchoingLocalization()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => $"[{key}]");
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"[{key}] {string.Join(", ", args)}");
        return localization.Object;
    }
}
