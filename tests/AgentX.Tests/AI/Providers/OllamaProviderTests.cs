using System.Globalization;
using System.Net;
using AgentX.Core.AI.Providers;
using FluentAssertions;
using OllamaSharp;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Providers;

public sealed class OllamaProviderTests
{
    [Theory]
    [InlineData("3.8B", 3800)]
    [InlineData("7B", 7000)]
    [InlineData("70.6B", 70600)]
    [InlineData("137M", 137)]
    [InlineData("", 0)]
    [InlineData("n/a", 0)]
    public void Parameter_size_is_parsed_independently_of_the_current_culture(string size, int millions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses ',' as the decimal separator; "3.8" must still mean 3.8.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            OllamaProvider.ParseParameterCount(size).Should().Be(millions);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Embedding_names_its_model_in_the_request_and_leaves_the_chat_model_alone()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            "{\"model\":\"all-minilm\",\"embeddings\":[[0.1,0.2,0.3]]}"));
        var client = new OllamaApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") }, "llama3.2");
        using var provider = new OllamaProvider(client, Log.Logger, costTracker: null);

        var vector = await provider.GenerateEmbeddingAsync("hello", "all-minilm");

        vector.Should().HaveCount(3);
        handler.Requests.Single().Body.Should().Contain("\"model\":\"all-minilm\"");
        client.SelectedModel.Should().Be("llama3.2", "an embedding call must not switch the chat model");
    }

    [Fact]
    public async Task Disposed_provider_rejects_new_calls()
    {
        var provider = new OllamaProvider(new Uri("http://localhost:11434"), Log.Logger);
        provider.Dispose();
        provider.Dispose(); // idempotent

        await FluentActions.Awaiting(() => provider.GenerateEmbeddingAsync("x", "all-minilm"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => provider.CheckConnectionAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
    }
}
