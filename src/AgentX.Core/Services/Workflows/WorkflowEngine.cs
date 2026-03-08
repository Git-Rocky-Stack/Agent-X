using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search;
using AgentX.Core.Services.Workflows.Models;
using Serilog;

namespace AgentX.Core.Services.Workflows;

/// <summary>
/// Executes workflow pipelines by iterating through each step in order,
/// processing templates, invoking AI services, and persisting run history.
/// Supports cancellation, progress reporting, and per-step error handling.
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowService _workflowService;
    private readonly IAiService _aiService;
    private readonly IRagPipeline _ragPipeline;
    private readonly AgentXDbContext _db;
    private readonly ILogger _log;

    private CancellationTokenSource? _cancellationSource;
    private volatile bool _isRunning;

    /// <summary>
    /// Supported text transform operations for the TextTransform step type.
    /// </summary>
    private static readonly HashSet<string> _supportedTransforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "uppercase",
        "lowercase",
        "titlecase",
        "trim",
        "extract_lines",
        "word_count",
        "char_count",
        "reverse_lines",
        "deduplicate_lines",
        "sort_lines",
        "number_lines",
    };

    public WorkflowEngine(
        IWorkflowService workflowService,
        IAiService aiService,
        IRagPipeline ragPipeline,
        AgentXDbContext db,
        ILogger logger)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _ragPipeline = ragPipeline ?? throw new ArgumentNullException(nameof(ragPipeline));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<WorkflowEngine>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public event EventHandler<WorkflowStepResult>? StepCompleted;

    /// <inheritdoc />
    public async Task<WorkflowRunResult> ExecuteWorkflowAsync(
        long workflowId,
        string input,
        IProgress<WorkflowStepResult>? progress = null,
        CancellationToken ct = default)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException(
                "A workflow is already being executed. Cancel the current execution before starting a new one.");
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException(
                "Workflow input must not be empty.", nameof(input));
        }

        // Load the workflow with its steps
        var workflow = await _workflowService.GetWorkflowAsync(workflowId);
        if (workflow is null)
        {
            throw new InvalidOperationException(
                $"Workflow {workflowId} not found.");
        }

        var orderedSteps = workflow.Steps
            .OrderBy(s => s.StepOrder)
            .ToList();

        if (orderedSteps.Count == 0)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflow.Name}' has no steps to execute.");
        }

        // Create a linked cancellation source so we can cancel internally
        _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _isRunning = true;

        var overallStopwatch = Stopwatch.StartNew();
        var stepResults = new List<WorkflowStepResult>();
        var previousOutput = string.Empty;

        // Create the run record
        var run = new WorkflowRunEntity
        {
            WorkflowId = workflowId,
            Status = "running",
            InitialInput = input,
            StartedAt = DateTime.UtcNow,
            TotalSteps = orderedSteps.Count,
            StepsCompleted = 0,
        };

        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync();

        _log.Information(
            "Starting workflow '{WorkflowName}' (Id={WorkflowId}) with {StepCount} steps, RunId={RunId}",
            workflow.Name, workflowId, orderedSteps.Count, run.Id);

        try
        {
            for (var i = 0; i < orderedSteps.Count; i++)
            {
                // Check for cancellation before each step
                if (_cancellationSource.Token.IsCancellationRequested)
                {
                    _log.Information(
                        "Workflow execution cancelled before step {StepOrder} '{StepName}'",
                        orderedSteps[i].StepOrder, orderedSteps[i].Name);

                    run.Status = "cancelled";
                    run.CompletedAt = DateTime.UtcNow;
                    run.FinalOutput = previousOutput;
                    run.StepOutputsJson = SerializeStepResults(stepResults);
                    run.TotalTokensUsed = stepResults.Sum(r => r.TokensUsed);
                    await _db.SaveChangesAsync();

                    break;
                }

                var step = orderedSteps[i];
                var stepResult = await ExecuteStepAsync(
                    step, input, previousOutput, _cancellationSource.Token);

                stepResults.Add(stepResult);

                // Report progress
                progress?.Report(stepResult);
                StepCompleted?.Invoke(this, stepResult);

                if (!stepResult.Success)
                {
                    _log.Error(
                        "Workflow '{WorkflowName}' failed at step {StepOrder} '{StepName}': {Error}",
                        workflow.Name, step.StepOrder, step.Name, stepResult.ErrorMessage);

                    run.Status = "failed";
                    run.ErrorMessage = $"Step '{step.Name}' (#{step.StepOrder}) failed: {stepResult.ErrorMessage}";
                    run.CompletedAt = DateTime.UtcNow;
                    run.StepsCompleted = i;
                    run.FinalOutput = previousOutput;
                    run.StepOutputsJson = SerializeStepResults(stepResults);
                    run.TotalTokensUsed = stepResults.Sum(r => r.TokensUsed);
                    await _db.SaveChangesAsync();

                    overallStopwatch.Stop();

                    return new WorkflowRunResult
                    {
                        WorkflowName = workflow.Name,
                        Steps = stepResults,
                        FinalOutput = previousOutput,
                        TotalTokensUsed = stepResults.Sum(r => r.TokensUsed),
                        TotalDurationMs = overallStopwatch.Elapsed.TotalMilliseconds,
                        Success = false,
                    };
                }

                previousOutput = stepResult.Output;
                run.StepsCompleted = i + 1;

                _log.Debug(
                    "Completed step {StepOrder}/{TotalSteps} '{StepName}' ({Tokens} tokens, {DurationMs:F0}ms)",
                    i + 1, orderedSteps.Count, step.Name,
                    stepResult.TokensUsed, stepResult.DurationMs);
            }

            overallStopwatch.Stop();

            // If we were cancelled, the run was already updated above
            if (run.Status != "cancelled")
            {
                run.Status = "completed";
                run.CompletedAt = DateTime.UtcNow;
                run.FinalOutput = previousOutput;
                run.StepsCompleted = orderedSteps.Count;
                run.StepOutputsJson = SerializeStepResults(stepResults);
                run.TotalTokensUsed = stepResults.Sum(r => r.TokensUsed);
                await _db.SaveChangesAsync();
            }

            // Increment the workflow's run count
            workflow.RunCount += 1;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _workflowService.UpdateWorkflowAsync(workflow);

            _log.Information(
                "Workflow '{WorkflowName}' {Status} in {DurationMs:F0}ms ({TotalTokens} tokens, {StepsCompleted}/{TotalSteps} steps)",
                workflow.Name, run.Status,
                overallStopwatch.Elapsed.TotalMilliseconds,
                stepResults.Sum(r => r.TokensUsed),
                run.StepsCompleted, run.TotalSteps);

            return new WorkflowRunResult
            {
                WorkflowName = workflow.Name,
                Steps = stepResults,
                FinalOutput = previousOutput,
                TotalTokensUsed = stepResults.Sum(r => r.TokensUsed),
                TotalDurationMs = overallStopwatch.Elapsed.TotalMilliseconds,
                Success = run.Status == "completed",
            };
        }
        catch (OperationCanceledException)
        {
            overallStopwatch.Stop();

            run.Status = "cancelled";
            run.CompletedAt = DateTime.UtcNow;
            run.FinalOutput = previousOutput;
            run.StepOutputsJson = SerializeStepResults(stepResults);
            run.TotalTokensUsed = stepResults.Sum(r => r.TokensUsed);
            await _db.SaveChangesAsync();

            _log.Information(
                "Workflow '{WorkflowName}' was cancelled after {StepsCompleted}/{TotalSteps} steps",
                workflow.Name, run.StepsCompleted, run.TotalSteps);

            return new WorkflowRunResult
            {
                WorkflowName = workflow.Name,
                Steps = stepResults,
                FinalOutput = previousOutput,
                TotalTokensUsed = stepResults.Sum(r => r.TokensUsed),
                TotalDurationMs = overallStopwatch.Elapsed.TotalMilliseconds,
                Success = false,
            };
        }
        catch (Exception ex)
        {
            overallStopwatch.Stop();

            _log.Error(ex,
                "Unexpected error executing workflow '{WorkflowName}' (RunId={RunId})",
                workflow.Name, run.Id);

            run.Status = "failed";
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTime.UtcNow;
            run.FinalOutput = previousOutput;
            run.StepOutputsJson = SerializeStepResults(stepResults);
            run.TotalTokensUsed = stepResults.Sum(r => r.TokensUsed);
            await _db.SaveChangesAsync();

            return new WorkflowRunResult
            {
                WorkflowName = workflow.Name,
                Steps = stepResults,
                FinalOutput = previousOutput,
                TotalTokensUsed = stepResults.Sum(r => r.TokensUsed),
                TotalDurationMs = overallStopwatch.Elapsed.TotalMilliseconds,
                Success = false,
            };
        }
        finally
        {
            _isRunning = false;
            _cancellationSource?.Dispose();
            _cancellationSource = null;
        }
    }

    /// <inheritdoc />
    public Task CancelExecutionAsync()
    {
        if (_cancellationSource is not null && !_cancellationSource.IsCancellationRequested)
        {
            _log.Information("Cancellation requested for running workflow");
            _cancellationSource.Cancel();
        }
        else
        {
            _log.Debug("CancelExecutionAsync called but no workflow is running or already cancelled");
        }

        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step execution dispatching
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single workflow step based on its <see cref="WorkflowStepEntity.StepType"/>.
    /// </summary>
    private async Task<WorkflowStepResult> ExecuteStepAsync(
        WorkflowStepEntity step,
        string originalInput,
        string previousOutput,
        CancellationToken ct)
    {
        var stepStopwatch = Stopwatch.StartNew();

        try
        {
            return step.StepType switch
            {
                "AiPrompt" => await ExecuteAiPromptStepAsync(step, originalInput, previousOutput, stepStopwatch, ct),
                "DocumentLookup" => await ExecuteDocumentLookupStepAsync(step, originalInput, previousOutput, stepStopwatch, ct),
                "TextTransform" => ExecuteTextTransformStep(step, originalInput, previousOutput, stepStopwatch),
                "ConditionalBranch" => ExecuteConditionalBranchStep(step, originalInput, previousOutput, stepStopwatch),
                "OutputFormat" => ExecuteOutputFormatStep(step, originalInput, previousOutput, stepStopwatch),
                _ => new WorkflowStepResult
                {
                    StepName = step.Name,
                    StepOrder = step.StepOrder,
                    Output = string.Empty,
                    DurationMs = stepStopwatch.Elapsed.TotalMilliseconds,
                    Success = false,
                    ErrorMessage = $"Unknown step type '{step.StepType}'.",
                },
            };
        }
        catch (OperationCanceledException)
        {
            throw; // Let the caller handle cancellation
        }
        catch (Exception ex)
        {
            stepStopwatch.Stop();

            _log.Error(ex,
                "Exception in step '{StepName}' (Type={StepType})",
                step.Name, step.StepType);

            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = string.Empty,
                DurationMs = stepStopwatch.Elapsed.TotalMilliseconds,
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // AiPrompt step
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes an AI prompt step by resolving the template placeholders
    /// and calling the AI service for a chat completion.
    /// </summary>
    private async Task<WorkflowStepResult> ExecuteAiPromptStepAsync(
        WorkflowStepEntity step,
        string originalInput,
        string previousOutput,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        var resolvedPrompt = ResolveTemplate(step.PromptTemplate, originalInput, previousOutput);

        if (string.IsNullOrWhiteSpace(resolvedPrompt))
        {
            stopwatch.Stop();
            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = string.Empty,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Success = false,
                ErrorMessage = "Resolved prompt template was empty after placeholder substitution.",
            };
        }

        // Build chat options with optional overrides
        var options = new ChatOptions();

        if (!string.IsNullOrWhiteSpace(step.ModelOverride))
        {
            options.ModelId = step.ModelOverride;
        }

        if (step.TemperatureOverride.HasValue)
        {
            options.Temperature = step.TemperatureOverride.Value;
        }

        if (step.MaxTokensOverride.HasValue)
        {
            options.MaxTokens = step.MaxTokensOverride.Value;
        }

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = resolvedPrompt },
        };

        var response = await _aiService.ChatAsync(messages, options: options, ct: ct);

        stopwatch.Stop();

        // Estimate token count from the response length (rough heuristic: ~4 chars per token)
        var estimatedTokens = (long)Math.Ceiling((resolvedPrompt.Length + response.Length) / 4.0);

        return new WorkflowStepResult
        {
            StepName = step.Name,
            StepOrder = step.StepOrder,
            Output = response,
            TokensUsed = estimatedTokens,
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            ModelUsed = step.ModelOverride ?? _aiService.ActiveModelId,
            Success = true,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // DocumentLookup step
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a document lookup step by querying the RAG pipeline
    /// with the resolved prompt template as the search query.
    /// </summary>
    private async Task<WorkflowStepResult> ExecuteDocumentLookupStepAsync(
        WorkflowStepEntity step,
        string originalInput,
        string previousOutput,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        var query = ResolveTemplate(step.PromptTemplate, originalInput, previousOutput);

        if (string.IsNullOrWhiteSpace(query))
        {
            stopwatch.Stop();
            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = string.Empty,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Success = false,
                ErrorMessage = "Document lookup query was empty after placeholder substitution.",
            };
        }

        // Parse optional collection ID from ConfigJson
        long? collectionId = null;
        if (!string.IsNullOrWhiteSpace(step.ConfigJson))
        {
            try
            {
                using var configDoc = JsonDocument.Parse(step.ConfigJson);
                if (configDoc.RootElement.TryGetProperty("collectionId", out var collectionIdElement)
                    && collectionIdElement.TryGetInt64(out var parsedId))
                {
                    collectionId = parsedId;
                }
            }
            catch (JsonException ex)
            {
                _log.Warning(ex,
                    "Failed to parse ConfigJson for DocumentLookup step '{StepName}'",
                    step.Name);
            }
        }

        var ragResponse = await _ragPipeline.AskAsync(query, collectionId, ct: ct);

        stopwatch.Stop();

        var output = ragResponse.AnswerText;

        // Estimate token count from output length
        var estimatedTokens = (long)Math.Ceiling((query.Length + output.Length) / 4.0);

        return new WorkflowStepResult
        {
            StepName = step.Name,
            StepOrder = step.StepOrder,
            Output = output,
            TokensUsed = estimatedTokens,
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            ModelUsed = _aiService.ActiveModelId,
            Success = true,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // TextTransform step
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a text transform step by applying a deterministic text
    /// transformation to the input. The transform type is specified in
    /// the step's <see cref="WorkflowStepEntity.ConfigJson"/>.
    /// </summary>
    private WorkflowStepResult ExecuteTextTransformStep(
        WorkflowStepEntity step,
        string originalInput,
        string previousOutput,
        Stopwatch stopwatch)
    {
        var textToTransform = ResolveTemplate(step.PromptTemplate, originalInput, previousOutput);

        // If the template is empty, fall back to previous output
        if (string.IsNullOrWhiteSpace(textToTransform))
        {
            textToTransform = previousOutput;
        }

        // Determine the transform type from ConfigJson
        var transformType = "uppercase"; // default
        if (!string.IsNullOrWhiteSpace(step.ConfigJson))
        {
            try
            {
                using var configDoc = JsonDocument.Parse(step.ConfigJson);
                if (configDoc.RootElement.TryGetProperty("transform", out var transformElement))
                {
                    transformType = transformElement.GetString() ?? "uppercase";
                }
            }
            catch (JsonException ex)
            {
                _log.Warning(ex,
                    "Failed to parse ConfigJson for TextTransform step '{StepName}'",
                    step.Name);
            }
        }

        if (!_supportedTransforms.Contains(transformType))
        {
            stopwatch.Stop();
            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = string.Empty,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Success = false,
                ErrorMessage = $"Unsupported transform type '{transformType}'. Supported: {string.Join(", ", _supportedTransforms)}.",
            };
        }

        var output = ApplyTextTransform(textToTransform, transformType);

        stopwatch.Stop();

        return new WorkflowStepResult
        {
            StepName = step.Name,
            StepOrder = step.StepOrder,
            Output = output,
            TokensUsed = 0, // No AI tokens consumed
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            Success = true,
        };
    }

    /// <summary>
    /// Applies a deterministic text transformation to the input string.
    /// </summary>
    private static string ApplyTextTransform(string text, string transformType)
    {
        return transformType.ToLowerInvariant() switch
        {
            "uppercase" => text.ToUpperInvariant(),
            "lowercase" => text.ToLowerInvariant(),
            "titlecase" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            "trim" => text.Trim(),
            "extract_lines" => ExtractNonEmptyLines(text),
            "word_count" => $"Word count: {CountWords(text)}",
            "char_count" => $"Character count: {text.Length}",
            "reverse_lines" => ReverseLines(text),
            "deduplicate_lines" => DeduplicateLines(text),
            "sort_lines" => SortLines(text),
            "number_lines" => NumberLines(text),
            _ => text,
        };
    }

    private static string ExtractNonEmptyLines(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l));
        return string.Join(Environment.NewLine, lines);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, @"\S+").Count;
    }

    private static string ReverseLines(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Reverse();
        return string.Join(Environment.NewLine, lines);
    }

    private static string DeduplicateLines(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Distinct();
        return string.Join(Environment.NewLine, lines);
    }

    private static string SortLines(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, lines);
    }

    private static string NumberLines(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Select((line, index) => $"{index + 1}. {line}");
        return string.Join(Environment.NewLine, lines);
    }

    // ─────────────────────────────────────────────────────────────────────
    // ConditionalBranch step
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a conditional branch step that checks the previous output
    /// against a condition and returns different output based on the result.
    /// The condition and branches are configured in <see cref="WorkflowStepEntity.ConfigJson"/>.
    /// </summary>
    private WorkflowStepResult ExecuteConditionalBranchStep(
        WorkflowStepEntity step,
        string originalInput,
        string previousOutput,
        Stopwatch stopwatch)
    {
        // Parse condition from ConfigJson
        // Expected format: { "condition": "contains|starts_with|ends_with|matches",
        //                     "value": "search text",
        //                     "trueBranch": "output if true",
        //                     "falseBranch": "output if false" }
        if (string.IsNullOrWhiteSpace(step.ConfigJson))
        {
            stopwatch.Stop();
            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = previousOutput,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Success = false,
                ErrorMessage = "ConditionalBranch step requires ConfigJson with condition, value, trueBranch, and falseBranch properties.",
            };
        }

        try
        {
            using var configDoc = JsonDocument.Parse(step.ConfigJson);
            var root = configDoc.RootElement;

            var condition = root.TryGetProperty("condition", out var condProp)
                ? condProp.GetString() ?? "contains"
                : "contains";
            var value = root.TryGetProperty("value", out var valProp)
                ? valProp.GetString() ?? string.Empty
                : string.Empty;
            var trueBranch = root.TryGetProperty("trueBranch", out var trueProp)
                ? trueProp.GetString() ?? previousOutput
                : previousOutput;
            var falseBranch = root.TryGetProperty("falseBranch", out var falseProp)
                ? falseProp.GetString() ?? previousOutput
                : previousOutput;

            var conditionMet = EvaluateCondition(previousOutput, condition, value);
            var output = conditionMet
                ? ResolveTemplate(trueBranch, originalInput, previousOutput)
                : ResolveTemplate(falseBranch, originalInput, previousOutput);

            stopwatch.Stop();

            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = output,
                TokensUsed = 0,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Success = true,
            };
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            return new WorkflowStepResult
            {
                StepName = step.Name,
                StepOrder = step.StepOrder,
                Output = string.Empty,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Success = false,
                ErrorMessage = $"Failed to parse ConditionalBranch ConfigJson: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Evaluates a condition against the given text.
    /// </summary>
    private static bool EvaluateCondition(string text, string condition, string value)
    {
        return condition.ToLowerInvariant() switch
        {
            "contains" => text.Contains(value, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !text.Contains(value, StringComparison.OrdinalIgnoreCase),
            "starts_with" => text.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            "ends_with" => text.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            "equals" => text.Equals(value, StringComparison.OrdinalIgnoreCase),
            "matches" => Regex.IsMatch(text, value, RegexOptions.IgnoreCase),
            "length_greater_than" => int.TryParse(value, out var len) && text.Length > len,
            _ => false,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // OutputFormat step
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes an output formatting step that wraps the previous output
    /// in a specified format structure. The format is configured in
    /// <see cref="WorkflowStepEntity.ConfigJson"/>.
    /// </summary>
    private WorkflowStepResult ExecuteOutputFormatStep(
        WorkflowStepEntity step,
        string originalInput,
        string previousOutput,
        Stopwatch stopwatch)
    {
        // Use the prompt template as the format wrapper, or fall through to ConfigJson
        var formattedOutput = !string.IsNullOrWhiteSpace(step.PromptTemplate)
            ? ResolveTemplate(step.PromptTemplate, originalInput, previousOutput)
            : previousOutput;

        // Check ConfigJson for additional formatting instructions
        if (!string.IsNullOrWhiteSpace(step.ConfigJson))
        {
            try
            {
                using var configDoc = JsonDocument.Parse(step.ConfigJson);
                var root = configDoc.RootElement;

                // Optional: wrap in a format template
                if (root.TryGetProperty("format", out var formatProp))
                {
                    var format = formatProp.GetString();
                    if (!string.IsNullOrWhiteSpace(format))
                    {
                        formattedOutput = format switch
                        {
                            "json" => WrapAsJson(formattedOutput),
                            "markdown" => WrapAsMarkdown(formattedOutput, step.Name),
                            "html" => WrapAsHtml(formattedOutput, step.Name),
                            "bullet_list" => ConvertToBulletList(formattedOutput),
                            "numbered_list" => ConvertToNumberedList(formattedOutput),
                            _ => formattedOutput,
                        };
                    }
                }

                // Optional: add a prefix
                if (root.TryGetProperty("prefix", out var prefixProp))
                {
                    var prefix = prefixProp.GetString();
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        formattedOutput = prefix + formattedOutput;
                    }
                }

                // Optional: add a suffix
                if (root.TryGetProperty("suffix", out var suffixProp))
                {
                    var suffix = suffixProp.GetString();
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        formattedOutput += suffix;
                    }
                }
            }
            catch (JsonException ex)
            {
                _log.Warning(ex,
                    "Failed to parse ConfigJson for OutputFormat step '{StepName}'",
                    step.Name);
            }
        }

        stopwatch.Stop();

        return new WorkflowStepResult
        {
            StepName = step.Name,
            StepOrder = step.StepOrder,
            Output = formattedOutput,
            TokensUsed = 0,
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            Success = true,
        };
    }

    private static string WrapAsJson(string text)
    {
        var obj = new { output = text };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string WrapAsMarkdown(string text, string title)
    {
        return $"# {title}\n\n{text}\n";
    }

    private static string WrapAsHtml(string text, string title)
    {
        var escapedTitle = System.Net.WebUtility.HtmlEncode(title);
        var escapedText = System.Net.WebUtility.HtmlEncode(text)
            .Replace(Environment.NewLine, "<br/>\n")
            .Replace("\n", "<br/>\n");
        return $"<div>\n<h2>{escapedTitle}</h2>\n<p>{escapedText}</p>\n</div>";
    }

    private static string ConvertToBulletList(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => $"- {l.TrimStart('-', '*', ' ')}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ConvertToNumberedList(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select((l, i) => $"{i + 1}. {l.TrimStart('-', '*', ' ')}");
        return string.Join(Environment.NewLine, lines);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Template resolution
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces <c>{{input}}</c> and <c>{{previous_output}}</c> placeholders
    /// in a template string with the corresponding values.
    /// </summary>
    private static string ResolveTemplate(string template, string input, string previousOutput)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return template
            .Replace("{{input}}", input)
            .Replace("{{previous_output}}", previousOutput);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Serialization
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes a list of step results to a JSON string for persistence
    /// in <see cref="WorkflowRunEntity.StepOutputsJson"/>.
    /// </summary>
    private static string? SerializeStepResults(List<WorkflowStepResult> results)
    {
        if (results.Count == 0) return null;

        return JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }
}
