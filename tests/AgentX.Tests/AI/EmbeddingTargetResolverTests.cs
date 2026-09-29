using AgentX.Core.AI;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class EmbeddingTargetResolverTests
{
    private const string BuiltIn = "llama-3.2-3b-instruct-q4_k_m.gguf";

    [Fact]
    public void Default_setting_uses_the_built_in_model_when_it_is_installed()
    {
        EmbeddingTargetResolver.Resolve("all-minilm", BuiltIn, localModelInstalled: true)
            .Should().Be(new EmbeddingTarget("local", BuiltIn));
    }

    [Fact]
    public void Default_setting_uses_Ollama_all_minilm_when_the_built_in_model_is_missing()
    {
        EmbeddingTargetResolver.Resolve("all-minilm", BuiltIn, localModelInstalled: false)
            .Should().Be(new EmbeddingTarget("ollama", "all-minilm"));
        EmbeddingTargetResolver.Resolve(null, BuiltIn, localModelInstalled: false)
            .Should().Be(new EmbeddingTarget("ollama", "all-minilm"));
    }

    [Theory]
    [InlineData("nomic-embed-text")]
    [InlineData("all-minilm:latest")]
    [InlineData("mxbai-embed-large")]
    public void An_explicit_Ollama_embedding_model_is_honored_even_when_the_built_in_model_exists(string model)
    {
        EmbeddingTargetResolver.Resolve(model, BuiltIn, localModelInstalled: true)
            .Should().Be(new EmbeddingTarget("ollama", model));
    }

    [Theory]
    [InlineData("text-embedding-3-small")]
    [InlineData("text-embedding-3-large")]
    [InlineData("TEXT-EMBEDDING-ADA-002")]
    public void Only_an_explicit_OpenAI_embedding_model_selects_the_cloud(string model)
    {
        EmbeddingTargetResolver.Resolve(model, BuiltIn, localModelInstalled: true)
            .Should().Be(new EmbeddingTarget("openai", model));
    }

    [Fact]
    public void A_gguf_file_name_selects_the_built_in_provider()
    {
        EmbeddingTargetResolver.Resolve("llama-3.2-1b-instruct-q4_k_m.gguf", BuiltIn, localModelInstalled: false)
            .Should().Be(new EmbeddingTarget("local", "llama-3.2-1b-instruct-q4_k_m.gguf"));
    }

    [Theory]
    [InlineData("all-minilm", true)]
    [InlineData("all-minilm", false)]
    [InlineData("claude-sonnet-5", true)]
    [InlineData("", false)]
    public void Anthropic_is_never_chosen(string setting, bool installed)
    {
        EmbeddingTargetResolver.Resolve(setting, BuiltIn, installed).ProviderId.Should().NotBe("anthropic");
    }

    [Theory]
    [InlineData("ollama", "all-minilm", 384)]
    [InlineData("ollama", "nomic-embed-text:latest", 768)]
    [InlineData("openai", "text-embedding-3-small", 1536)]
    [InlineData("local", "llama-3.2-3b-instruct-q4_k_m.gguf", 3072)]
    [InlineData("local", "llama-3.2-1b-instruct-q4_k_m.gguf", 2048)]
    public void Known_models_report_their_vector_size(string provider, string model, int dimensions)
    {
        EmbeddingTargetResolver.KnownDimensions(new EmbeddingTarget(provider, model)).Should().Be(dimensions);
    }

    [Fact]
    public void Unknown_models_have_no_assumed_vector_size()
    {
        EmbeddingTargetResolver.KnownDimensions(new EmbeddingTarget("ollama", "my-custom-embedder")).Should().BeNull();
    }
}
