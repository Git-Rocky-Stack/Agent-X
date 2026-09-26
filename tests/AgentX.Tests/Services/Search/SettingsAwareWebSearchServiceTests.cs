using System.Net;
using System.Text;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services.Search;

/// <summary>
/// Web search reads the current settings on every call (provider, the key or SearXNG URL of
/// that provider, result limit, cache duration), and a failed or empty answer is never cached.
/// </summary>
public sealed class SettingsAwareWebSearchServiceTests
{
    private const string BraveJson =
        "{\"web\":{\"results\":[" +
        "{\"title\":\"One\",\"url\":\"https://a.example/1\",\"description\":\"first\"}," +
        "{\"title\":\"Two\",\"url\":\"https://a.example/2\",\"description\":\"second\"}]}}";

    private readonly AppSettings _settings = new() { WebSearchProvider = WebSearchProvider.Brave, WebSearchApiKey = "brave-key" };
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly WebSearchCache _cache = new(Logger.None);

    public SettingsAwareWebSearchServiceTests()
    {
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => _settings);
    }

    private SettingsAwareWebSearchService CreateService(RecordingHandler handler) =>
        new(_settingsService.Object, _cache, Logger.None, new HttpClient(handler));

    [Fact]
    public void The_default_client_decodes_compressed_responses()
    {
        using var handler = WebSearchHttp.CreateHandler();

        handler.AutomaticDecompression.Should().Be(DecompressionMethods.All);
    }

    [Fact]
    public async Task Brave_requests_send_the_key_of_the_selected_provider_and_no_hand_written_accept_encoding()
    {
        var handler = new RecordingHandler(_ => Json(BraveJson));

        var response = await CreateService(handler).SearchAsync("local ai");

        response.Results.Should().HaveCount(2);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Host.Should().Be("api.search.brave.com");
        request.Headers.Should().Contain("X-Subscription-Token: brave-key");
        request.Headers.Should().NotContain(h => h.StartsWith("Accept-Encoding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_settings_change_applies_to_the_next_search_without_a_restart()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "google.serper.dev"
            ? Json("{\"organic\":[{\"title\":\"S\",\"link\":\"https://s.example/\",\"snippet\":\"s\"}]}")
            : Json(BraveJson));
        var service = CreateService(handler);

        await service.SearchAsync("first query");
        _settings.WebSearchProvider = WebSearchProvider.Serper;
        _settings.WebSearchApiKey = "serper-key";
        var response = await service.SearchAsync("second query");

        response.SearchProvider.Should().Be(WebSearchProvider.Serper);
        service.ActiveProvider.Should().Be(WebSearchProvider.Serper);
        var serperRequest = handler.Requests.Last();
        serperRequest.Uri.Host.Should().Be("google.serper.dev");
        serperRequest.Headers.Should().Contain("X-API-KEY: serper-key");
    }

    [Fact]
    public async Task Searxng_uses_the_instance_url_from_settings()
    {
        _settings.WebSearchProvider = WebSearchProvider.SearXng;
        _settings.WebSearchApiKey = "http://localhost:8888/";
        var handler = new RecordingHandler(_ => Json("{\"results\":[{\"title\":\"X\",\"url\":\"https://x.example/\",\"content\":\"x\"}]}"));
        var service = CreateService(handler);

        var response = await service.SearchAsync("searx query");

        service.IsConfigured.Should().BeTrue();
        response.Results.Should().ContainSingle();
        handler.Requests.Single().Uri.GetLeftPart(UriPartial.Path).Should().Be("http://localhost:8888/search");
    }

    [Fact]
    public async Task Searxng_without_a_url_does_not_fall_back_to_another_provider()
    {
        _settings.WebSearchProvider = WebSearchProvider.SearXng;
        _settings.WebSearchApiKey = "brave-key";
        var handler = new RecordingHandler(_ => Json(BraveJson));
        var service = CreateService(handler);

        var response = await service.SearchAsync("query");

        service.IsConfigured.Should().BeFalse();
        response.Results.Should().BeEmpty();
        handler.Requests.Should().BeEmpty("a Brave key must never be sent for a SearXNG selection");
    }

    [Fact]
    public async Task The_configured_result_limit_caps_every_search()
    {
        _settings.MaxSearchResults = 1;
        var handler = new RecordingHandler(_ => Json(BraveJson));

        var response = await CreateService(handler).SearchAsync("query", maxResults: 10);

        handler.Requests.Single().Uri.Query.Should().Contain("count=1");
        response.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task The_configured_cache_duration_is_used()
    {
        _settings.SearchCacheTtlMinutes = 15;
        var handler = new RecordingHandler(_ => Json(BraveJson));

        await CreateService(handler).SearchAsync("query");

        _cache.TtlMinutes.Should().Be(15);
    }

    [Fact]
    public async Task IsConfigured_follows_the_current_settings()
    {
        _settings.WebSearchApiKey = null;
        var service = CreateService(new RecordingHandler(_ => Json(BraveJson)));
        await _settingsService.Object.GetSettingsAsync();

        service.IsConfigured.Should().BeFalse();

        _settings.WebSearchApiKey = "new-key";
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Successful_results_are_cached()
    {
        var handler = new RecordingHandler(_ => Json(BraveJson));
        var service = CreateService(handler);

        await service.SearchAsync("cached query");
        var second = await service.SearchAsync("cached query");

        second.FromCache.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("\u001f\u008b\u0008 binary gzip bytes")]
    [InlineData("<html>rate limited</html>")]
    [InlineData("{\"web\":{\"results\":[]}}")]
    public async Task A_failed_or_empty_answer_is_not_cached(string body)
    {
        var handler = new RecordingHandler(_ => Json(body));
        var service = CreateService(handler);

        var first = await service.SearchAsync("retry me");
        var second = await service.SearchAsync("retry me");

        first.Results.Should().BeEmpty();
        second.FromCache.Should().BeFalse();
        handler.Requests.Should().HaveCount(2, "the next search must ask the provider again");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(Uri Uri, IReadOnlyList<string> Headers);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}").ToList();
            Requests.Add(new RecordedRequest(request.RequestUri!, headers));
            return Task.FromResult(respond(request));
        }
    }
}
