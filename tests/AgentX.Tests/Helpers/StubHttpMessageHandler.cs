using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;

namespace AgentX.Tests.Helpers;

/// <summary>
/// An <see cref="HttpMessageHandler"/> for connector provider tests: records every request
/// (URL, headers, body) and answers it with whatever the test's responder returns, so the
/// provider's real request building and response parsing run without a network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    private readonly List<RecordedRequest> _requests = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    /// <summary>Answers the requests in order with the given responses (the last repeats).</summary>
    public static StubHttpMessageHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var index = 0;
        return new StubHttpMessageHandler(_ =>
        {
            var next = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return next();
        });
    }

    /// <summary>Requests received so far, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToList();
        }
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) =>
        new(status) { Content = new StringContent(string.Empty) };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var headers = request.Headers.ToDictionary(
            h => h.Key,
            h => (IReadOnlyList<string>)h.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);

        lock (_requests)
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));

        var response = _responder(request);
        response.RequestMessage ??= request;
        return response;
    }
}

/// <summary>One request seen by <see cref="StubHttpMessageHandler"/>.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Body)
{
    /// <summary>The decoded query string parameters.</summary>
    public NameValueCollection Query => HttpUtility.ParseQueryString(Uri.Query);

    /// <summary>Every value sent for <paramref name="name"/>, or an empty list.</summary>
    public IReadOnlyList<string> Header(string name) =>
        Headers.TryGetValue(name, out var values) ? values : [];
}
