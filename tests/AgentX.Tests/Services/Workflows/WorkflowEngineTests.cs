using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Workflows;

/// <summary>
/// End-to-end coverage for <see cref="WorkflowEngine"/>. Each test drives the real
/// engine against an in-memory SQLite <see cref="AgentXDbContext"/> (so run-history
/// persistence and the WorkflowRun -> Workflow foreign key are exercised for real),
/// while the AI service, RAG pipeline, and workflow store are mocked with Moq.
///
/// The engine reads steps from the mocked <see cref="IWorkflowService"/>; the database
/// only needs a seeded workflow row to satisfy the required foreign key on each run.
/// </summary>
public sealed class WorkflowEngineTests
{
    private const string DefaultModel = "active-model";

    // ---------------------------------------------------------------------
    // Test harness
    // ---------------------------------------------------------------------

    private sealed class WorkflowEngineHarness : IDisposable
    {
        public TestDbContextFactory DbFactory { get; } = new();
        public Mock<IWorkflowService> WorkflowService { get; } = new();
        public Mock<IAiService> AiService { get; } = new();
        public Mock<IRagPipeline> RagPipeline { get; } = new();
        public AgentXDbContext Db { get; }
        public WorkflowEngine Engine { get; }

        /// <summary>StepCompleted events captured in order.</summary>
        public List<WorkflowStepResult> Events { get; } = new();

        public WorkflowEngineHarness()
        {
            Db = DbFactory.CreateContext();

            // Silent Serilog logger - no sinks configured, so log calls are no-ops.
            ILogger logger = new LoggerConfiguration().CreateLogger();

            AiService.SetupGet(s => s.ActiveModelId).Returns(DefaultModel);
            AiService
                .Setup(s => s.ChatAsync(
                    It.IsAny<IReadOnlyList<ChatMessage>>(),
                    It.IsAny<string?>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("AI_OUTPUT");

            RagPipeline
                .Setup(r => r.AskAsync(
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RagResponse { AnswerText = "RAG_ANSWER" });

            WorkflowService
                .Setup(s => s.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .Returns(Task.CompletedTask);

            // Unconfigured workflow ids resolve to "not found".
            WorkflowService
                .Setup(s => s.GetWorkflowAsync(It.IsAny<long>()))
                .ReturnsAsync((WorkflowEntity?)null);

            Engine = new WorkflowEngine(
                WorkflowService.Object, AiService.Object, RagPipeline.Object, Db, logger);

            Engine.StepCompleted += (_, result) => Events.Add(result);
        }

        /// <summary>
        /// Seeds a workflow row (to satisfy the run foreign key) and wires the mocked
        /// store to return a detached entity carrying the supplied steps.
        /// </summary>
        public long ConfigureWorkflow(string name, params WorkflowStepEntity[] steps)
        {
            long id;
            using (var seed = DbFactory.CreateContext())
            {
                var row = new WorkflowEntity
                {
                    Name = name,
                    Category = "Custom",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                seed.Workflows.Add(row);
                seed.SaveChanges();
                id = row.Id;
            }

            var detached = new WorkflowEntity
            {
                Id = id,
                Name = name,
                Category = "Custom",
                Steps = steps.ToList(),
            };

            WorkflowService.Setup(s => s.GetWorkflowAsync(id)).ReturnsAsync(detached);
            return id;
        }

        /// <summary>Reads the most recently persisted run from a fresh context.</summary>
        public WorkflowRunEntity LastRun()
        {
            using var verify = DbFactory.CreateContext();
            return verify.WorkflowRuns.OrderByDescending(r => r.Id).First();
        }

        public int RunCount()
        {
            using var verify = DbFactory.CreateContext();
            return verify.WorkflowRuns.Count();
        }

        public void Dispose()
        {
            Db.Dispose();
            DbFactory.Dispose();
        }
    }

    private static WorkflowStepEntity Step(
        string type,
        int order = 0,
        string name = "Step",
        string template = "",
        string? config = null,
        string? model = null,
        double? temperature = null,
        int? maxTokens = null) => new()
        {
            StepType = type,
            StepOrder = order,
            Name = name,
            PromptTemplate = template,
            ConfigJson = config,
            ModelOverride = model,
            TemperatureOverride = temperature,
            MaxTokensOverride = maxTokens,
        };

    private sealed class RecordingProgress : IProgress<WorkflowStepResult>
    {
        public List<WorkflowStepResult> Reports { get; } = new();
        public void Report(WorkflowStepResult value) => Reports.Add(value);
    }

    // ---------------------------------------------------------------------
    // Constructor guards
    // ---------------------------------------------------------------------

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        using var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        var ws = new Mock<IWorkflowService>().Object;
        var ai = new Mock<IAiService>().Object;
        var rag = new Mock<IRagPipeline>().Object;
        ILogger log = new LoggerConfiguration().CreateLogger();

        ((Action)(() => new WorkflowEngine(null!, ai, rag, db, log)))
            .Should().Throw<ArgumentNullException>().WithParameterName("workflowService");
        ((Action)(() => new WorkflowEngine(ws, null!, rag, db, log)))
            .Should().Throw<ArgumentNullException>().WithParameterName("aiService");
        ((Action)(() => new WorkflowEngine(ws, ai, null!, db, log)))
            .Should().Throw<ArgumentNullException>().WithParameterName("ragPipeline");
        ((Action)(() => new WorkflowEngine(ws, ai, rag, null!, log)))
            .Should().Throw<ArgumentNullException>().WithParameterName("db");
        ((Action)(() => new WorkflowEngine(ws, ai, rag, db, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ---------------------------------------------------------------------
    // Pre-execution guards
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task ExecuteWorkflowAsync_rejects_blank_input(string input)
    {
        using var harness = new WorkflowEngineHarness();

        await harness.Engine.Invoking(e => e.ExecuteWorkflowAsync(1, input))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must not be empty*");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_throws_when_workflow_not_found()
    {
        using var harness = new WorkflowEngineHarness();

        await harness.Engine.Invoking(e => e.ExecuteWorkflowAsync(999, "input"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999 not found*");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_throws_when_workflow_has_no_steps()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("empty");

        await harness.Engine.Invoking(e => e.ExecuteWorkflowAsync(id, "input"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has no steps*");

        // No run record should be created when the workflow is rejected up front.
        harness.RunCount().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_rejects_concurrent_execution()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf", Step("AiPrompt", template: "{{input}}"));

        // Block the AI call so the first execution stays in-flight.
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(gate.Task);

        var first = harness.Engine.ExecuteWorkflowAsync(id, "input");

        for (var i = 0; i < 200 && !harness.Engine.IsRunning; i++)
        {
            await Task.Delay(10);
        }

        harness.Engine.IsRunning.Should().BeTrue();

        await harness.Engine.Invoking(e => e.ExecuteWorkflowAsync(id, "second"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already being executed*");

        gate.SetResult("done");
        var result = await first;

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be("done");
        harness.Engine.IsRunning.Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Happy path + persistence
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ExecuteWorkflowAsync_completes_single_step_and_persists_run()
    {
        using var harness = new WorkflowEngineHarness();
        var progress = new RecordingProgress();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", name: "Generate", template: "Echo: {{input}}"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "hello", progress);

        result.Success.Should().BeTrue();
        result.WorkflowName.Should().Be("wf");
        result.FinalOutput.Should().Be("AI_OUTPUT");
        result.Steps.Should().ContainSingle();
        result.Steps[0].Success.Should().BeTrue();
        result.Steps[0].StepName.Should().Be("Generate");
        result.TotalTokensUsed.Should().BeGreaterThan(0);
        result.TotalDurationMs.Should().BeGreaterThanOrEqualTo(0);

        // Progress + event hooks both fired exactly once with the step result.
        progress.Reports.Should().ContainSingle().Which.StepName.Should().Be("Generate");
        harness.Events.Should().ContainSingle().Which.StepName.Should().Be("Generate");

        // Run history persisted as completed.
        var run = harness.LastRun();
        run.Status.Should().Be("completed");
        run.StepsCompleted.Should().Be(1);
        run.TotalSteps.Should().Be(1);
        run.InitialInput.Should().Be("hello");
        run.FinalOutput.Should().Be("AI_OUTPUT");
        run.TotalTokensUsed.Should().BeGreaterThan(0);
        run.StepOutputsJson.Should().NotBeNullOrEmpty();
        run.CompletedAt.Should().NotBeNull();

        // Engine bumps the workflow's run count via the store.
        harness.WorkflowService.Verify(
            s => s.UpdateWorkflowAsync(It.Is<WorkflowEntity>(w => w.RunCount == 1)), Times.Once);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_chains_output_and_resolves_templates()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("chain",
            Step("AiPrompt", order: 0, name: "First", template: "S0:{{input}}"),
            Step("AiPrompt", order: 1, name: "Second", template: "S1:{{input}}|{{previous_output}}"));

        var prompts = new List<string>();
        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> m, string? _, ChatOptions? _, CancellationToken _) =>
            {
                prompts.Add(m[0].Content);
                return Task.FromResult($"OUT{prompts.Count - 1}");
            });

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "myinput");

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(2);
        result.FinalOutput.Should().Be("OUT1");

        prompts.Should().HaveCount(2);
        prompts[0].Should().Be("S0:myinput");
        prompts[1].Should().Be("S1:myinput|OUT0");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_executes_steps_in_step_order()
    {
        using var harness = new WorkflowEngineHarness();
        // Provided out of order; engine must sort by StepOrder.
        var id = harness.ConfigureWorkflow("ordered",
            Step("AiPrompt", order: 2, name: "Third", template: "{{previous_output}}"),
            Step("AiPrompt", order: 0, name: "First", template: "{{input}}"),
            Step("AiPrompt", order: 1, name: "Second", template: "{{previous_output}}"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "go");

        result.Steps.Select(s => s.StepName)
            .Should().ContainInOrder("First", "Second", "Third");
    }

    // ---------------------------------------------------------------------
    // AiPrompt step
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AiPrompt_applies_model_temperature_and_token_overrides()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", template: "{{input}}", model: "model-x", temperature: 0.3, maxTokens: 123));

        ChatOptions? captured = null;
        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<ChatMessage> _, string? _, ChatOptions? o, CancellationToken _) => captured = o)
            .ReturnsAsync("OUT");

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        captured.Should().NotBeNull();
        captured!.ModelId.Should().Be("model-x");
        captured.Temperature.Should().Be(0.3);
        captured.MaxTokens.Should().Be(123);
        result.Steps[0].ModelUsed.Should().Be("model-x");
    }

    [Fact]
    public async Task AiPrompt_without_overrides_reports_active_model_and_default_options()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf", Step("AiPrompt", template: "{{input}}"));

        ChatOptions? captured = null;
        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<ChatMessage> _, string? _, ChatOptions? o, CancellationToken _) => captured = o)
            .ReturnsAsync("OUT");

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        captured.Should().NotBeNull();
        captured!.ModelId.Should().BeNull();
        result.Steps[0].ModelUsed.Should().Be(DefaultModel);
    }

    [Fact]
    public async Task AiPrompt_with_empty_resolved_prompt_fails_step_and_run()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", name: "Blank", template: ""));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Contain("empty after placeholder substitution");

        var run = harness.LastRun();
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Contain("Blank");
        run.ErrorMessage.Should().Contain("empty after placeholder substitution");
        run.StepsCompleted.Should().Be(0);

        // The AI service must not be called when the prompt resolves to empty.
        harness.AiService.Verify(s => s.ChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(),
            It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------
    // DocumentLookup step
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DocumentLookup_queries_rag_with_collection_from_config()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { collectionId = 42L });
        var id = harness.ConfigureWorkflow("wf",
            Step("DocumentLookup", template: "find {{input}}", config: config));

        string? capturedQuery = null;
        long? capturedCollection = null;
        harness.RagPipeline
            .Setup(r => r.AskAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback((string q, long? cid, Action<string>? _, bool _, CancellationToken _) =>
            {
                capturedQuery = q;
                capturedCollection = cid;
            })
            .ReturnsAsync(new RagResponse { AnswerText = "DOC_ANSWER" });

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "neurons");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be("DOC_ANSWER");
        result.Steps[0].ModelUsed.Should().Be(DefaultModel);
        capturedQuery.Should().Be("find neurons");
        capturedCollection.Should().Be(42L);
    }

    [Fact]
    public async Task DocumentLookup_without_config_passes_null_collection()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("DocumentLookup", template: "{{input}}"));

        long? capturedCollection = 7L;
        harness.RagPipeline
            .Setup(r => r.AskAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, long? cid, Action<string>? _, bool _, CancellationToken _) => capturedCollection = cid)
            .ReturnsAsync(new RagResponse { AnswerText = "A" });

        await harness.Engine.ExecuteWorkflowAsync(id, "q");

        capturedCollection.Should().BeNull();
    }

    [Fact]
    public async Task DocumentLookup_with_config_missing_collection_passes_null()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { somethingElse = "x" });
        var id = harness.ConfigureWorkflow("wf",
            Step("DocumentLookup", template: "{{input}}", config: config));

        long? capturedCollection = 7L;
        harness.RagPipeline
            .Setup(r => r.AskAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, long? cid, Action<string>? _, bool _, CancellationToken _) => capturedCollection = cid)
            .ReturnsAsync(new RagResponse { AnswerText = "A" });

        await harness.Engine.ExecuteWorkflowAsync(id, "q");

        capturedCollection.Should().BeNull();
    }

    [Fact]
    public async Task DocumentLookup_with_invalid_config_json_still_succeeds()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("DocumentLookup", template: "{{input}}", config: "{ not valid json"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "q");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be("RAG_ANSWER");
    }

    [Fact]
    public async Task DocumentLookup_with_empty_query_fails_step()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("DocumentLookup", name: "Lookup", template: ""));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "q");

        result.Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Contain("query was empty");
        harness.RagPipeline.Verify(r => r.AskAsync(
            It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<Action<string>?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------
    // TextTransform step
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("uppercase", "abc", "ABC")]
    [InlineData("lowercase", "ABC", "abc")]
    [InlineData("titlecase", "hello world", "Hello World")]
    [InlineData("trim", "  hi  ", "hi")]
    [InlineData("word_count", "one two three", "Word count: 3")]
    [InlineData("char_count", "hello", "Character count: 5")]
    public async Task TextTransform_applies_single_line_transforms(string transform, string input, string expected)
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { transform });
        var id = harness.ConfigureWorkflow("wf",
            Step("TextTransform", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, input);

        result.Success.Should().BeTrue();
        result.Steps[0].Output.Should().Be(expected);
        result.Steps[0].TokensUsed.Should().Be(0);
    }

    [Fact]
    public async Task TextTransform_handles_multi_line_transforms()
    {
        using var harness = new WorkflowEngineHarness();
        var nl = Environment.NewLine;

        async Task<string> Transform(string transform, string input)
        {
            using var h = new WorkflowEngineHarness();
            var config = JsonSerializer.Serialize(new { transform });
            var id = h.ConfigureWorkflow("wf", Step("TextTransform", template: "{{input}}", config: config));
            var r = await h.Engine.ExecuteWorkflowAsync(id, input);
            r.Success.Should().BeTrue();
            return r.Steps[0].Output;
        }

        (await Transform("extract_lines", "a\n\nb\n")).Should().Be(string.Join(nl, "a", "b"));
        (await Transform("reverse_lines", "a\nb\nc")).Should().Be(string.Join(nl, "c", "b", "a"));
        (await Transform("deduplicate_lines", "a\na\nb")).Should().Be(string.Join(nl, "a", "b"));
        (await Transform("sort_lines", "b\na\nc")).Should().Be(string.Join(nl, "a", "b", "c"));
        (await Transform("number_lines", "x\ny")).Should().Be(string.Join(nl, "1. x", "2. y"));
    }

    [Fact]
    public async Task TextTransform_defaults_to_uppercase_without_config()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("TextTransform", template: "{{input}}"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "abc");

        result.Steps[0].Output.Should().Be("ABC");
    }

    [Fact]
    public async Task TextTransform_falls_back_to_previous_output_when_template_blank()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", order: 0, name: "Gen", template: "{{input}}"),
            Step("TextTransform", order: 1, name: "Upper", template: "",
                config: JsonSerializer.Serialize(new { transform = "uppercase" })));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("prev-out");

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "ignored");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be("PREV-OUT");
    }

    [Fact]
    public async Task TextTransform_with_unsupported_transform_fails_step()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { transform = "rot13" });
        var id = harness.ConfigureWorkflow("wf",
            Step("TextTransform", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "abc");

        result.Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Contain("Unsupported transform type 'rot13'");
    }

    [Fact]
    public async Task TextTransform_with_invalid_config_json_defaults_to_uppercase()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("TextTransform", template: "{{input}}", config: "{ bad json"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "abc");

        result.Success.Should().BeTrue();
        result.Steps[0].Output.Should().Be("ABC");
    }

    // ---------------------------------------------------------------------
    // ConditionalBranch step
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("contains", "world", "hello world", true)]
    [InlineData("not_contains", "xyz", "hello", true)]
    [InlineData("starts_with", "he", "hello", true)]
    [InlineData("ends_with", "lo", "hello", true)]
    [InlineData("equals", "HELLO", "hello", true)]
    [InlineData("matches", "\\d+", "abc123", true)]
    [InlineData("length_greater_than", "3", "hello", true)]
    [InlineData("contains", "zzz", "hello", false)]
    [InlineData("length_greater_than", "10", "hello", false)]
    [InlineData("unknown_op", "x", "hello", false)]
    public async Task ConditionalBranch_evaluates_conditions(
        string condition, string value, string previous, bool expectMet)
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new
        {
            condition,
            value,
            trueBranch = "YES",
            falseBranch = "NO",
        });
        var id = harness.ConfigureWorkflow("wf",
            // Step 0 seeds previousOutput; step 1 branches on it.
            Step("AiPrompt", order: 0, name: "Seed", template: "{{input}}"),
            Step("ConditionalBranch", order: 1, name: "Branch", config: config));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "seed");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be(expectMet ? "YES" : "NO");
    }

    [Fact]
    public async Task ConditionalBranch_resolves_templates_in_branches()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new
        {
            condition = "contains",
            value = "match",
            trueBranch = "input was {{input}}",
            falseBranch = "no",
        });
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", order: 0, name: "Seed", template: "{{input}}"),
            Step("ConditionalBranch", order: 1, name: "Branch", config: config));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("this is a match");

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "ORIGINAL");

        result.FinalOutput.Should().Be("input was ORIGINAL");
    }

    [Fact]
    public async Task ConditionalBranch_defaults_branches_to_previous_output()
    {
        using var harness = new WorkflowEngineHarness();
        // No trueBranch/falseBranch -> both default to previous output.
        var config = JsonSerializer.Serialize(new { condition = "contains", value = "x" });
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", order: 0, name: "Seed", template: "{{input}}"),
            Step("ConditionalBranch", order: 1, name: "Branch", config: config));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("no match here");

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "seed");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be("no match here");
    }

    [Fact]
    public async Task ConditionalBranch_without_config_fails_step()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("ConditionalBranch", name: "Branch"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Contain("requires ConfigJson");
    }

    [Fact]
    public async Task ConditionalBranch_with_invalid_config_json_fails_step()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("ConditionalBranch", name: "Branch", config: "{ broken"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Contain("Failed to parse ConditionalBranch ConfigJson");
    }

    // ---------------------------------------------------------------------
    // OutputFormat step
    // ---------------------------------------------------------------------

    [Fact]
    public async Task OutputFormat_wraps_as_json()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { format = "json" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "payload");

        result.Steps[0].Output.Should().Contain("\"output\"");
        result.Steps[0].Output.Should().Contain("payload");
    }

    [Fact]
    public async Task OutputFormat_wraps_as_markdown()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { format = "markdown" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", name: "Title", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "body");

        result.Steps[0].Output.Should().StartWith("# Title");
        result.Steps[0].Output.Should().Contain("body");
    }

    [Fact]
    public async Task OutputFormat_wraps_as_html_and_escapes()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { format = "html" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", name: "Doc", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "<b>&");

        result.Steps[0].Output.Should().Contain("<h2>Doc</h2>");
        result.Steps[0].Output.Should().Contain("&lt;b&gt;&amp;");
    }

    [Fact]
    public async Task OutputFormat_converts_to_bullet_and_numbered_lists()
    {
        using var harness = new WorkflowEngineHarness();

        async Task<string> Format(string format)
        {
            using var h = new WorkflowEngineHarness();
            var config = JsonSerializer.Serialize(new { format });
            var id = h.ConfigureWorkflow("wf", Step("OutputFormat", template: "{{input}}", config: config));
            var r = await h.Engine.ExecuteWorkflowAsync(id, "alpha\nbeta");
            return r.Steps[0].Output;
        }

        var bullets = await Format("bullet_list");
        bullets.Should().Contain("- alpha").And.Contain("- beta");

        var numbered = await Format("numbered_list");
        numbered.Should().Contain("1. alpha").And.Contain("2. beta");
    }

    [Fact]
    public async Task OutputFormat_applies_prefix_and_suffix()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { prefix = ">> ", suffix = " <<" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "core");

        result.Steps[0].Output.Should().Be(">> core <<");
    }

    [Fact]
    public async Task OutputFormat_with_unknown_format_passes_through()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { format = "xml" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "raw");

        result.Steps[0].Output.Should().Be("raw");
    }

    [Fact]
    public async Task OutputFormat_with_empty_format_skips_formatting()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { format = "" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "raw");

        result.Steps[0].Output.Should().Be("raw");
    }

    [Fact]
    public async Task OutputFormat_without_template_uses_previous_output()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", order: 0, name: "Gen", template: "{{input}}"),
            Step("OutputFormat", order: 1, name: "Fmt", template: ""));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("prev");

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "ignored");

        result.FinalOutput.Should().Be("prev");
    }

    [Fact]
    public async Task OutputFormat_with_invalid_config_json_passes_through()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", template: "{{input}}", config: "{ broken json"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "raw");

        result.Success.Should().BeTrue();
        result.Steps[0].Output.Should().Be("raw");
    }

    // ---------------------------------------------------------------------
    // Unknown step type + error handling
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UnknownStepType_fails_step_and_run()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("Telekinesis", name: "Weird"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Contain("Unknown step type 'Telekinesis'");
        harness.LastRun().Status.Should().Be("failed");
    }

    [Fact]
    public async Task Step_throwing_generic_exception_is_captured_as_failed_step()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", name: "Boom", template: "{{input}}"));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ai-boom"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.Steps[0].Success.Should().BeFalse();
        result.Steps[0].ErrorMessage.Should().Be("ai-boom");

        var run = harness.LastRun();
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Contain("ai-boom");
    }

    [Fact]
    public async Task Unexpected_exception_after_steps_marks_run_failed()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", name: "Gen", template: "{{input}}"));

        // The run-count update happens after all steps succeed, inside the guarded block.
        harness.WorkflowService
            .Setup(s => s.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()))
            .ThrowsAsync(new InvalidOperationException("update-boom"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.FinalOutput.Should().Be("AI_OUTPUT");

        var run = harness.LastRun();
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Be("update-boom");
    }

    // ---------------------------------------------------------------------
    // Cancellation
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ExecuteWorkflowAsync_with_precancelled_token_cancels_before_first_step()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", template: "{{input}}"));

        var result = await harness.Engine.ExecuteWorkflowAsync(
            id, "in", progress: null, ct: new CancellationToken(canceled: true));

        result.Success.Should().BeFalse();
        result.Steps.Should().BeEmpty();

        var run = harness.LastRun();
        run.Status.Should().Be("cancelled");
        run.StepsCompleted.Should().Be(0);
        run.StepOutputsJson.Should().BeNull();

        // Run count still advances even for a cancelled run.
        harness.WorkflowService.Verify(
            s => s.UpdateWorkflowAsync(It.Is<WorkflowEntity>(w => w.RunCount == 1)), Times.Once);
    }

    [Fact]
    public async Task CancelExecutionAsync_mid_run_stops_before_next_step()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", order: 0, name: "First", template: "{{input}}"),
            Step("AiPrompt", order: 1, name: "Second", template: "{{previous_output}}"));

        // The first AI call requests cancellation; the engine should stop before step 1.
        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<ChatMessage> _, string? _, ChatOptions? _, CancellationToken _) =>
            {
                await harness.Engine.CancelExecutionAsync();
                return "OUT0";
            });

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.FinalOutput.Should().Be("OUT0");

        var run = harness.LastRun();
        run.Status.Should().Be("cancelled");
        run.StepsCompleted.Should().Be(1);
        run.FinalOutput.Should().Be("OUT0");
        run.StepOutputsJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Step_timing_out_without_a_cancel_request_fails_the_run_instead_of_cancelling_it()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", name: "Gen", template: "{{input}}"));

        // HttpClient reports its own timeout as TaskCanceledException while nobody asked to
        // cancel; that is a failed step, not a user cancellation.
        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.WasCancelled.Should().BeFalse();
        result.Steps.Should().ContainSingle().Which.ErrorMessage.Should().Contain("timed out");

        var run = harness.LastRun();
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task Step_cancelled_through_the_engine_marks_the_run_cancelled_and_reports_it()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("AiPrompt", name: "Gen", template: "{{input}}"));

        harness.AiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<ChatMessage> _, string? _, ChatOptions? _, CancellationToken ct) =>
            {
                await harness.Engine.CancelExecutionAsync();
                ct.ThrowIfCancellationRequested();
                return "never";
            });

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeFalse();
        result.WasCancelled.Should().BeTrue();
        harness.LastRun().Status.Should().Be("cancelled");
        harness.Engine.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Precancelled_run_reports_it_was_cancelled()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf", Step("AiPrompt", template: "{{input}}"));

        var result = await harness.Engine.ExecuteWorkflowAsync(
            id, "in", progress: null, ct: new CancellationToken(canceled: true));

        result.WasCancelled.Should().BeTrue();
    }

    // ---- Engine lock, interrupted runs, templates, formatting and regex safety ----

    [Fact]
    public async Task Failed_save_of_the_run_record_does_not_lock_the_engine()
    {
        using var harness = new WorkflowEngineHarness();

        // A workflow the store returns but the database has no row for: inserting its run
        // violates the foreign key, so the very first save fails.
        harness.WorkflowService
            .Setup(s => s.GetWorkflowAsync(555))
            .ReturnsAsync(new WorkflowEntity
            {
                Id = 555,
                Name = "ghost",
                Category = "Custom",
                Steps = [Step("AiPrompt", template: "{{input}}")],
            });

        await harness.Engine.Invoking(e => e.ExecuteWorkflowAsync(555, "in"))
            .Should().ThrowAsync<Exception>();

        harness.Engine.IsRunning.Should().BeFalse("a failed save must release the engine");

        var id = harness.ConfigureWorkflow("wf", Step("AiPrompt", template: "{{input}}"));
        var result = await harness.Engine.ExecuteWorkflowAsync(id, "in");

        result.Success.Should().BeTrue("the next run starts normally instead of reporting 'already being executed'");
    }

    [Fact]
    public async Task First_execution_reconciles_runs_interrupted_by_a_previous_session_once()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf", Step("AiPrompt", template: "{{input}}"));

        await harness.Engine.ExecuteWorkflowAsync(id, "one");
        await harness.Engine.ExecuteWorkflowAsync(id, "two");

        harness.WorkflowService.Verify(s => s.ReconcileInterruptedRunsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Templates_are_resolved_in_one_pass_so_user_text_is_never_rescanned()
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", order: 0, template: "first"),
            Step("OutputFormat", order: 1, template: "[{{input}}] [{{previous_output}}]"));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "literal {{previous_output}} and {{input}}");

        result.FinalOutput.Should().Be("[literal {{previous_output}} and {{input}}] [first]");
    }

    [Fact]
    public async Task OutputFormat_html_emits_one_line_break_per_newline()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new { format = "html" });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", name: "Doc", template: "{{input}}", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, "a\r\nb\nc\rd");

        result.Steps[0].Output.Should().Contain("<p>a<br/>\nb<br/>\nc<br/>\nd</p>");
        result.Steps[0].Output.Should().NotContain("<br/><br/>");
    }

    [Fact]
    public async Task ConditionalBranch_matches_with_a_runaway_pattern_fails_the_step_instead_of_hanging()
    {
        using var harness = new WorkflowEngineHarness();
        var config = JsonSerializer.Serialize(new
        {
            condition = "matches",
            value = "^(a+)+$",
            trueBranch = "yes",
            falseBranch = "no",
        });
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", order: 0, template: "{{input}}"),
            Step("ConditionalBranch", order: 1, name: "Branch", config: config));

        var result = await harness.Engine.ExecuteWorkflowAsync(id, new string('a', 64) + "!");

        result.Success.Should().BeFalse();
        result.Steps[^1].ErrorMessage.Should().Contain("longer than");
    }

    [Fact]
    public async Task CancelExecutionAsync_is_a_noop_when_nothing_is_running()
    {
        using var harness = new WorkflowEngineHarness();

        await harness.Engine.Invoking(e => e.CancelExecutionAsync())
            .Should().NotThrowAsync();
        harness.Engine.IsRunning.Should().BeFalse();
    }

    // ---- Agreement with the settings check the workflow builder runs (WorkflowStepSettings) ----

    /// <summary>
    /// Runs <paramref name="stepType"/> with <paramref name="configJson"/> after a seed step that
    /// outputs the input, so a conditional branch has a previous output to test.
    /// </summary>
    private static async Task<WorkflowRunResult> RunCheckedStepAsync(string stepType, string configJson, string input)
    {
        using var harness = new WorkflowEngineHarness();
        var id = harness.ConfigureWorkflow("wf",
            Step("OutputFormat", order: 0, name: "Seed", template: "{{input}}"),
            Step(stepType, order: 1, name: "Checked", template: "{{input}}", config: configJson));

        return await harness.Engine.ExecuteWorkflowAsync(id, input);
    }

    [Fact]
    public async Task Every_transform_the_settings_check_accepts_is_applied_by_the_engine()
    {
        foreach (var transform in WorkflowStepSettings.TextTransforms)
        {
            var config = JsonSerializer.Serialize(new { transform });
            WorkflowStepSettings.Validate("TextTransform", config).Should().BeNull();

            var result = await RunCheckedStepAsync("TextTransform", config, "b\na\na");

            result.Success.Should().BeTrue(transform);
        }

        // And the engine supports no transform the check would reject: its refusal lists them all.
        var refused = await RunCheckedStepAsync("TextTransform", "{\"transform\": \"rot13\"}", "abc");
        var supported = refused.Steps[^1].ErrorMessage!.Split("Supported: ")[1].TrimEnd('.').Split(", ");
        supported.Should().BeEquivalentTo(WorkflowStepSettings.TextTransforms);
    }

    [Fact]
    public async Task Every_condition_the_settings_check_accepts_is_evaluated_by_the_engine()
    {
        // For each condition, a value that makes it true for the previous output "abc".
        var metBy = new Dictionary<string, string>
        {
            ["contains"] = "b",
            ["not_contains"] = "z",
            ["starts_with"] = "a",
            ["ends_with"] = "c",
            ["equals"] = "ABC",
            ["matches"] = "^a.c$",
            ["length_greater_than"] = "2",
        };
        metBy.Keys.Should().BeEquivalentTo(WorkflowStepSettings.Conditions);

        foreach (var (condition, value) in metBy)
        {
            var config = JsonSerializer.Serialize(new { condition, value, trueBranch = "YES", falseBranch = "NO" });
            WorkflowStepSettings.Validate("ConditionalBranch", config).Should().BeNull();

            var result = await RunCheckedStepAsync("ConditionalBranch", config, "abc");

            result.FinalOutput.Should().Be("YES", condition);
        }
    }

    [Fact]
    public async Task Every_format_the_settings_check_accepts_is_applied_by_the_engine()
    {
        const string input = "line one\nline two";
        foreach (var format in WorkflowStepSettings.OutputFormats)
        {
            var config = JsonSerializer.Serialize(new { format });
            WorkflowStepSettings.Validate("OutputFormat", config).Should().BeNull();

            var result = await RunCheckedStepAsync("OutputFormat", config, input);

            result.Success.Should().BeTrue(format);
            result.FinalOutput.Should().NotBe(input, format);
        }
    }

    [Theory]
    [InlineData("DocumentLookup", "[1]")]
    [InlineData("DocumentLookup", "{\"collectionId\": \"3\"}")]
    [InlineData("TextTransform", "\"lowercase\"")]
    [InlineData("TextTransform", "{\"transform\": 5}")]
    [InlineData("TextTransform", "{\"transform\": \"rot13\"}")]
    [InlineData("ConditionalBranch", "")]
    [InlineData("ConditionalBranch", "{\"condition\": \"length_greater_than\", \"value\": 100}")]
    [InlineData("ConditionalBranch", "{\"condition\": \"matches\", \"value\": \"(unclosed\"}")]
    [InlineData("OutputFormat", "[]")]
    [InlineData("OutputFormat", "{\"prefix\": 1}")]
    public async Task Settings_the_check_rejects_fail_the_step_when_run(string stepType, string configJson)
    {
        WorkflowStepSettings.Validate(stepType, configJson).Should().NotBeNull();

        var result = await RunCheckedStepAsync(stepType, configJson, "abc");

        result.Success.Should().BeFalse();
        result.Steps[^1].Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("TextTransform", "{ \"transform\": \"lowercase\"", "ABC")]
    [InlineData("TextTransform", "{\"Transform\": \"lowercase\"}", "ABC")]
    [InlineData("OutputFormat", "{\"format\": \"JSON\"}", "abc")]
    [InlineData("ConditionalBranch", "{\"condition\": \"has\", \"value\": \"a\", \"trueBranch\": \"YES\", \"falseBranch\": \"NO\"}", "NO")]
    public async Task Settings_the_check_rejects_are_otherwise_ignored_when_run(string stepType, string configJson, string output)
    {
        // Not a failure, which is the trouble: the run succeeds as if the settings were not there.
        WorkflowStepSettings.Validate(stepType, configJson).Should().NotBeNull();

        var result = await RunCheckedStepAsync(stepType, configJson, "abc");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be(output);
    }
}
