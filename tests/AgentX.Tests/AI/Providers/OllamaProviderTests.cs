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

    private static OllamaProvider StreamingProvider(params string[] ndjsonLines)
    {
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join("\n", ndjsonLines) + "\n", System.Text.Encoding.UTF8, "application/x-ndjson")
        });
        var client = new OllamaApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") }, "llama3.2");
        return new OllamaProvider(client, Log.Logger, costTracker: null);
    }

    [Fact]
    public async Task Mid_stream_error_fails_the_response_instead_of_returning_a_partial_answer()
    {
        using var provider = StreamingProvider(
            "{\"model\":\"llama3.2\",\"created_at\":\"2026-09-26T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"Partial\"},\"done\":false}",
            "{\"error\":\"model runner has unexpectedly stopped\"}");

        var received = new List<string>();
        var act = async () =>
        {
            await foreach (var token in provider.StreamChatAsync(new List<AgentX.Core.AI.Models.ChatMessage> { AgentX.Core.AI.Models.ChatMessage.User("hi") }))
                received.Add(token);
        };

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*before it finished*");
        received.Should().Equal("Partial");
        await FluentActions.Awaiting(() => provider.ChatAsync(new List<AgentX.Core.AI.Models.ChatMessage> { AgentX.Core.AI.Models.ChatMessage.User("hi") }))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Completed_stream_returns_the_answer_and_records_usage()
    {
        var tracker = new Moq.Mock<AgentX.Core.AI.Models.ICostTracker>();
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"model\":\"llama3.2\",\"created_at\":\"2026-09-26T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"done\":false}\n" +
                "{\"model\":\"llama3.2\",\"created_at\":\"2026-09-26T00:00:01Z\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":12,\"eval_count\":3}\n",
                System.Text.Encoding.UTF8, "application/x-ndjson")
        });
        var client = new OllamaApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") }, "llama3.2");
        using var provider = new OllamaProvider(client, Log.Logger, tracker.Object);

        var answer = await provider.ChatAsync(
            new List<AgentX.Core.AI.Models.ChatMessage> { AgentX.Core.AI.Models.ChatMessage.User("hi") },
            new AgentX.Core.AI.Models.ChatOptions { ModelId = "llama3.2" });

        answer.Should().Be("Hello");
        tracker.Verify(t => t.RecordUsage("llama3.2", "ollama", 12, 3), Moq.Times.Once);
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
