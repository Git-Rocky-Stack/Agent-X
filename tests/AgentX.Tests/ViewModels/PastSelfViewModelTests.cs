using AgentX.App.ViewModels;
using AgentX.Core.Services.TemporalIdentity;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class PastSelfViewModelTests
{
    private readonly Mock<ITemporalIdentityService> _temporalIdentity = new();

    // ── Voice profile ────────────────────────────────────────────────────────
    // With no captured samples the panel used to show an invented 15-word average and a
    // "Balanced" style, which reads as a measurement of the user's writing rather than
    // the absence of one.

    [Fact]
    public async Task LoadVoiceProfileAsync_WithNoSamples_ReportsNoMeasurementRatherThanInventedOnes()
    {
        _temporalIdentity
            .Setup(service => service.GetVoiceProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentX.Core.Services.TemporalIdentity.Models.VoiceProfileEntity?)null);

        var viewModel = new PastSelfViewModel(_temporalIdentity.Object);

        await viewModel.LoadVoiceProfileCommand.ExecuteAsync(null);

        viewModel.VoiceProfile.Should().NotBeNull();
        viewModel.VoiceProfile!.SampleCount.Should().Be(0);
        viewModel.VoiceProfile.AvgSentenceLength.Should().Be(0);
        viewModel.VoiceProfile.FormalityLabel.Should().Be("Not enough data");
    }

    [Fact]
    public void FormalityLabel_WithSamples_DescribesTheMeasuredStyle()
    {
        new VoiceProfileDisplay { SampleCount = 40, FormalityScore = 0.1 }
            .FormalityLabel.Should().Be("Casual");
        new VoiceProfileDisplay { SampleCount = 40, FormalityScore = 0.5 }
            .FormalityLabel.Should().Be("Balanced");
        new VoiceProfileDisplay { SampleCount = 40, FormalityScore = 0.9 }
            .FormalityLabel.Should().Be("Formal");
    }
}
