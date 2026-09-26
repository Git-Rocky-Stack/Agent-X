using System.Net;
using System.Text;

namespace AgentX.Tests.AI.Providers;

/// <summary>
/// Records requests and answers them from a delegate, so cloud providers can be driven through
/// their real HTTP and SSE parsing without a network.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
        return _respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Sse(params string[] lines) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join("\n", lines) + "\n", Encoding.UTF8, "text/event-stream")
        };

    /// <summary>A 200 response whose body never ends, for cancellation tests.</summary>
    public static HttpResponseMessage Hanging(string firstLines) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream(Encoding.UTF8.GetBytes(firstLines))) };

    /// <summary>Serves a prefix, then blocks every read until the read is cancelled.</summary>
    private sealed class HangingStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public HangingStream(byte[] prefix) => _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                var n = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsMemory(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
