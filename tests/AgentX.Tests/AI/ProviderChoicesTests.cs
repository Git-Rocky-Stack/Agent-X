using AgentX.Core.AI;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class ProviderChoicesTests
{
    [Fact]
    public void The_default_built_in_provider_is_selectable()
    {
        var defaultProvider = new AppSettings().ActiveProviderId;

        ProviderChoices.IndexOf(defaultProvider).Should().BeGreaterThanOrEqualTo(0,
            "opening Settings must show the default provider instead of silently selecting Ollama");
        ProviderChoices.ResolveSelection(ProviderChoices.IndexOf(defaultProvider), defaultProvider)
            .Should().Be(defaultProvider);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("ollama")]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("OpenAI")]
    public void Every_registered_provider_round_trips(string providerId)
    {
        var index = ProviderChoices.IndexOf(providerId);

        index.Should().BeGreaterThanOrEqualTo(0);
        ProviderChoices.ResolveSelection(index, providerId).Should().Be(providerId.ToLowerInvariant());
    }

    [Theory]
    [InlineData("lmstudio")]
    [InlineData("some-plugin-provider")]
    public void Unknown_provider_ids_are_saved_back_unchanged(string providerId)
    {
        var index = ProviderChoices.IndexOf(providerId);

        index.Should().Be(-1);
        ProviderChoices.ResolveSelection(index, providerId).Should().Be(providerId);
    }

    [Fact]
    public void Picking_a_provider_replaces_the_saved_one()
    {
        ProviderChoices.ResolveSelection(ProviderChoices.IndexOf("anthropic"), "local").Should().Be("anthropic");
        ProviderChoices.ResolveSelection(-1, null).Should().Be("local");
        ProviderChoices.DisplayNames.Should().HaveCount(ProviderChoices.All.Count);
    }
}
