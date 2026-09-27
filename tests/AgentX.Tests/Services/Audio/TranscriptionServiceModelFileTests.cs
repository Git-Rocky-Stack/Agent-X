using System.Net;
using AgentX.Core.Services.Audio;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Audio;

/// <summary>
/// Model file management of <see cref="TranscriptionService"/>: downloading the Whisper model into
/// place only when the download is complete and really is a model, reporting its size, removing
/// it, and failing transcription with a typed exception while it is missing. Downloads go through
/// a stub HTTP handler into a temporary model directory, so nothing touches the network or the
/// user's real model folder.
/// </summary>
public sealed class TranscriptionServiceModelFileTests : IDisposable
{
    private const string BaseModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

    private readonly string _modelDirectory =
        Path.Combine(Path.GetTempPath(), "agentx-whisper-" + Guid.NewGuid().ToString("N"));

    private StubHandler? _handler;

    private string ModelPath => Path.Combine(_modelDirectory, "ggml-base.bin");

    private string PartialPath => ModelPath + ".download";

    public void Dispose()
    {
        _handler?.Dispose();
        try { Directory.Delete(_modelDirectory, recursive: true); } catch (IOException) { }
    }

    private TranscriptionService CreateSut(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _handler = new StubHandler(respond);
        return new TranscriptionService(Log.Logger, _modelDirectory, _handler);
    }

    /// <summary>A plausible model: the GGML magic followed by enough bytes for several reads.</summary>
    private static byte[] ModelBytes() =>
        new byte[] { 0x6c, 0x6d, 0x67, 0x67 }
            .Concat(Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)))
            .ToArray();

    private static HttpResponseMessage Ok(HttpContent content) =>
        new(HttpStatusCode.OK) { Content = content };

    // Download
    [Fact]
    public async Task Download_WritesTheCompleteModelIntoPlace_AndReportsProgress()
    {
        var model = ModelBytes();
        var sut = CreateSut(_ => Ok(new ByteArrayContent(model)));
        var progress = new RecordingProgress();

        await sut.DownloadModelAsync("base", progress);

        _handler!.Requests.Should().Equal(new Uri(BaseModelUrl));
        File.ReadAllBytes(ModelPath).Should().Equal(model);
        File.Exists(PartialPath).Should().BeFalse();
        (await sut.IsModelAvailableAsync("base")).Should().BeTrue();
        (await sut.GetInstalledModelSizeAsync("base")).Should().Be(model.Length);
        progress.Values.Should().NotBeEmpty().And.BeInAscendingOrder();
        progress.Values[^1].Should().Be(1.0);
    }

    [Fact]
    public async Task Download_WhileInProgress_WritesOnlyTheTemporaryFile()
    {
        var model = ModelBytes();
        var sut = CreateSut(_ => Ok(new ByteArrayContent(model)));
        var seen = new List<(bool Model, bool Partial)>();
        var progress = new RecordingProgress(_ => seen.Add((File.Exists(ModelPath), File.Exists(PartialPath))));

        await sut.DownloadModelAsync("base", progress);

        // Every report before the final 1.0 comes from the copy loop, before the move.
        seen.SkipLast(1).Should().NotBeEmpty().And.OnlyContain(s => !s.Model && s.Partial);
        File.Exists(ModelPath).Should().BeTrue();
    }

    [Fact]
    public async Task Download_WithoutContentLength_StillInstallsAVerifiedModel()
    {
        var model = ModelBytes();
        var sut = CreateSut(_ =>
        {
            var content = new ByteArrayContent(model);
            content.Headers.ContentLength = null;
            return Ok(content);
        });

        await sut.DownloadModelAsync("base");

        File.ReadAllBytes(ModelPath).Should().Equal(model);
        File.Exists(PartialPath).Should().BeFalse();
    }

    [Fact]
    public async Task Download_ShorterThanItsContentLength_FailsAndLeavesNothingBehind()
    {
        var model = ModelBytes();
        var sut = CreateSut(_ =>
        {
            var content = new ByteArrayContent(model);
            content.Headers.ContentLength = model.Length + 4096;
            return Ok(content);
        });

        var act = () => sut.DownloadModelAsync("base");

        (await act.Should().ThrowAsync<IOException>()).WithMessage("*incomplete*");
        File.Exists(ModelPath).Should().BeFalse();
        File.Exists(PartialPath).Should().BeFalse();
        (await sut.IsModelAvailableAsync("base")).Should().BeFalse();
    }

    [Fact]
    public async Task Download_ThatIsNotAModel_FailsAndLeavesNothingBehind()
    {
        // A proxy or captive portal can answer 200 with a page of its own.
        var sut = CreateSut(_ => Ok(new StringContent("<html><body>Sign in to continue</body></html>")));

        var act = () => sut.DownloadModelAsync("base");

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(ModelPath).Should().BeFalse();
        File.Exists(PartialPath).Should().BeFalse();
    }

    [Fact]
    public async Task Download_HttpError_FailsAndLeavesNothingBehind()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var act = () => sut.DownloadModelAsync("base");

        await act.Should().ThrowAsync<HttpRequestException>();
        File.Exists(ModelPath).Should().BeFalse();
        File.Exists(PartialPath).Should().BeFalse();
    }

    [Fact]
    public async Task Download_CancelledMidway_LeavesNoPartialFile()
    {
        var firstChunk = ModelBytes().Take(1_000).ToArray();
        var sut = CreateSut(_ =>
        {
            var content = new StreamContent(new StallingStream(firstChunk));
            content.Headers.ContentLength = 10_000;
            return Ok(content);
        });
        using var cts = new CancellationTokenSource();
        var partialExistedWhileDownloading = false;
        var progress = new RecordingProgress(_ =>
        {
            partialExistedWhileDownloading = File.Exists(PartialPath);
            cts.Cancel();
        });

        var act = () => sut.DownloadModelAsync("base", progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        partialExistedWhileDownloading.Should().BeTrue();
        File.Exists(PartialPath).Should().BeFalse();
        File.Exists(ModelPath).Should().BeFalse();
    }

    [Fact]
    public async Task Download_WhenAlreadyInstalled_DoesNotDownloadAgain()
    {
        Directory.CreateDirectory(_modelDirectory);
        File.WriteAllBytes(ModelPath, ModelBytes());
        var sut = CreateSut(_ => throw new InvalidOperationException("no request expected"));
        var progress = new RecordingProgress();

        await sut.DownloadModelAsync("base", progress);

        _handler!.Requests.Should().BeEmpty();
        progress.Values.Should().Equal(1.0);
    }

    [Fact]
    public async Task PartialFileFromAnEarlierSession_IsNotAModel_AndIsReplacedByTheDownload()
    {
        Directory.CreateDirectory(_modelDirectory);
        File.WriteAllBytes(PartialPath, new byte[] { 1, 2, 3 });
        var model = ModelBytes();
        var sut = CreateSut(_ => Ok(new ByteArrayContent(model)));

        (await sut.IsModelAvailableAsync("base")).Should().BeFalse();
        (await sut.GetInstalledModelSizeAsync("base")).Should().BeNull();

        await sut.DownloadModelAsync("base");

        File.ReadAllBytes(ModelPath).Should().Equal(model);
        File.Exists(PartialPath).Should().BeFalse();
    }

    // Size and removal
    [Fact]
    public async Task GetInstalledModelSize_IsNullWhileTheModelIsMissing()
    {
        var sut = CreateSut(_ => throw new InvalidOperationException("no request expected"));

        (await sut.GetInstalledModelSizeAsync("base")).Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesTheModelAndAnyPartialDownload()
    {
        Directory.CreateDirectory(_modelDirectory);
        File.WriteAllBytes(ModelPath, ModelBytes());
        File.WriteAllBytes(PartialPath, new byte[] { 1, 2, 3 });
        var sut = CreateSut(_ => throw new InvalidOperationException("no request expected"));

        await sut.RemoveModelAsync("base");

        File.Exists(ModelPath).Should().BeFalse();
        File.Exists(PartialPath).Should().BeFalse();
        (await sut.GetInstalledModelSizeAsync("base")).Should().BeNull();
    }

    [Fact]
    public async Task Remove_WhenNothingIsInstalled_DoesNothing()
    {
        var sut = CreateSut(_ => throw new InvalidOperationException("no request expected"));

        var act = () => sut.RemoveModelAsync("base");

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("huge")]
    [InlineData("../../base")]
    public async Task SizeAndRemove_RejectUnknownModelSizes(string modelSize)
    {
        var sut = CreateSut(_ => throw new InvalidOperationException("no request expected"));

        await sut.Invoking(s => s.GetInstalledModelSizeAsync(modelSize)).Should().ThrowAsync<ArgumentException>();
        await sut.Invoking(s => s.RemoveModelAsync(modelSize)).Should().ThrowAsync<ArgumentException>();
    }

    // Transcription without the model
    [Fact]
    public async Task Transcribe_WithoutTheModel_ThrowsTheTypedMissingModelException()
    {
        Directory.CreateDirectory(_modelDirectory);
        var audio = Path.Combine(_modelDirectory, "talk.wav");
        File.WriteAllBytes(audio, new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x01 });
        var sut = CreateSut(_ => throw new InvalidOperationException("no request expected"));

        var act = () => sut.TranscribeFileAsync(audio);

        var thrown = await act.Should().ThrowAsync<TranscriptionModelMissingException>();
        thrown.Which.ModelSize.Should().Be("base");
        thrown.Which.Should().BeAssignableTo<InvalidOperationException>();
    }

    // Doubles
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_respond(request));
        }
    }

    /// <summary>Records every report synchronously, unlike <see cref="Progress{T}"/>, which posts them.</summary>
    private sealed class RecordingProgress : IProgress<double>
    {
        private readonly Action<double>? _onReport;

        public RecordingProgress(Action<double>? onReport = null) => _onReport = onReport;

        public List<double> Values { get; } = new();

        public void Report(double value)
        {
            Values.Add(value);
            _onReport?.Invoke(value);
        }
    }

    /// <summary>Serves one chunk, then waits for cancellation instead of serving the rest.</summary>
    private sealed class StallingStream : Stream
    {
        private readonly byte[] _firstChunk;
        private bool _served;

        public StallingStream(byte[] firstChunk) => _firstChunk = firstChunk;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_served)
            {
                _served = true;
                _firstChunk.CopyTo(buffer);
                return _firstChunk.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
