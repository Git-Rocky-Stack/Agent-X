using System.Net;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Providers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Providers;

public sealed class OpenAiProviderTests
{
    private static readonly List<ChatMessage> Hello = new() { ChatMessage.User("hi") };

    private static OpenAiProvider Create(StubHttpHandler handler, string endpoint = "https://api.openai.com/v1/", ICostTracker? tracker = null) =>
        new("sk-test", endpoint, Log.Logger, handler, tracker);

    [Fact]
    public void Null_options_do_not_throw_and_send_no_penalties()
    {
        var body = OpenAiProvider.BuildRequestBody(Hello, null, "gpt-4o-mini", includeUsage: false, Log.Logger);

        body["max_tokens"].Should().Be(2048);
        body["temperature"].Should().Be(0.7);
        body.Should().NotContainKey("frequency_penalty");
        body.Should().NotContainKey("presence_penalty");
        body.Should().NotContainKey("top_p");
    }

    [Theory]
    [InlineData("o1")]
    [InlineData("o3-mini")]
    [InlineData("o4-mini")]
    [InlineData("gpt-5")]
    [InlineData("gpt-5-mini")]
    public void Reasoning_models_get_max_completion_tokens_and_no_sampling(string model)
    {
        var options = new ChatOptions { MaxTokens = 500, Temperature = 0.2, TopP = 0.5, FrequencyPenalty = 0.3, PresencePenalty = 0.1 };

        var body = OpenAiProvider.BuildRequestBody(Hello, options, model, includeUsage: false, Log.Logger);

        body["max_completion_tokens"].Should().Be(500);
        body.Should().NotContainKey("max_tokens");
        body.Should().NotContainKey("temperature");
        body.Should().NotContainKey("top_p");
        body.Should().NotContainKey("frequency_penalty");
        body.Should().NotContainKey("presence_penalty");
    }

    [Fact]
    public void Chat_models_keep_their_sampling_parameters()
    {
        var options = new ChatOptions { MaxTokens = 300, Temperature = 0.2, TopP = 0.5, FrequencyPenalty = 0.3 };

        var body = OpenAiProvider.BuildRequestBody(Hello, options, "gpt-4o", includeUsage: true, Log.Logger);

        body["max_tokens"].Should().Be(300);
        body["temperature"].Should().Be(0.2);
        body["top_p"].Should().Be(0.5);
        body["frequency_penalty"].Should().Be(0.3);
        body.Should().ContainKey("stream_options");
    }

    [Theory]
    [InlineData("gpt-4o", true)]
    [InlineData("gpt-4o-mini", true)]
    [InlineData("gpt-4.1", true)]
    [InlineData("gpt-5", true)]
    [InlineData("chatgpt-4o-latest", true)]
    [InlineData("o3-mini", true)]
    [InlineData("gpt-image-1", false)]
    [InlineData("gpt-4o-mini-tts", false)]
    [InlineData("gpt-4o-transcribe", false)]
    [InlineData("gpt-4o-realtime-preview", false)]
    [InlineData("gpt-4o-audio-preview", false)]
    [InlineData("gpt-3.5-turbo-instruct", false)]
    [InlineData("o1-pro", false)]
    [InlineData("text-embedding-3-small", false)]
    [InlineData("dall-e-3", false)]
    [InlineData("whisper-1", false)]
    [InlineData("tts-1", false)]
    [InlineData("omni-moderation-latest", false)]
    public void Only_chat_completion_models_are_listed(string model, bool listed)
    {
        OpenAiProvider.IsChatModel(model).Should().Be(listed);
    }

    [Fact]
    public async Task Mid_stream_error_payload_fails_the_response()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Sse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"}}]}",
            "",
            "data: {\"error\":{\"message\":\"The server had an error while processing your request.\",\"type\":\"server_error\"}}",
            ""));
        var provider = Create(handler);

        var received = new List<string>();
        var act = async () =>
        {
            await foreach (var token in provider.StreamChatAsync(Hello, new ChatOptions { ModelId = "gpt-4o" }))
                received.Add(token);
        };

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*server_error*");
        received.Should().Equal("Partial");
    }

    [Fact]
    public async Task Http_error_surfaces_the_api_error_body()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Unsupported parameter: 'max_tokens'\",\"type\":\"invalid_request_error\",\"code\":\"unsupported_parameter\"}}"));

        var act = () => Create(handler).ChatAsync(Hello, new ChatOptions { ModelId = "gpt-4o" });

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("400").And.Contain("unsupported_parameter").And.Contain("max_tokens");
    }

    [Fact]
    public async Task Usage_is_requested_from_OpenAI_and_recorded()
    {
        var tracker = new Mock<ICostTracker>();
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Sse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}",
            "",
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":21,\"completion_tokens\":2,\"prompt_tokens_details\":{\"cached_tokens\":0}}}",
            "",
            "data: [DONE]",
            ""));
        var provider = Create(handler, tracker: tracker.Object);

        (await provider.ChatAsync(Hello, new ChatOptions { ModelId = "gpt-4o-mini" })).Should().Be("Hi");

        handler.Requests.Single().Body.Should().Contain("\"stream_options\":{\"include_usage\":true}");
        tracker.Verify(t => t.RecordUsage("gpt-4o-mini", "openai", 21, 2), Times.Once);
    }

    [Fact]
    public async Task Custom_compatible_endpoints_do_not_receive_stream_options()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Sse("data: [DONE]", ""));
        var provider = Create(handler, endpoint: "http://localhost:1234/v1/");

        await provider.ChatAsync(Hello, new ChatOptions { ModelId = "local-model" });

        handler.Requests.Single().Body.Should().NotContain("stream_options");
    }

    [Fact]
    public async Task Streaming_honors_cancellation_while_waiting_for_the_next_chunk()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Hanging(
            "data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n"));
        var provider = Create(handler);
        using var cts = new CancellationTokenSource();

        var act = async () =>
        {
            await foreach (var token in provider.StreamChatAsync(Hello, null, cts.Token))
            {
                token.Should().Be("first");
                cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
