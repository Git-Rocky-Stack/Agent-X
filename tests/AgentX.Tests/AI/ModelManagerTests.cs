using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class ModelManagerTests
{
    private static Mock<IAiProvider> Provider(string id, params string[] models)
    {
        var provider = new Mock<IAiProvider>();
        provider.Setup(p => p.ProviderId).Returns(id);
        provider.Setup(p => p.ListModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(models.Select(m => new AiModel { Id = m, Name = m, ProviderId = id }).ToList());
        return provider;
    }

    [Fact]
    public async Task Model_list_is_not_reused_after_the_active_provider_changes()
    {
        var ollama = Provider("ollama", "llama3.2", "mistral");
        var openAi = Provider("openai", "gpt-4o-mini");
        var aiService = new Mock<IAiService>();
        aiService.Setup(s => s.ActiveProvider).Returns(ollama.Object);
        var manager = new ModelManager(aiService.Object);

        (await manager.GetInstalledModelsAsync()).Select(m => m.Id).Should().Equal("llama3.2", "mistral");

        aiService.Setup(s => s.ActiveProvider).Returns(openAi.Object);

        (await manager.GetInstalledModelsAsync()).Select(m => m.Id).Should().Equal(new[] { "gpt-4o-mini" },
            "the cached Ollama list must not be offered for the OpenAI provider");
        (await manager.IsModelAvailableAsync("llama3.2")).Should().BeFalse();
    }

    [Fact]
    public async Task Model_list_is_cached_for_the_same_provider()
    {
        var ollama = Provider("ollama", "llama3.2");
        var aiService = new Mock<IAiService>();
        aiService.Setup(s => s.ActiveProvider).Returns(ollama.Object);
        var manager = new ModelManager(aiService.Object);

        await manager.GetInstalledModelsAsync();
        await manager.GetInstalledModelsAsync();

        ollama.Verify(p => p.ListModelsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
