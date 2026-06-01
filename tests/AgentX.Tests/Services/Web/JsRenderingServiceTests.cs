using System.Net;
using System.Net.Sockets;
using System.Text;
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

    // The two tests below were previously [Fact(Skip = "Requires Playwright browsers installed")]
    // and hit the live https://example.com, making them both manual and network-dependent. They
    // are now hermetic (served from a local loopback HTTP server) and SkippableFacts: they run
    // wherever a Playwright browser is installed (CI runs `playwright install chromium`) and skip
    // gracefully — rather than fail — on a machine without the browser.

    [SkippableFact]
    public async Task RenderPageAsync_executes_javascript_and_returns_the_rendered_dom()
    {
        await using var server = LocalHtmlServer.Start(
            "<html><body><div id='app'></div>" +
            "<script>document.getElementById('app').textContent = 'Rendered by JS';</script>" +
            "</body></html>");

        using var service = new JsRenderingService();

        var html = await RenderOrSkipAsync(service, server.Url, waitForNetworkIdle: true);

        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("Rendered by JS",
            "the service must return the JavaScript-executed DOM, not the static source markup");
    }

    [SkippableFact]
    public async Task RenderPageAsync_returns_static_markup_for_a_plain_page()
    {
        await using var server = LocalHtmlServer.Start(
            "<html><body><h1>Hello Hermetic World</h1></body></html>");

        using var service = new JsRenderingService();

        var html = await RenderOrSkipAsync(service, server.Url, waitForNetworkIdle: false);

        html.Should().Contain("Hello Hermetic World");
    }

    /// <summary>
    /// Renders the page, translating a "browser not installed" Playwright failure into a test skip
    /// so environments without <c>playwright install</c> do not see a hard failure.
    /// </summary>
    private static async Task<string> RenderOrSkipAsync(IJsRenderingService service, string url, bool waitForNetworkIdle)
    {
        try
        {
            return await service.RenderPageAsync(url, waitForNetworkIdle);
        }
        catch (Microsoft.Playwright.PlaywrightException ex) when (
            ex.Message.Contains("install", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            Skip.If(true, "Playwright browser not installed — run `pwsh bin/.../playwright.ps1 install chromium`.");
            throw; // unreachable; Skip.If throws.
        }
    }

    /// <summary>Minimal loopback HTTP server that serves a single fixed HTML document.</summary>
    private sealed class LocalHtmlServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _payload;
        private readonly CancellationTokenSource _cts = new();

        public string Url { get; }

        private LocalHtmlServer(string html, int port)
        {
            _payload = Encoding.UTF8.GetBytes(html);
            Url = $"http://localhost:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(ServeLoopAsync);
        }

        public static LocalHtmlServer Start(string html) => new(html, GetFreeLoopbackPort());

        private async Task ServeLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break; // listener stopped
                }

                try
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength64 = _payload.Length;
                    await context.Response.OutputStream.WriteAsync(_payload);
                }
                catch
                {
                    // client went away; ignore
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        private static int GetFreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // best-effort teardown
            }

            _cts.Dispose();
            await Task.CompletedTask;
        }
    }
}
