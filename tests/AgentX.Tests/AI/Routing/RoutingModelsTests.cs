using AgentX.Core.AI.Routing;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI.Routing;

public class RoutingModelsTests
{
    // ── TaskType Tests ──────────────────────────────────────────────

    [Fact]
    public void TaskType_Extraction_PreferLocalAndSpeed()
    {
        TaskType.Extraction.Name.Should().Be("extraction");
        TaskType.Extraction.PreferLocal.Should().BeTrue();
        TaskType.Extraction.PreferSpeed.Should().BeTrue();
        TaskType.Extraction.PreferQuality.Should().BeFalse();
    }

    [Fact]
    public void TaskType_Chat_PreferLocalAndSpeed()
    {
        TaskType.Chat.Name.Should().Be("chat");
        TaskType.Chat.PreferLocal.Should().BeTrue();
        TaskType.Chat.PreferSpeed.Should().BeTrue();
        TaskType.Chat.PreferQuality.Should().BeFalse();
    }

    [Fact]
    public void TaskType_Embedding_PreferLocalAndSpeed()
    {
        TaskType.Embedding.Name.Should().Be("embedding");
        TaskType.Embedding.PreferLocal.Should().BeTrue();
        TaskType.Embedding.PreferSpeed.Should().BeTrue();
        TaskType.Embedding.PreferQuality.Should().BeFalse();
    }

    [Fact]
    public void TaskType_Summarization_PreferLocalAndSpeed()
    {
        TaskType.Summarization.Name.Should().Be("summarization");
        TaskType.Summarization.PreferLocal.Should().BeTrue();
        TaskType.Summarization.PreferSpeed.Should().BeTrue();
        TaskType.Summarization.PreferQuality.Should().BeFalse();
    }

    [Fact]
    public void TaskType_Analysis_PreferQuality()
    {
        TaskType.Analysis.Name.Should().Be("analysis");
        TaskType.Analysis.PreferLocal.Should().BeFalse();
        TaskType.Analysis.PreferSpeed.Should().BeFalse();
        TaskType.Analysis.PreferQuality.Should().BeTrue();
    }

    [Fact]
    public void TaskType_Generation_PreferQuality()
    {
        TaskType.Generation.Name.Should().Be("generation");
        TaskType.Generation.PreferLocal.Should().BeFalse();
        TaskType.Generation.PreferSpeed.Should().BeFalse();
        TaskType.Generation.PreferQuality.Should().BeTrue();
    }

    [Fact]
    public void TaskType_Code_PreferQuality()
    {
        TaskType.Code.Name.Should().Be("code");
        TaskType.Code.PreferLocal.Should().BeFalse();
        TaskType.Code.PreferSpeed.Should().BeFalse();
        TaskType.Code.PreferQuality.Should().BeTrue();
    }

    [Fact]
    public void TaskType_Creative_PreferQuality()
    {
        TaskType.Creative.Name.Should().Be("creative");
        TaskType.Creative.PreferLocal.Should().BeFalse();
        TaskType.Creative.PreferSpeed.Should().BeFalse();
        TaskType.Creative.PreferQuality.Should().BeTrue();
    }

    [Theory]
    [InlineData("extraction", "extraction")]
    [InlineData("summarization", "summarization")]
    [InlineData("analysis", "analysis")]
    [InlineData("generation", "generation")]
    [InlineData("code", "code")]
    [InlineData("creative", "creative")]
    [InlineData("chat", "chat")]
    [InlineData("embedding", "embedding")]
    [InlineData("Analysis", "analysis")]
    [InlineData("CODE", "code")]
    public void TaskType_FromString_KnownTypes_ReturnsCorrectType(string input, string expectedName)
    {
        var result = TaskType.FromString(input);
        result.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("random-task")]
    public void TaskType_FromString_UnknownOrEmpty_ReturnsChat(string? input)
    {
        var result = TaskType.FromString(input!);
        result.Should().BeSameAs(TaskType.Chat);
    }

    [Fact]
    public void TaskType_AllNames_ContainsAllPredefinedTypes()
    {
        TaskType.AllNames.Should().Contain(
        [
            "extraction", "summarization", "analysis", "generation",
            "code", "creative", "chat", "embedding"
        ]);
    }

    [Fact]
    public void TaskType_ToString_ReturnsName()
    {
        TaskType.Analysis.ToString().Should().Be("analysis");
    }

    [Fact]
    public void TaskType_Equals_SameName_ReturnsTrue()
    {
        TaskType.FromString("analysis").Equals(TaskType.Analysis).Should().BeTrue();
    }

    [Fact]
    public void TaskType_Equals_DifferentName_ReturnsFalse()
    {
        TaskType.Analysis.Equals(TaskType.Chat).Should().BeFalse();
    }

    // ── RoutingProfile Tests ───────────────────────────────────────

    [Fact]
    public void RoutingProfile_CostOptimized_PreferLocalFirst()
    {
        RoutingProfile.CostOptimized.Id.Should().Be("cost-optimized");
        RoutingProfile.CostOptimized.PreferLocalFirst.Should().BeTrue();
        RoutingProfile.CostOptimized.TaskOverrides.Should().BeEmpty();
    }

    [Fact]
    public void RoutingProfile_QualityOptimized_PreferCloudFirst()
    {
        RoutingProfile.QualityOptimized.Id.Should().Be("quality-optimized");
        RoutingProfile.QualityOptimized.PreferLocalFirst.Should().BeFalse();
        RoutingProfile.QualityOptimized.TaskOverrides.Should().ContainKey("analysis");
        RoutingProfile.QualityOptimized.TaskOverrides.Should().ContainKey("code");
        RoutingProfile.QualityOptimized.TaskOverrides.Should().ContainKey("creative");
        RoutingProfile.QualityOptimized.TaskOverrides.Should().ContainKey("generation");
    }

    [Fact]
    public void RoutingProfile_Balanced_MixedPreference()
    {
        RoutingProfile.Balanced.Id.Should().Be("balanced");
        RoutingProfile.Balanced.PreferLocalFirst.Should().BeTrue();
        RoutingProfile.Balanced.TaskOverrides.Should().ContainKey("analysis");
        RoutingProfile.Balanced.TaskOverrides.Should().ContainKey("code");
        RoutingProfile.Balanced.TaskOverrides.Should().ContainKey("creative");
    }

    [Theory]
    [InlineData("cost-optimized", "cost-optimized")]
    [InlineData("quality-optimized", "quality-optimized")]
    [InlineData("balanced", "balanced")]
    [InlineData("COST-OOPTIMIZED", "balanced")] // typo → fallback
    [InlineData("", "balanced")]
    [InlineData(null, "balanced")]
    public void RoutingProfile_FromId_ReturnsCorrectProfile(string? input, string expectedId)
    {
        var result = RoutingProfile.FromId(input!);
        result.Id.Should().Be(expectedId);
    }

    [Fact]
    public void RoutingProfile_AllDefaultIds_ContainsThreeProfiles()
    {
        RoutingProfile.AllDefaultIds.Should().Contain(
        [
            "cost-optimized", "quality-optimized", "balanced"
        ]);
    }

    [Fact]
    public void RoutingProfile_ToString_ContainsId()
    {
        var result = RoutingProfile.Balanced.ToString();
        result.Should().Contain("balanced");
    }

    // ── RoutingDecision Tests ──────────────────────────────────────

    [Fact]
    public void RoutingDecision_DefaultValues_AreSet()
    {
        var decision = new RoutingDecision();
        decision.ProviderId.Should().BeEmpty();
        decision.ModelId.Should().BeEmpty();
        decision.TaskType.Should().BeSameAs(TaskType.Chat);
        decision.Profile.Should().BeSameAs(RoutingProfile.Balanced);
        decision.Reason.Should().BeEmpty();
        decision.DecidedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RoutingDecision_WithValues_PreservesData()
    {
        var decision = new RoutingDecision
        {
            ProviderId = "openai",
            ModelId = "gpt-4o-mini",
            TaskType = TaskType.Code,
            Profile = RoutingProfile.QualityOptimized,
            Reason = "Test reason",
            DecidedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        decision.ProviderId.Should().Be("openai");
        decision.ModelId.Should().Be("gpt-4o-mini");
        decision.TaskType.Should().BeSameAs(TaskType.Code);
        decision.Profile.Should().BeSameAs(RoutingProfile.QualityOptimized);
        decision.Reason.Should().Be("Test reason");
    }
}
