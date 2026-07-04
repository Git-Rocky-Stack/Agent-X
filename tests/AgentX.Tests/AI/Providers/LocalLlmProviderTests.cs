using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Providers;
using FluentAssertions;
using LLama.Common;
using LLama.Sampling;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AgentX.Tests.AI.Providers;

/// <summary>
/// Behavioural coverage for <see cref="LocalLlmProvider"/> — the LLamaSharp-backed offline
/// provider. Real native model loading is impossible in unit tests (needs a multi-GB GGUF), so
/// coverage splits three ways: (1) file-system paths (listing, delete, availability) run for
/// real against a temp models directory; (2) the streaming-chat pipeline runs through the
/// internal <c>InferenceOverride</c> seam with hand-rolled IAsyncEnumerable token streams
/// (established AX-QA-009 harness); (3) the download pipeline runs through the internal
/// <c>DownloadUrlResolver</c> seam against a localhost HttpListener stub (established
/// HttpListener-stub + bind-retry harness). The deliberate residual is LoadModelAsync's
/// success body and the real StatelessExecutor/embedder calls.
/// </summary>
public sealed class LocalLlmProviderTests : IDisposable
{
    private const string PrimaryModel = "llama-3.2-3b-instruct-q4_k_m.gguf";

    private readonly string _modelsDir =
        Path.Combine(Path.GetTempPath(), "agentx-llm-tests", Guid.NewGuid().ToString("N"));
    private readonly List<LocalLlmProvider> _providers = new();
    private readonly CollectingSink _sink = new();
    private readonly Logger _logger;

    public LocalLlmProviderTests()
    {
        _logger = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
    }

    public void Dispose()
    {
        foreach (var p in _providers) p.Dispose();
        _logger.Dispose();
        try { if (Directory.Exists(_modelsDir)) Directory.Delete(_modelsDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private LocalLlmProvider NewProvider(
        string modelFileName = PrimaryModel, int contextSize = 2048, int gpuLayers = 0)
    {
        var p = new LocalLlmProvider(_modelsDir, modelFileName, contextSize, gpuLayers, _logger);
        _providers.Add(p);
        return p;
    }

    private string WriteModelFile(string name, int bytes = 64)
    {
        Directory.CreateDirectory(_modelsDir);
        var path = Path.Combine(_modelsDir, name);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x42, bytes).ToArray());
        return path;
    }

    private static async IAsyncEnumerable<string> Tokens(
        [EnumeratorCancellation] CancellationToken ct = default, params string[] tokens)
    {
        foreach (var t in tokens)
        {
            await Task.Yield();
            yield return t;
        }
    }

    /// <summary>List-backed Serilog sink so warning-branch tests can assert on log events.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    // ─── Construction & identity ─────────────────────────────────────────────────

    [Fact]
    public void Ctor_guards_null_arguments()
    {
        FluentActions.Invoking(() => new LocalLlmProvider(null!, PrimaryModel, 2048, 0, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelsDirectory");
        FluentActions.Invoking(() => new LocalLlmProvider(_modelsDir, null!, 2048, 0, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelFileName");
        FluentActions.Invoking(() => new LocalLlmProvider(_modelsDir, PrimaryModel, 2048, 0, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Identity_and_initial_availability()
    {
        var p = NewProvider();
        p.ProviderId.Should().Be("local");
        p.DisplayName.Should().Be("Built-in LLM");
        p.IsAvailable.Should().BeFalse();
    }

    // ─── CheckConnectionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CheckConnection_missing_model_returns_false_without_loading()
    {
        var p = NewProvider();
        (await p.CheckConnectionAsync()).Should().BeFalse();
        p.IsAvailable.Should().BeFalse();
    }

    // ─── ListModelsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListModels_missing_directory_returns_empty()
    {
        (await NewProvider().ListModelsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ListModels_primary_model_carries_curated_metadata()
    {
        WriteModelFile(PrimaryModel, bytes: 128);
        var p = NewProvider(contextSize: 4096);

        var models = await p.ListModelsAsync();

        models.Should().HaveCount(1);
        var m = models[0];
        m.Id.Should().Be(PrimaryModel);
        m.Name.Should().Be("Llama 3.2 3B Instruct (Q4_K_M)");
        m.ProviderId.Should().Be("local");
        m.Family.Should().Be("llama");
        m.IsAvailable.Should().BeTrue();
        m.SizeBytes.Should().Be(128);
        m.QuantizationLevel.Should().Be("Q4_K_M");
        m.ContextLength.Should().Be(4096);
    }

    [Fact]
    public async Task ListModels_lists_extra_ggufs_once_without_duplicating_primary()
    {
        WriteModelFile(PrimaryModel);
        WriteModelFile("other-model.gguf", bytes: 32);
        var p = NewProvider();

        var models = await p.ListModelsAsync();

        models.Should().HaveCount(2);
        models.Select(m => m.Id).Should().BeEquivalentTo(PrimaryModel, "other-model.gguf");
        models.Single(m => m.Id == "other-model.gguf").Family.Should().Be("gguf");
        models.Single(m => m.Id == "other-model.gguf").Name.Should().Be("other-model");
    }

    // ─── DeleteModelAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_inactive_model_file()
    {
        var path = WriteModelFile("stale.gguf");
        await NewProvider().DeleteModelAsync("stale.gguf");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_active_model_unloads_and_removes()
    {
        var path = WriteModelFile(PrimaryModel);
        var p = NewProvider();

        await p.DeleteModelAsync(PrimaryModel);

        File.Exists(path).Should().BeFalse();
        p.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_missing_file_is_a_noop()
    {
        await NewProvider().DeleteModelAsync("never-existed.gguf"); // must not throw
    }

    // ─── PullModelAsync / download pipeline ──────────────────────────────────────

    [Fact]
    public async Task Pull_unknown_model_without_url_is_a_noop()
    {
        var p = NewProvider();
        await p.PullModelAsync("unknown-model.gguf");
        Directory.EnumerateFiles(_modelsDir).Should().BeEmpty();
    }

    [Fact]
    public void ResolveDownloadUrl_maps_known_models_and_rejects_unknown()
    {
        var method = typeof(LocalLlmProvider).GetMethod(
            "ResolveDownloadUrl", BindingFlags.NonPublic | BindingFlags.Static)!;
        string? Invoke(string name) => (string?)method.Invoke(null, new object[] { name });

        Invoke("llama-3.2-3b-instruct-q4_k_m.gguf").Should()
            .Be("https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf");
        Invoke("LLAMA-3.2-1B-INSTRUCT-Q4_K_M.GGUF").Should()
            .Be("https://huggingface.co/hugging-quants/Llama-3.2-1B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-1b-instruct-q4_k_m.gguf");
        Invoke("mystery.gguf").Should().BeNull();
    }

    /// <summary>Starts a localhost HttpListener on a free port (established bind-retry harness
    /// for the free-port TOCTOU flake) and serves exactly one request via the handler.</summary>
    private static (HttpListener Listener, string Url, Task Served) StartStub(
        Action<HttpListenerContext> handler)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var port = GetFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try { listener.Start(); }
            catch (HttpListenerException) { continue; }

            var served = Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                try { handler(ctx); }
                finally { try { ctx.Response.Close(); } catch { /* aborted responses */ } }
            });
            return (listener, $"http://localhost:{port}/model.gguf", served);
        }
        throw new InvalidOperationException("Could not bind an HttpListener stub after 5 attempts.");
    }

    private static int GetFreePort()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    [Fact]
    public async Task Pull_downloads_streams_progress_and_moves_part_file_atomically()
    {
        var payload = Enumerable.Repeat((byte)7, 1024).ToArray();
        var (listener, url, served) = StartStub(ctx =>
        {
            ctx.Response.ContentLength64 = payload.Length;
            ctx.Response.OutputStream.Write(payload);
        });
        using var _ = listener;

        var p = NewProvider();
        p.DownloadUrlResolver = _ => url;
        var reports = new ConcurrentQueue<ModelDownloadProgress>();
        var progress = new SynchronousProgress<ModelDownloadProgress>(reports.Enqueue);

        // The download itself must succeed; the trailing LoadModelAsync then fails on the
        // garbage GGUF (llama.cpp rejects the magic managed-side). That throw is expected
        // and is exactly the LoadModelAsync catch-arm we want covered.
        await FluentActions.Awaiting(() => p.PullModelAsync("target.gguf", progress))
            .Should().ThrowAsync<Exception>();

        var target = Path.Combine(_modelsDir, "target.gguf");
        File.Exists(target).Should().BeTrue();
        new FileInfo(target).Length.Should().Be(1024);
        File.Exists(target + ".part").Should().BeFalse();
        reports.Should().Contain(r => r.Status == "Complete" && r.CompletedBytes == 1024 && r.TotalBytes == 1024);
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_server_error_throws_and_leaves_no_partial()
    {
        var (listener, url, served) = StartStub(ctx => ctx.Response.StatusCode = 500);
        using var _ = listener;

        var p = NewProvider();
        p.DownloadUrlResolver = _ => url;

        await FluentActions.Awaiting(() => p.PullModelAsync("errored.gguf"))
            .Should().ThrowAsync<HttpRequestException>();

        Directory.EnumerateFiles(_modelsDir).Should().BeEmpty();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_aborted_mid_stream_cleans_partial_and_rethrows()
    {
        var (listener, url, served) = StartStub(ctx =>
        {
            ctx.Response.ContentLength64 = 4096;               // promise more than we send
            ctx.Response.OutputStream.Write(new byte[512]);
            ctx.Response.OutputStream.Flush();
            ctx.Response.Abort();                              // hard-kill mid-body
        });
        using var _ = listener;

        var p = NewProvider();
        p.DownloadUrlResolver = _ => url;

        await FluentActions.Awaiting(() => p.PullModelAsync("aborted.gguf"))
            .Should().ThrowAsync<Exception>(); // HttpIOException/IOException depending on stack

        File.Exists(Path.Combine(_modelsDir, "aborted.gguf")).Should().BeFalse();
        File.Exists(Path.Combine(_modelsDir, "aborted.gguf.part")).Should().BeFalse();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Inline IProgress — Progress&lt;T&gt; posts asynchronously and loses reports.</summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    // ─── StreamChatAsync / ChatAsync via InferenceOverride ───────────────────────

    private static List<ChatMessage> Msgs(params (string Role, string Content)[] items)
        => items.Select(i => new ChatMessage { Role = i.Role, Content = i.Content }).ToList();

    [Fact]
    public async Task StreamChat_formats_llama3_prompt_and_yields_override_tokens()
    {
        var p = NewProvider();
        string? capturedPrompt = null;
        InferenceParams? capturedParams = null;
        p.InferenceOverride = (prompt, prms, ct) =>
        {
            capturedPrompt = prompt;
            capturedParams = prms;
            return Tokens(ct, "Hello", " world");
        };

        var output = new List<string>();
        await foreach (var t in p.StreamChatAsync(Msgs(
            ("System", "Be brief"), ("user", "Hi"), ("ASSISTANT", "Yo"), ("tool", "data"))))
        {
            output.Add(t);
        }

        string.Concat(output).Should().Be("Hello world");
        capturedPrompt.Should().StartWith("<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\nBe brief<|eot_id|>");
        capturedPrompt.Should().Contain("<|start_header_id|>user<|end_header_id|>\n\nHi<|eot_id|>");
        capturedPrompt.Should().Contain("<|start_header_id|>assistant<|end_header_id|>\n\nYo<|eot_id|>");
        capturedPrompt.Should().Contain("<|start_header_id|>user<|end_header_id|>\n\ndata<|eot_id|>"); // unknown role -> user
        capturedPrompt.Should().EndWith("<|start_header_id|>assistant<|end_header_id|>\n\n");
        capturedParams!.MaxTokens.Should().Be(2048); // defaults with null options
        capturedParams.AntiPrompts.Should().Contain(new[] { "<|eot_id|>", "<|end_of_text|>" });
    }

    [Fact]
    public async Task StreamChat_json_mode_injects_instruction_and_primes_brace()
    {
        var p = NewProvider();
        string? capturedPrompt = null;
        p.InferenceOverride = (prompt, _, ct) => { capturedPrompt = prompt; return Tokens(ct, "{}"); };

        await foreach (var _ in p.StreamChatAsync(
            Msgs(("user", "give json")), new ChatOptions { ResponseFormat = ResponseFormat.JsonObject })) { }

        capturedPrompt.Should().Contain("You MUST respond with valid JSON only.");
        capturedPrompt.Should().EndWith("<|start_header_id|>assistant<|end_header_id|>\n\n{");
    }

    [Fact]
    public async Task StreamChat_maps_chat_options_to_inference_params()
    {
        var p = NewProvider();
        InferenceParams? captured = null;
        p.InferenceOverride = (_, prms, ct) => { captured = prms; return Tokens(ct, "x"); };

        await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")),
            new ChatOptions { MaxTokens = 64, Temperature = 0.2, TopP = 0.5 })) { }

        captured!.MaxTokens.Should().Be(64);
        var pipeline = captured.SamplingPipeline.Should().BeOfType<DefaultSamplingPipeline>().Subject;
        pipeline.Temperature.Should().BeApproximately(0.2f, 0.0001f);
        pipeline.TopP.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public async Task StreamChat_warns_when_token_budget_exhausted()
    {
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "a", "b");

        await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")),
            new ChatOptions { MaxTokens = 2 })) { }

        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Warning &&
            e.MessageTemplate.Text.Contains("likely truncated"));
    }

    [Fact]
    public async Task StreamChat_stops_yielding_after_cancellation_and_releases_lock()
    {
        var p = NewProvider();
        using var cts = new CancellationTokenSource();
        p.InferenceOverride = (_, _, _) => CancelAfterFirst(cts);

        var received = new List<string>();
        await foreach (var t in p.StreamChatAsync(Msgs(("user", "hi")), null, cts.Token))
        {
            received.Add(t);
        }

        received.Should().Equal("a"); // "b" arrives after cancel and must not surface

        // Lock must have been released by the finally — a second call proceeds.
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "again");
        (await p.ChatAsync(Msgs(("user", "hi")))).Should().Be("again");

        static async IAsyncEnumerable<string> CancelAfterFirst(CancellationTokenSource cts)
        {
            yield return "a";
            cts.Cancel();
            await Task.Yield();
            yield return "b";
        }
    }

    [Fact]
    public async Task Chat_concatenates_streamed_tokens()
    {
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "foo", "bar", "!");
        (await p.ChatAsync(Msgs(("user", "hi")))).Should().Be("foobar!");
    }

    // ─── Embeddings & model-load failure paths ───────────────────────────────────

    [Fact]
    public async Task Embeddings_without_model_file_throw_FileNotFound_and_mark_unavailable()
    {
        var p = NewProvider();

        await FluentActions.Awaiting(() => p.GenerateEmbeddingAsync("text", "model"))
            .Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Awaiting(() => p.GenerateEmbeddingsAsync(new[] { "a", "b" }, "model"))
            .Should().ThrowAsync<FileNotFoundException>();
        p.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task StreamChat_without_override_and_without_model_throws_FileNotFound()
    {
        var p = NewProvider();
        await FluentActions.Awaiting(async () =>
        {
            await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")))) { }
        }).Should().ThrowAsync<FileNotFoundException>();
    }

    // ─── Dispose semantics ───────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_is_idempotent_and_guards_every_entry_point()
    {
        var p = NewProvider();
        p.Dispose();
        p.Dispose(); // idempotent

        await FluentActions.Awaiting(() => p.CheckConnectionAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.ListModelsAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.PullModelAsync("x.gguf"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.DeleteModelAsync("x.gguf"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.GenerateEmbeddingAsync("t", "m"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(async () =>
        {
            await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")))) { }
        }).Should().ThrowAsync<ObjectDisposedException>();
    }

    // ─── GPU detection (environment-tolerant) ────────────────────────────────────

    [Fact]
    public void DetectRecommendedGpuLayers_returns_a_supported_tier()
    {
        var p = NewProvider();
        var method = typeof(LocalLlmProvider).GetMethod(
            "DetectRecommendedGpuLayers", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var layers = (int)method.Invoke(p, null)!;

        // Real WMI probe: 0 on CPU-only machines/CI, a fixed tier when an NVIDIA GPU exists.
        layers.Should().BeOneOf(0, 16, 28, 33);
    }
}
