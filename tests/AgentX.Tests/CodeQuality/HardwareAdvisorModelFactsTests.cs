using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the model facts the Hardware Advisor shows. The advisor told users that
/// <c>llama3.2:latest</c> was an 8B, 4.7 GB model (the tag is the 3B, 2.0 GB model) and
/// recommended <c>phi3:medium</c>, a 7.9 GB download, to machines with 4-8 GB of memory.
/// The recommendations are read from the view model itself, with the English descriptions
/// the app ships, for every memory tier.
/// </summary>
public sealed class HardwareAdvisorModelFactsTests
{
    /// <summary>One video memory size in each of the advisor's tiers, in GB.</summary>
    private static readonly int[] TierSizes = { 2, 6, 12, 24 };

    [Fact]
    public async Task Llama_3_2_tags_are_described_as_the_3B_model()
    {
        var entries = new List<RecommendedModel>();
        foreach (var gigabytes in TierSizes)
        {
            entries.AddRange(await RecommendAsync(gigabytes));
        }

        var llama32 = entries.Where(e => e.Name.StartsWith("llama3.2", StringComparison.Ordinal)).ToList();
        llama32.Should().NotBeEmpty();
        llama32.Should().OnlyContain(e => e.Size == "2.0 GB" && e.Description.Contains("(3B)"),
            "llama3.2 and llama3.2:latest are the 3B model in the Ollama library");
    }

    [Fact]
    public async Task Phi3_medium_is_not_recommended_below_8_GB()
    {
        (await RecommendAsync(6)).Should().NotContain(e => e.Name == "phi3:medium");

        var entries = new List<RecommendedModel>();
        foreach (var gigabytes in TierSizes)
        {
            entries.AddRange(await RecommendAsync(gigabytes));
        }

        entries.Where(e => e.Name == "phi3:medium").Should().NotBeEmpty()
            .And.OnlyContain(e => e.Size == "7.9 GB");
    }

    /// <summary>The models the advisor recommends for a GPU with this much video memory.</summary>
    private static async Task<IReadOnlyList<RecommendedModel>> RecommendAsync(int gigabytes)
    {
        var detector = new Mock<IHardwareDetector>();
        detector.Setup(d => d.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HardwareCapability
        {
            GpuName = "Test GPU",
            GpuVramBytes = gigabytes * 1_000_000_000L,
            CpuName = "Test CPU",
            CpuCores = 8,
            TotalRamBytes = 64_000_000_000,
            AvailableRamBytes = 48_000_000_000,
        });
        var modelManager = new Mock<IModelManager>();
        modelManager.Setup(m => m.GetInstalledModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());

        var viewModel = new HardwareAdvisorViewModel(
            detector.Object, modelManager.Object, EnglishResourceLocalization.Create());
        await viewModel.InitializeAsync();
        return viewModel.RecommendedModels.ToList();
    }
}
