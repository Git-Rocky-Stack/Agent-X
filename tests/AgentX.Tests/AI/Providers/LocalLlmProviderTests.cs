using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using AgentX.Core.AI;
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
/// Behavioural coverage for <see cref="LocalLlmProvider"/> - the LLamaSharp-backed offline
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

    // --- Construction & identity -------------------------------------------------

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

    // --- CheckConnectionAsync ----------------------------------------------------

    [Fact]
    public async Task CheckConnection_missing_model_returns_false_without_loading()
    {
        var p = NewProvider();
        (await p.CheckConnectionAsync()).Should().BeFalse();
        p.IsAvailable.Should().BeFalse();
    }

    // --- ListModelsAsync ---------------------------------------------------------

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

    // --- DeleteModelAsync --------------------------------------------------------

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

    // --- PullModelAsync / download pipeline --------------------------------------

    [Fact]
    public async Task Pull_unknown_model_throws_instead_of_reporting_success()
    {
        var p = NewProvider();

        await FluentActions.Awaiting(() => p.PullModelAsync("unknown-model.gguf"))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*unknown-model.gguf*");

        Directory.Exists(_modelsDir).Should().BeFalse("nothing may be written for an unknown model");
    }

    [Theory]
    [InlineData("..\\outside.gguf")]
    [InlineData("../outside.gguf")]
    [InlineData("sub/dir.gguf")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public async Task Pull_and_delete_reject_names_that_are_not_bare_gguf_files(string name)
    {
        var p = NewProvider();

        await FluentActions.Awaiting(() => p.PullModelAsync(name))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => p.DeleteModelAsync(name))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Catalog_maps_known_models_and_rejects_unknown()
    {
        BuiltInModelCatalog.Find("llama-3.2-3b-instruct-q4_k_m.gguf")!.DownloadUrl.Should()
            .Be("https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf");
        BuiltInModelCatalog.Find("LLAMA-3.2-1B-INSTRUCT-Q4_K_M.GGUF")!.DownloadUrl.Should()
            .Be("https://huggingface.co/hugging-quants/Llama-3.2-1B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-1b-instruct-q4_k_m.gguf");
        BuiltInModelCatalog.Find("mystery.gguf").Should().BeNull();
    }

    private static BuiltInModelSource StubSource(string fileName, string url, long minimumValidBytes = 1) =>
        new(fileName, fileName, url, ExpectedSizeBytes: 1024, MinimumValidBytes: minimumValidBytes, Sha256: null);

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
        p.DownloadSourceResolver = name => StubSource(name, url);
        var reports = new ConcurrentQueue<ModelDownloadProgress>();
        var progress = new SynchronousProgress<ModelDownloadProgress>(reports.Enqueue);

        // The pulled file is not the configured model, so the pull must complete without
        // loading (and failing on) the configured model afterwards.
        await p.PullModelAsync("target.gguf", progress);

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
        p.DownloadSourceResolver = name => StubSource(name, url);

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
        p.DownloadSourceResolver = name => StubSource(name, url);

        await FluentActions.Awaiting(() => p.PullModelAsync("aborted.gguf"))
            .Should().ThrowAsync<Exception>(); // HttpIOException/IOException depending on stack

        File.Exists(Path.Combine(_modelsDir, "aborted.gguf")).Should().BeFalse();
        File.Exists(Path.Combine(_modelsDir, "aborted.gguf.part")).Should().BeFalse();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_rejects_a_download_below_the_size_floor_and_publishes_nothing()
    {
        var (listener, url, served) = StartStub(ctx =>
        {
            ctx.Response.ContentLength64 = 64;
            ctx.Response.OutputStream.Write(new byte[64]);
        });
        using var _ = listener;

        var p = NewProvider();
        p.DownloadSourceResolver = name => StubSource(name, url, minimumValidBytes: 1_000_000);

        await FluentActions.Awaiting(() => p.PullModelAsync("tiny.gguf"))
            .Should().ThrowAsync<IOException>().WithMessage("*implausibly small*");

        File.Exists(Path.Combine(_modelsDir, "tiny.gguf")).Should().BeFalse();
        File.Exists(Path.Combine(_modelsDir, "tiny.gguf.part")).Should().BeFalse();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_verifies_a_configured_checksum_and_rejects_a_mismatch()
    {
        var (listener, url, served) = StartStub(ctx =>
        {
            ctx.Response.ContentLength64 = 32;
            ctx.Response.OutputStream.Write(new byte[32]);
        });
        using var _ = listener;

        var p = NewProvider();
        p.DownloadSourceResolver = name => StubSource(name, url) with { Sha256 = new string('0', 64) };

        await FluentActions.Awaiting(() => p.PullModelAsync("hashed.gguf"))
            .Should().ThrowAsync<IOException>().WithMessage("*checksum mismatch*");

        File.Exists(Path.Combine(_modelsDir, "hashed.gguf")).Should().BeFalse();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Inline IProgress - Progress&lt;T&gt; posts asynchronously and loses reports.</summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    // --- StreamChatAsync / ChatAsync via InferenceOverride -----------------------

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
    public async Task Chat_json_mode_returns_the_primed_brace_so_the_object_parses()
    {
        // The model continues after the primed "{", so its stream does NOT start with a brace.
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "\"score\"", ": 8", ", \"reason\": \"relevant\"", "}");

        var response = await p.ChatAsync(
            Msgs(("user", "rate it")), new ChatOptions { ResponseFormat = ResponseFormat.JsonObject });

        response.Should().Be("{\"score\": 8, \"reason\": \"relevant\"}");
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        using var doc = System.Text.Json.JsonDocument.Parse(response[start..(end + 1)]);
        doc.RootElement.GetProperty("score").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task Chat_json_mode_drops_a_duplicate_opening_brace_from_the_model()
    {
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, " {", "\"ok\": true}");

        var response = await p.ChatAsync(
            Msgs(("user", "json")), new ChatOptions { ResponseFormat = ResponseFormat.JsonObject });

        response.Should().Be("{ \"ok\": true}");
        using var doc = System.Text.Json.JsonDocument.Parse(response);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Chat_text_mode_does_not_add_a_brace()
    {
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "plain", " answer");

        (await p.ChatAsync(Msgs(("user", "hi")))).Should().Be("plain answer");
    }

    [Fact]
    public async Task StreamChat_drops_oldest_history_when_the_prompt_exceeds_the_local_context()
    {
        // 1 token per character keeps the arithmetic obvious: 512-token context, 128 reserved.
        var p = NewProvider(contextSize: 512);
        p.PromptTokenCounterOverride = text => text.Length;
        string? capturedPrompt = null;
        p.InferenceOverride = (prompt, _, ct) => { capturedPrompt = prompt; return Tokens(ct, "ok"); };

        var messages = Msgs(
            ("system", "Be brief"),
            ("user", "OLDEST " + new string('a', 200)),
            ("assistant", "MIDDLE " + new string('b', 200)),
            ("user", "LATEST question"));

        (await p.ChatAsync(messages, new ChatOptions { MaxTokens = 128, ContextWindow = 32_768 })).Should().Be("ok");

        capturedPrompt.Should().Contain("Be brief").And.Contain("LATEST question");
        capturedPrompt.Should().NotContain("OLDEST");
        capturedPrompt!.Length.Should().BeLessThanOrEqualTo(512 - 128);
    }

    [Fact]
    public async Task StreamChat_throws_a_clear_error_when_the_final_message_alone_overflows_the_context()
    {
        var p = NewProvider(contextSize: 256);
        p.PromptTokenCounterOverride = text => text.Length;
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "never");

        await FluentActions.Awaiting(() => p.ChatAsync(Msgs(("user", new string('x', 1000)))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*context*");
    }

    [Fact]
    public void ResolveChatModelFileName_uses_installed_ggufs_and_falls_back_to_the_configured_model()
    {
        WriteModelFile(PrimaryModel);
        WriteModelFile("llama-3.2-1b-instruct-q4_k_m.gguf");
        var p = NewProvider();

        p.ResolveChatModelFileName(null).Should().Be(PrimaryModel);
        p.ResolveChatModelFileName("LLAMA-3.2-3B-INSTRUCT-Q4_K_M.GGUF").Should().Be(PrimaryModel);
        p.ResolveChatModelFileName("llama-3.2-1b-instruct-q4_k_m.gguf").Should().Be("llama-3.2-1b-instruct-q4_k_m.gguf");
        p.ResolveChatModelFileName("llama3.2").Should().Be(PrimaryModel, "an Ollama tag is not a local model");
        p.ResolveChatModelFileName("missing.gguf").Should().Be(PrimaryModel);
        p.ResolveChatModelFileName("..\\llama-3.2-1b-instruct-q4_k_m.gguf").Should().Be(PrimaryModel);
    }

    [Fact]
    public async Task Dispose_during_a_stream_defers_release_and_the_stream_completes()
    {
        var p = NewProvider();
        var disposeNow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.InferenceOverride = (_, _, _) => DisposeMidStream();

        var received = new List<string>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var t in p.StreamChatAsync(Msgs(("user", "hi"))))
                received.Add(t);
        });

        await disposeNow.Task.WaitAsync(TimeSpan.FromSeconds(5));
        p.Dispose();
        await consumer.WaitAsync(TimeSpan.FromSeconds(5));

        received.Should().Equal(new[] { "before", "after" }, "dispose must not break the running stream");
        await FluentActions.Awaiting(() => p.ChatAsync(Msgs(("user", "again"))))
            .Should().ThrowAsync<ObjectDisposedException>();

        async IAsyncEnumerable<string> DisposeMidStream()
        {
            yield return "before";
            disposeNow.SetResult();
            await Task.Delay(50);
            yield return "after";
        }
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

        // Lock must have been released by the finally - a second call proceeds.
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

    // --- Embeddings & model-load failure paths -----------------------------------

    [Fact]
    public async Task Embeddings_without_model_file_throw_FileNotFound_and_mark_unavailable()
    {
        var p = NewProvider();

        await FluentActions.Awaiting(() => p.GenerateEmbeddingAsync("text", PrimaryModel))
            .Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Awaiting(() => p.GenerateEmbeddingsAsync(new[] { "a", "b" }, PrimaryModel))
            .Should().ThrowAsync<FileNotFoundException>();
        p.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Embeddings_reject_a_model_other_than_the_configured_one()
    {
        var p = NewProvider();
        p.EmbeddingOverride = (_, _) => Task.FromResult<IReadOnlyList<float[]>>(new[] { new[] { 1f } });

        await FluentActions.Awaiting(() => p.GenerateEmbeddingAsync("text", "all-minilm"))
            .Should().ThrowAsync<NotSupportedException>().WithMessage("*" + PrimaryModel + "*");

        (await p.GenerateEmbeddingAsync("text", PrimaryModel)).Should().Equal(1f);
        (await p.GenerateEmbeddingAsync("text", string.Empty)).Should().Equal(1f);
    }

    [Fact]
    public async Task Embeddings_are_input_dependent_rather_than_the_leading_token_vector()
    {
        // Simulates a runtime that returns one vector per token: the first (BOS) vector is the
        // same for every input in a causal model, so returning it made every chunk identical.
        var bos = new[] { 9f, 9f };
        var p = NewProvider();
        p.EmbeddingOverride = (text, _) =>
        {
            var vectors = new List<float[]> { bos };
            vectors.AddRange(text.Split(' ').Select(w => new[] { (float)w.Length, w[0] == 'a' ? 1f : 0f }));
            return Task.FromResult<IReadOnlyList<float[]>>(vectors);
        };

        var first = await p.GenerateEmbeddingAsync("alpha beta", PrimaryModel);
        var second = await p.GenerateEmbeddingAsync("gamma delta epsilon", PrimaryModel);

        first.Should().NotEqual(bos);
        second.Should().NotEqual(bos);
        first.Should().NotEqual(second);
        first.Should().Equal((9f + 5f + 4f) / 3f, (9f + 1f + 0f) / 3f); // mean over all token vectors
    }

    [Fact]
    public async Task Embeddings_are_serialized_through_the_single_embedder_context()
    {
        var p = NewProvider();
        var active = 0;
        var maxActive = 0;
        p.EmbeddingOverride = async (text, ct) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref active);
            return new[] { new[] { (float)text.Length } };
        };

        var calls = Enumerable.Range(0, 6)
            .Select(async i => i % 2 == 0
                ? await p.GenerateEmbeddingAsync(new string('q', i + 1), PrimaryModel)
                : (await p.GenerateEmbeddingsAsync(new[] { "index a", "index bb" }, PrimaryModel))[0])
            .ToArray();
        await Task.WhenAll(calls);

        maxActive.Should().Be(1, "background indexing and query embedding share one native context");

        static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                   Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    [Fact]
    public void PoolEmbeddings_returns_a_pooled_vector_and_rejects_empty_output()
    {
        var single = new[] { 1f, 2f };
        LocalLlmProvider.PoolEmbeddings(new[] { single }).Should().BeSameAs(single);
        LocalLlmProvider.PoolEmbeddings(new[] { new[] { 1f, 3f }, new[] { 3f, 5f } }).Should().Equal(2f, 4f);

        FluentActions.Invoking(() => LocalLlmProvider.PoolEmbeddings(Array.Empty<float[]>()))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => LocalLlmProvider.PoolEmbeddings(new[] { Array.Empty<float>() }))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => LocalLlmProvider.PoolEmbeddings(new[] { new[] { 1f }, new[] { 1f, 2f } }))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FitToTokenLimit_keeps_short_inputs_and_truncates_long_ones_to_the_limit()
    {
        static string[] Tokenize(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static string Decode(string[] tokens, int count) => string.Join(' ', tokens.Take(count));

        const string shortText = "one two three";
        LocalLlmProvider.FitToTokenLimit(shortText, 5, Tokenize, Decode).Should().BeSameAs(shortText);

        var longText = string.Join(' ', Enumerable.Range(0, 2000).Select(i => "w" + i));
        var fitted = LocalLlmProvider.FitToTokenLimit(longText, 1023, Tokenize, Decode);

        Tokenize(fitted).Should().HaveCount(1023);
        fitted.Should().StartWith("w0 w1 w2");
    }

    [Fact]
    public void FitToTokenLimit_rechecks_a_prefix_that_grows_when_re_tokenized()
    {
        // A decoder that adds a token on the way back forces the loop to shorten the prefix.
        static string[] Tokenize(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static string Decode(string[] tokens, int count) => string.Join(' ', tokens.Take(count)) + " extra";

        var fitted = LocalLlmProvider.FitToTokenLimit(
            string.Join(' ', Enumerable.Range(0, 100).Select(i => "t" + i)), 10, Tokenize, Decode);

        Tokenize(fitted).Length.Should().BeLessThanOrEqualTo(10);
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

    // --- Dispose semantics -------------------------------------------------------

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

    // --- GPU detection (environment-tolerant) ------------------------------------

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

    // The saved LocalGpuLayers: 0 (the default) is Automatic, so the provider detects an NVIDIA
    // GPU; a positive count is used as it is; a negative value keeps the model on the CPU. A
    // negative count used to reach llama.cpp as it was.

    [Fact]
    public void ResolveGpuLayers_Zero_IsAutomatic()
    {
        LocalLlmProvider.ResolveGpuLayers(0, () => 28).Should().Be(28);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(999)]
    public void ResolveGpuLayers_APositiveCount_IsUsedWithoutDetecting(int configured)
    {
        var detected = false;

        var layers = LocalLlmProvider.ResolveGpuLayers(configured, () => { detected = true; return 16; });

        layers.Should().Be(configured);
        detected.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-40)]
    public void ResolveGpuLayers_ANegativeValue_KeepsEveryLayerOnTheCpu(int configured)
    {
        var detected = false;

        var layers = LocalLlmProvider.ResolveGpuLayers(configured, () => { detected = true; return 33; });

        layers.Should().Be(0);
        detected.Should().BeFalse();
    }
}
