using AgentX.Core.Services.Web;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class JsRenderingServiceTests
{
    [Fact]
    public void JsRenderingService_Implements_IJsRenderingService()
    {
        var service = new JsRenderingService();
        service.Should().BeAssignableTo<IJsRenderingService>();
    }

    [Fact]
    public void JsRenderingService_CanBeDisposed()
    {
        var service = new JsRenderingService();
        service.Dispose();
        // No exception thrown
    }

    [Fact(Skip = "Requires Playwright browsers installed - run manually")]
    public async Task RenderPageAsync_ReturnsNonEmptyHtml()
    {
        using var service = new JsRenderingService();
        var result = await service.RenderPageAsync("https://example.com");
        result.Should().NotBeEmpty();
        result.Should().Contain("Example Domain");
    }

    [Fact(Skip = "Requires Playwright browsers installed - run manually")]
    public async Task RenderPageAsync_WithNetworkIdle_WaitsForJs()
    {
        using var service = new JsRenderingService();
        var result = await service.RenderPageAsync("https://example.com", waitForNetworkIdle: true);
        result.Should().NotBeEmpty();
    }
}