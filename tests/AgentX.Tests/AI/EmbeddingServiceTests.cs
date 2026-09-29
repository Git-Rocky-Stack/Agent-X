using AgentX.Core.AI;
using AgentX.Core.Configuration;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class EmbeddingServiceTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IRagConfiguration> _configuration = new();

    public EmbeddingServiceTests()
    {
        _configuration.Setup(c => c.EmbeddingBatchSize).Returns(2);
        _configuration.Setup(c => c.DefaultEmbeddingDimensions).Returns(384);
        _configuration.Setup(c => c.DefaultEmbeddingModel).Returns("all-minilm");
    }

    private EmbeddingService CreateService() => new(_aiService.Object, _configuration.Object);

    private static Mock<IAiProvider> Provider(string id)
    {
        var provider = new Mock<IAiProvider>();
        provider.Setup(p => p.ProviderId).Returns(id);
        return provider;
    }

    [Fact]
    public async Task Embeddings_use_the_embedding_target_not_the_active_chat_provider()
    {
        // Anthropic is the active chat provider; it has no embedding API.
        var anthropic = Provider("anthropic");
        anthropic.Setup(p => p.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Anthropic does not provide an embedding API."));
        _aiService.Setup(s => s.ActiveProvider).Returns(anthropic.Object);
        _aiService.Setup(s => s.IsConnected).Returns(true);

        var ollama = Provider("ollama");
        ollama.Setup(p => p.GenerateEmbeddingAsync("hello", "nomic-embed-text", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);
        _aiService.Setup(s => s.ResolveEmbeddingTarget()).Returns(new EmbeddingTarget("ollama", "nomic-embed-text"));
        _aiService.Setup(s => s.GetProvider("ollama")).Returns(ollama.Object);

        var service = CreateService();
        var vector = await service.EmbedAsync("hello");

        vector.Should().HaveCount(768);
        anthropic.Verify(p => p.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        service.ModelName.Should().Be("nomic-embed-text", "the saved Embedding Model setting is honored");
    }

    [Fact]
    public async Task ModelVersion_identifies_provider_model_and_actual_vector_size()
    {
        var local = Provider("local");
        local.Setup(p => p.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3072]);
        _aiService.Setup(s => s.ResolveEmbeddingTarget())
            .Returns(new EmbeddingTarget("local", "llama-3.2-3b-instruct-q4_k_m.gguf"));
        _aiService.Setup(s => s.GetProvider("local")).Returns(local.Object);

        var service = CreateService();
        service.ModelVersion.Should().Be("local:llama-3.2-3b-instruct-q4_k_m.gguf:3072", "known size before the first call");

        await service.EmbedAsync("text");

        service.ModelVersion.Should().Be("local:llama-3.2-3b-instruct-q4_k_m.gguf:3072");
        service.Dimensions.Should().Be(3072);
    }

    [Fact]
    public async Task ModelVersion_uses_the_observed_size_for_an_unknown_model()
    {
        var ollama = Provider("ollama");
        ollama.Setup(p => p.GenerateEmbeddingAsync(It.IsAny<string>(), "custom-embedder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[512]);
        _aiService.Setup(s => s.ResolveEmbeddingTarget()).Returns(new EmbeddingTarget("ollama", "custom-embedder"));
        _aiService.Setup(s => s.GetProvider("ollama")).Returns(ollama.Object);

        var service = CreateService();
        await service.EmbedAsync("text");

        service.ModelVersion.Should().Be("ollama:custom-embedder:512");
    }

    [Fact]
    public async Task OpenAI_embedding_model_without_a_key_fails_clearly_and_never_falls_back()
    {
        var ollama = Provider("ollama");
        _aiService.Setup(s => s.ResolveEmbeddingTarget()).Returns(new EmbeddingTarget("openai", "text-embedding-3-small"));
        _aiService.Setup(s => s.GetProvider("openai")).Returns((IAiProvider?)null);
        _aiService.Setup(s => s.GetProvider("ollama")).Returns(ollama.Object);

        var act = () => CreateService().EmbedAsync("secret document text");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OpenAI API key*");
        ollama.Verify(p => p.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Batch_uses_the_configured_model_and_rejects_a_short_result()
    {
        var ollama = Provider("ollama");
        ollama.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), "all-minilm", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, string _, CancellationToken _) =>
                texts.Select(_ => new float[384]).ToList());
        _aiService.Setup(s => s.ResolveEmbeddingTarget()).Returns(new EmbeddingTarget("ollama", "all-minilm"));
        _aiService.Setup(s => s.GetProvider("ollama")).Returns(ollama.Object);

        var vectors = await CreateService().EmbedBatchAsync(new[] { "a", "b", "c" });
        vectors.Should().HaveCount(3);
        ollama.Verify(p => p.GenerateEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), "all-minilm", It.IsAny<CancellationToken>()),
            Times.Exactly(2), "batch size 2 splits three texts into two calls");

        ollama.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), "all-minilm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<float[]>());

        var act = () => CreateService().EmbedBatchAsync(new[] { "a", "b" });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*0 embeddings for 2 texts*");
    }

    [Fact]
    public async Task An_empty_vector_is_reported_as_an_error()
    {
        var ollama = Provider("ollama");
        ollama.Setup(p => p.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<float>());
        _aiService.Setup(s => s.ResolveEmbeddingTarget()).Returns(new EmbeddingTarget("ollama", "all-minilm"));
        _aiService.Setup(s => s.GetProvider("ollama")).Returns(ollama.Object);

        var act = () => CreateService().EmbedAsync("text");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty embedding*");
    }
}
