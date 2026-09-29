using System.Net;
using System.Text.Json;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Providers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI.Providers;

public sealed class AnthropicProviderTests
{
    private static readonly List<ChatMessage> Hello = new() { ChatMessage.User("hi") };

    private static AnthropicProvider Create(StubHttpHandler handler, ICostTracker? tracker = null) =>
        new("sk-ant-test", "https://api.anthropic.com/v1/", Log.Logger, handler, tracker);

    private static string Serialize(Dictionary<string, object> body) => JsonSerializer.Serialize(body);

    // Request body

    [Theory]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-opus-4-7")]
    [InlineData("claude-some-future-model")]
    public void Models_that_reject_sampling_parameters_get_none(string model)
    {
        var (body, _, _, _) = AnthropicProvider.BuildRequestBody(Hello, new ChatOptions { Temperature = 0.7, TopP = 0.9 }, model);

        body.Should().NotContainKey("temperature");
        body.Should().NotContainKey("top_p");
        body.Should().NotContainKey("top_k");
    }

    [Theory]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("claude-sonnet-4-20250514")]
    [InlineData("claude-opus-4-6")]
    public void Older_models_get_only_temperature_clamped_to_the_Anthropic_range(string model)
    {
        var (body, _, _, _) = AnthropicProvider.BuildRequestBody(Hello, new ChatOptions { Temperature = 1.8, TopP = 0.9 }, model);

        body["temperature"].Should().Be(1.0);
        body.Should().NotContainKey("top_p", "temperature and top_p together are rejected");
    }

    [Fact]
    public void Null_options_send_no_sampling_parameter()
    {
        var (body, _, _, _) = AnthropicProvider.BuildRequestBody(Hello, null, "claude-haiku-4-5-20251001");

        body.Should().NotContainKey("temperature");
        body["max_tokens"].Should().Be(2048);
    }

    [Fact]
    public void Structured_output_uses_forced_tool_use_where_the_model_supports_it()
    {
        var options = new ChatOptions
        {
            ResponseFormat = ResponseFormat.JsonObject,
            JsonSchema = """{"type":"object","properties":{"score":{"type":"number"}}}""",
            JsonSchemaName = "score"
        };

        var (body, forcing, _, _) = AnthropicProvider.BuildRequestBody(Hello, options, "claude-sonnet-5");

        forcing.Should().BeTrue();
        Serialize(body).Should().Contain("\"tool_choice\":{\"type\":\"tool\",\"name\":\"score\"}");
    }

    [Fact]
    public void Models_that_reject_forced_tool_use_get_json_mode_with_the_schema_instead()
    {
        var options = new ChatOptions
        {
            ResponseFormat = ResponseFormat.JsonObject,
            JsonSchema = """{"type":"object","properties":{"score":{"type":"number"}}}""",
            JsonSchemaName = "score"
        };

        var (body, forcing, _, _) = AnthropicProvider.BuildRequestBody(Hello, options, "claude-opus-5-5");

        forcing.Should().BeFalse();
        body.Should().NotContainKey("tool_choice");
        body.Should().NotContainKey("tools");
        body["system"].ToString().Should().Contain("valid JSON").And.Contain("\"score\"");
    }

    // Streaming

    [Fact]
    public async Task Mid_stream_error_event_fails_the_response_instead_of_returning_a_partial_answer()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Sse(
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":12,\"output_tokens\":1}}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Partial\"}}",
            "",
            "event: error",
            "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}",
            ""));
        var provider = Create(handler);

        var received = new List<string>();
        var act = async () =>
        {
            await foreach (var token in provider.StreamChatAsync(Hello, new ChatOptions { ModelId = "claude-sonnet-5" }))
                received.Add(token);
        };

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*overloaded_error*Overloaded*");
        received.Should().Equal("Partial");
        (await FluentActions.Awaiting(() => provider.ChatAsync(Hello)).Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("overloaded_error");
    }

    [Fact]
    public async Task Http_error_surfaces_the_api_error_body()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"temperature: not supported for this model\"}}"));
        var provider = Create(handler);

        var act = () => provider.ChatAsync(Hello, new ChatOptions { ModelId = "claude-sonnet-5" });

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("400").And.Contain("invalid_request_error").And.Contain("temperature: not supported");
    }

    [Fact]
    public async Task Usage_from_the_stream_is_recorded_with_cache_tokens()
    {
        var tracker = new Mock<ICostTracker>();
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Sse(
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":100,\"cache_creation_input_tokens\":40,\"cache_read_input_tokens\":900,\"output_tokens\":1}}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Done\"}}",
            "",
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":57}}",
            "",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            ""));
        var provider = Create(handler, tracker.Object);

        (await provider.ChatAsync(Hello, new ChatOptions { ModelId = "claude-haiku-4-5-20251001" })).Should().Be("Done");

        tracker.Verify(t => t.RecordUsage("claude-haiku-4-5-20251001", "anthropic", 100, 57, 40, 900), Times.Once);
    }

    [Fact]
    public async Task Streaming_honors_cancellation_while_waiting_for_the_next_event()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Hanging(
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"first\"}}\n\n"));
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

    // Connection and models

    [Fact]
    public async Task Connection_check_uses_the_free_models_endpoint()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, "{\"data\":[]}"));
        var provider = Create(handler);

        (await provider.CheckConnectionAsync()).Should().BeTrue();

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Path.Should().StartWith("/v1/models");
    }

    [Fact]
    public async Task Connection_check_reports_an_invalid_key_as_disconnected()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}"));

        (await Create(handler).CheckConnectionAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Models_are_listed_from_the_Models_API()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"claude-opus-5-5\",\"display_name\":\"Claude Opus 5.5\"},{\"id\":\"claude-sonnet-5\",\"display_name\":\"Claude Sonnet 5\"}],\"has_more\":false}"));

        var models = await Create(handler).ListModelsAsync();

        models.Select(m => m.Id).Should().Equal("claude-opus-5-5", "claude-sonnet-5");
        models.Should().OnlyContain(m => m.ProviderId == "anthropic");
    }

    [Fact]
    public async Task Model_list_falls_back_to_current_models_when_the_API_fails()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        var models = await Create(handler).ListModelsAsync();

        models.Select(m => m.Id).Should().Contain(new[] { "claude-opus-5-5", "claude-sonnet-5", "claude-haiku-4-5-20251001" });
        models.Select(m => m.Id).Should().NotContain(id => id.StartsWith("claude-3-5"), "retired models are not offered");
    }
}
