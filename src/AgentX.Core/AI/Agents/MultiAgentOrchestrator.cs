using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.AI.Agents;

/// <summary>
/// Implementation of multi-agent orchestration supporting multiple collaboration strategies.
/// </summary>
public sealed class MultiAgentOrchestrator : IMultiAgentOrchestrator
{
    private readonly IAiService _aiService;
    private readonly ILogger _log;
    private static readonly char[] SentenceTerminators = ['.', '!', '?'];
    private static readonly char[] WordSeparators = [' ', '\r', '\n', '\t', ',', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\''];
    private static readonly string[] ContrastMarkers =
    [
        " however ",
        " but ",
        " risk ",
        " risks ",
        " concern ",
        " concerns ",
        " avoid ",
        " delay ",
        " delaying ",
        " disagree ",
        " trade-off ",
        " tradeoff ",
        " only ",
        " unless "
    ];
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "after",
        "again",
        "agent",
        "agents",
        "also",
        "and",
        "are",
        "because",
        "been",
        "being",
        "can",
        "could",
        "each",
        "for",
        "from",
        "has",
        "have",
        "into",
        "must",
        "need",
        "needs",
        "not",
        "only",
        "out",
        "over",
        "plan",
        "recommend",
        "should",
        "task",
        "that",
        "the",
        "their",
        "then",
        "there",
        "this",
        "until",
        "use",
        "user",
        "users",
        "with",
        "would"
    };

    public MultiAgentOrchestrator(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _log = logger?.ForContext<MultiAgentOrchestrator>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cancelling <paramref name="ct"/> throws <see cref="OperationCanceledException"/> instead of
    /// returning a failed result. A single failing agent does not fail the run while the others
    /// produce an answer: its error is listed in <see cref="OrchestrationResult.Errors"/> and the
    /// final answer says that it is incomplete.
    /// </remarks>
    public async Task<OrchestrationResult> RunAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        OrchestratorStrategy strategy,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _log.Information("Starting orchestration: {Strategy} with {AgentCount} agents", strategy, agents.Count);

        var result = new OrchestrationResult
        {
            Task = task,
            Strategy = strategy,
            Contributions = new(),
            Errors = new()
        };

        try
        {
            switch (strategy)
            {
                case OrchestratorStrategy.Sequential:
                    result = await RunSequentialAsync(task, agents, ct);
                    break;

                case OrchestratorStrategy.Parallel:
                    result = await RunParallelOrchestrationAsync(task, agents, ct);
                    break;

                case OrchestratorStrategy.Debate:
                    var debateErrors = new List<string>();
                    var debateResult = await RunDebateCoreAsync(task, agents, rounds: 2, debateErrors, ct);
                    result.Errors.AddRange(debateErrors);
                    result.IsSuccess = debateResult.Rounds.Any(round => round.Positions.Count > 0) &&
                                       !string.IsNullOrEmpty(debateResult.Synthesis);
                    result.FinalAnswer = AppendFailureNote(debateResult.Synthesis, debateErrors, agents.Count * 2);
                    break;

                case OrchestratorStrategy.DivideAndConquer:
                    result = await RunDivideAndConquerAsync(task, agents, ct);
                    break;

                case OrchestratorStrategy.GenerateCritiqueRefine:
                    result = await RunGcrAsync(task, agents, ct);
                    break;

                default:
                    result = await RunSequentialAsync(task, agents, ct);
                    break;
            }
        }
        catch (Exception ex) when (IsCancellation(ex, ct))
        {
            _log.Information("Orchestration cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Orchestration failed");
            result.IsSuccess = false;
            result.Errors.Add(ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    /// <inheritdoc />
    public Task<DebateResult> RunDebateAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        int rounds,
        CancellationToken ct = default) =>
        RunDebateCoreAsync(task, agents, rounds, new List<string>(), ct);

    private async Task<DebateResult> RunDebateCoreAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        int rounds,
        List<string> errors,
        CancellationToken ct)
    {
        _log.Information("Starting debate: {Rounds} rounds, {AgentCount} participants", rounds, agents.Count);

        var result = new DebateResult { Topic = task, Rounds = new() };

        for (int round = 1; round <= rounds; round++)
        {
            _log.Debug("Debate round {Round}", round);

            var debateRound = new DebateRound { RoundNumber = round };
            var roundContext = new StringBuilder();

            // Add previous round context
            if (round > 1)
            {
                roundContext.AppendLine("Previous positions:");
                foreach (var prevRound in result.Rounds)
                {
                    foreach (var pos in prevRound.Positions)
                    {
                        roundContext.AppendLine($"{pos.Agent.Name}: {pos.Argument.Truncate(150)}...");
                    }
                }
                roundContext.AppendLine();
            }

            foreach (var agent in agents)
            {
                var prompt = BuildDebatePrompt(task, agent, roundContext.ToString(), agents);

                try
                {
                    var response = await _aiService.ChatAsync(
                        messages: new List<ChatMessage> { ChatMessage.User(prompt) },
                        systemPrompt: agent.SystemPrompt,
                        options: new ChatOptions { Temperature = agent.Temperature, MaxTokens = 1500 },
                        ct: ct);

                    debateRound.Positions.Add(new DebatePosition
                    {
                        Agent = agent,
                        Argument = response
                    });

                    roundContext.AppendLine($"{agent.Name}: {response}");
                }
                catch (Exception ex) when (!IsCancellation(ex, ct))
                {
                    _log.Warning(ex, "Agent {AgentName} failed in round {Round}", agent.Name, round);
                    errors.Add(DescribeFailure($"{agent.Name} (round {round})", ex));
                }
            }

            result.Rounds.Add(debateRound);
        }

        // Synthesize final result
        result.Synthesis = await SynthesizeDebateAsync(task, result, ct);
        result.WinningPerspective = IdentifyWinningPerspective(result);

        return result;
    }

    /// <inheritdoc />
    public async Task<ParallelResult> RunParallelAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct = default)
    {
        var (result, _) = await RunParallelCoreAsync(task, agents, ct);
        return result;
    }

    private async Task<(ParallelResult Result, List<string> Errors)> RunParallelCoreAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct)
    {
        _log.Information("Running {AgentCount} agents in parallel", agents.Count);

        var outcomes = await Task.WhenAll(agents.Select(agent => RunAgentAsync(agent, task, ct)));

        var outputs = outcomes.Where(o => o.Contribution is not null).Select(o => o.Contribution!).ToList();
        var errors = outcomes.Where(o => o.Error is not null).Select(o => o.Error!).ToList();
        var combined = await SynthesizeParallelOutputsAsync(task, outputs, ct);

        var result = new ParallelResult
        {
            Task = task,
            Outputs = outputs,
            CombinedOutput = combined.combined,
            Consensus = combined.consensus,
            Disagreements = combined.disagreements
        };

        return (result, errors);
    }

    private async Task<OrchestrationResult> RunSequentialAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct)
    {
        var result = new OrchestrationResult
        {
            Task = task,
            Strategy = OrchestratorStrategy.Sequential,
            Contributions = new()
        };

        var context = $"Task: {task}\n\n";
        var finalAnswer = string.Empty;

        foreach (var agent in agents)
        {
            var prompt = $"{context}\nPlease complete your part of this task based on your expertise.";

            try
            {
                var response = await _aiService.ChatAsync(
                    messages: new List<ChatMessage> { ChatMessage.User(prompt) },
                    systemPrompt: agent.SystemPrompt,
                    options: new ChatOptions { Temperature = agent.Temperature, MaxTokens = 2000 },
                    ct: ct);

                result.Contributions.Add(new AgentContribution
                {
                    Agent = agent,
                    Output = response,
                    Timestamp = DateTime.UtcNow
                });

                context += $"{agent.Name}'s contribution:\n{response}\n\n";
                finalAnswer = response;
            }
            catch (Exception ex) when (!IsCancellation(ex, ct))
            {
                _log.Warning(ex, "Agent {AgentName} failed", agent.Name);
                result.Errors.Add(DescribeFailure(agent.Name, ex));
            }
        }

        result.IsSuccess = !string.IsNullOrWhiteSpace(finalAnswer);
        result.FinalAnswer = AppendFailureNote(finalAnswer, result.Errors, agents.Count);
        return result;
    }

    private async Task<OrchestrationResult> RunParallelOrchestrationAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct)
    {
        var (parallelResult, errors) = await RunParallelCoreAsync(task, agents, ct);
        var isSuccess = parallelResult.Outputs.Count > 0 && !string.IsNullOrEmpty(parallelResult.CombinedOutput);

        return new OrchestrationResult
        {
            Task = task,
            Strategy = OrchestratorStrategy.Parallel,
            FinalAnswer = isSuccess
                ? AppendFailureNote(parallelResult.CombinedOutput, errors, agents.Count)
                : parallelResult.CombinedOutput,
            Contributions = parallelResult.Outputs,
            Errors = errors,
            IsSuccess = isSuccess
        };
    }

    private async Task<OrchestrationResult> RunDivideAndConquerAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct)
    {
        var result = new OrchestrationResult
        {
            Task = task,
            Strategy = OrchestratorStrategy.DivideAndConquer,
            Contributions = new()
        };

        // Divide task among agents
        var divisionPrompt = $"Divide this task into {agents.Count} parts:\n{task}\n\n" +
            "For each part, specify which agent should handle it based on their expertise.";

        try
        {
            var division = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(divisionPrompt) },
                systemPrompt: "You are a task planner. Divide complex work among specialized agents.",
                options: new ChatOptions { Temperature = 0.5, MaxTokens = 1500 },
                ct: ct);

            // Execute sub-tasks in parallel
            var subTasks = ParseSubTasks(division);
            var assigned = Math.Min(agents.Count, subTasks.Count);
            if (subTasks.Count > agents.Count)
            {
                _log.Warning("Task planner returned {SubTaskCount} parts for {AgentCount} agents; extra parts are not run",
                    subTasks.Count, agents.Count);
            }

            var subTaskRuns = Enumerable.Range(0, assigned)
                .Select(i => Task.Run(() => RunAgentAsync(agents[i], subTasks[i], ct), ct))
                .ToList();

            var outcomes = await Task.WhenAll(subTaskRuns);
            result.Contributions = outcomes.Where(o => o.Contribution is not null).Select(o => o.Contribution!).ToList();
            result.Errors.AddRange(outcomes.Where(o => o.Error is not null).Select(o => o.Error!));

            if (result.Contributions.Count == 0)
            {
                result.IsSuccess = false;
                result.Errors.Add("No sub-task produced a result.");
                return result;
            }

            // Synthesize final answer
            var synthesisPrompt = $"Original task: {task}\n\n" +
                "Partial results:\n" + string.Join("\n", result.Contributions.Select(c => $"{c.Agent.Name}: {c.Output.Truncate(200)}"));

            var synthesis = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(synthesisPrompt) },
                systemPrompt: "You are a synthesizer. Combine partial results into a complete answer.",
                options: new ChatOptions { Temperature = 0.5, MaxTokens = 3000 },
                ct: ct);

            result.FinalAnswer = AppendFailureNote(synthesis, result.Errors, assigned);
            result.IsSuccess = !string.IsNullOrWhiteSpace(synthesis);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            _log.Error(ex, "Divide and conquer failed");
            result.IsSuccess = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private async Task<OrchestrationResult> RunGcrAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct)
    {
        if (agents.Count < 3)
        {
            return await RunSequentialAsync(task, agents, ct);
        }

        var result = new OrchestrationResult
        {
            Task = task,
            Strategy = OrchestratorStrategy.GenerateCritiqueRefine,
            Contributions = new()
        };

        try
        {
            // Generate
            var generator = agents[0];
            var initial = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(task) },
                systemPrompt: generator.SystemPrompt,
                options: new ChatOptions { Temperature = generator.Temperature, MaxTokens = 2000 },
                ct: ct);

            result.Contributions.Add(new AgentContribution { Agent = generator, Output = initial });

            // Critique
            var critic = agents[1];
            var critiquePrompt = $"Critique this response to: {task}\n\n{initial}";
            var critique = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(critiquePrompt) },
                systemPrompt: critic.SystemPrompt,
                options: new ChatOptions { Temperature = critic.Temperature, MaxTokens = 1500 },
                ct: ct);

            result.Contributions.Add(new AgentContribution { Agent = critic, Output = critique });

            // Refine
            var refiner = agents[2];
            var refinePrompt = $"Refine this response based on the critique:\n\nOriginal: {initial}\n\nCritique: {critique}";
            var refined = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(refinePrompt) },
                systemPrompt: refiner.SystemPrompt,
                options: new ChatOptions { Temperature = refiner.Temperature, MaxTokens = 3000 },
                ct: ct);

            result.Contributions.Add(new AgentContribution { Agent = refiner, Output = refined });
            result.FinalAnswer = refined;
            result.IsSuccess = true;
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            _log.Error(ex, "GCR orchestration failed");
            result.IsSuccess = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>One agent's output, or the reason it produced none.</summary>
    private readonly record struct AgentOutcome(AgentContribution? Contribution, string? Error);

    private async Task<AgentOutcome> RunAgentAsync(AgentRole agent, string task, CancellationToken ct)
    {
        try
        {
            var response = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(task) },
                systemPrompt: agent.SystemPrompt,
                options: new ChatOptions { Temperature = agent.Temperature, MaxTokens = 2000 },
                ct: ct);

            return new AgentOutcome(
                new AgentContribution
                {
                    Agent = agent,
                    Output = response,
                    Timestamp = DateTime.UtcNow
                },
                null);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            _log.Warning(ex, "Agent {AgentName} failed", agent.Name);
            return new AgentOutcome(null, DescribeFailure(agent.Name, ex));
        }
    }

    /// <summary>
    /// True when <paramref name="ex"/> is the caller's cancellation. A timeout inside a provider
    /// also surfaces as an <see cref="OperationCanceledException"/>, but without the caller's
    /// token being cancelled; that is an agent failure, not a cancellation.
    /// </summary>
    private static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;

    private static string DescribeFailure(string agentName, Exception ex) =>
        $"{agentName}: {ex.Message.Truncate(200)}";

    /// <summary>
    /// Appends a note naming the failed agents, so an answer built from only some of them is
    /// never presented as complete.
    /// </summary>
    private static string AppendFailureNote(string answer, IReadOnlyCollection<string> errors, int attempted)
    {
        if (errors.Count == 0 || string.IsNullOrWhiteSpace(answer))
            return answer;

        var sb = new StringBuilder(answer.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.Append($"Note: {errors.Count} of {Math.Max(attempted, errors.Count)} agent responses failed, so this answer is incomplete.");
        foreach (var error in errors)
        {
            sb.AppendLine();
            sb.Append("- ").Append(error);
        }

        return sb.ToString();
    }

    private static Task<(string combined, string? consensus, List<string> disagreements)> SynthesizeParallelOutputsAsync(
        string task, List<AgentContribution> outputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (outputs.Count == 0)
        {
            return Task.FromResult(("No agents returned a usable output.", (string?)null, new List<string>()));
        }

        var texts = outputs.Select(output => output.Output).ToList();
        var consensus = BuildConsensusSummary(texts, minimumSources: outputs.Count > 1 ? 2 : 1);
        var disagreements = ExtractTensions(outputs).ToList();
        var combined = BuildParallelSynthesis(task, outputs, consensus, disagreements);

        return Task.FromResult((combined, (string?)consensus, disagreements));
    }

    private static string BuildDebatePrompt(string topic, AgentRole agent, string previousContext, IReadOnlyList<AgentRole> allAgents)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are participating in a debate on: {topic}");
        sb.AppendLine($"Your role: {agent.Name} ({agent.Expertise})");
        sb.AppendLine($"Your expertise: {agent.Expertise}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(previousContext))
        {
            sb.AppendLine("Previous positions:");
            sb.AppendLine(previousContext);
            sb.AppendLine();
        }

        sb.AppendLine("Present your position on this topic.");
        if (allAgents.Count > 1)
        {
            sb.AppendLine("Feel free to reference or critique other positions when relevant.");
        }

        return sb.ToString();
    }

    private static Task<string> SynthesizeDebateAsync(string topic, DebateResult result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var positions = result.Rounds
            .SelectMany(round => round.Positions.Select(position => (round.RoundNumber, position)))
            .ToList();

        if (positions.Count == 0)
        {
            return Task.FromResult($"# Debate Synthesis\n\nTopic: {topic}\n\nNo debate positions were produced.");
        }

        var consensus = BuildConsensusSummary(
            positions.Select(item => item.position.Argument),
            minimumSources: positions.Count > 1 ? 2 : 1);
        var latestRound = result.Rounds.LastOrDefault()?.Positions ?? [];
        var disagreements = ExtractTensions(latestRound.Select(position => new AgentContribution
        {
            Agent = position.Agent,
            Output = position.Argument,
        })).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Debate Synthesis");
        sb.AppendLine();
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine($"Rounds analyzed: {result.Rounds.Count}");
        sb.AppendLine();
        sb.AppendLine("## Consensus");
        sb.AppendLine(consensus);
        sb.AppendLine();
        sb.AppendLine("## Open Disagreements");
        if (disagreements.Count == 0)
        {
            sb.AppendLine("- No material disagreement remained in the final round.");
        }
        else
        {
            foreach (var disagreement in disagreements)
            {
                sb.AppendLine($"- {disagreement}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Position Evolution");
        foreach (var (roundNumber, position) in positions)
        {
            sb.AppendLine($"- Round {roundNumber}, {position.Agent.Name}: {SummarizeSentence(position.Argument)}");
        }
        sb.AppendLine();
        sb.AppendLine("## Final Perspective");
        sb.AppendLine(BuildFinalPerspective(latestRound, consensus));

        return Task.FromResult(sb.ToString());
    }

    private static string? IdentifyWinningPerspective(DebateResult result)
    {
        if (result.Rounds.Count == 0) return null;

        return result.Rounds[^1].Positions
            .OrderByDescending(position => ScoreDebatePosition(position.Argument))
            .ThenBy(position => position.Agent.Name, StringComparer.Ordinal)
            .FirstOrDefault()
            ?.Agent
            .Name;
    }

    // A list item: "- x", "* x", "+ x", "\u2022 x", "1. x", "1) x", "(1) x". Group "indent" is the
    // leading whitespace, group "text" the item text exactly as written.
    private static readonly Regex ListItemPattern = new(
        @"^(?<indent>[ \t]*)(?:[-*+\u2022]|\(?\d{1,3}[.)])[ \t]+(?<text>\S.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A heading such as "**Part 2: Risks** (Critic)" or "Step 3 - Review".
    private static readonly Regex PartHeadingPattern = new(
        @"^(?<indent>[ \t]*)(?:\*\*|__)?(?:part|step|sub-?task|task)[ \t]+\d{1,3}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Splits the planner's answer into sub-tasks: one per top-level bullet, numbered item or
    /// "Part N" heading. Indented (nested) items and plain lines continue the current sub-task.
    /// The item text is kept as written, including any numbers in it. Without a recognizable
    /// list the whole answer is a single sub-task.
    /// </summary>
    internal static List<string> ParseSubTasks(string division)
    {
        var subTasks = new List<string>();
        var currentTask = new StringBuilder();
        int? topLevelIndent = null;

        foreach (var rawLine in division.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string? itemText = null;
            var indent = 0;

            var heading = PartHeadingPattern.Match(line);
            if (heading.Success)
            {
                indent = heading.Groups["indent"].Length;
                itemText = line.Replace("**", string.Empty).Replace("__", string.Empty).Trim();
            }
            else
            {
                var item = ListItemPattern.Match(line);
                if (item.Success)
                {
                    indent = item.Groups["indent"].Length;
                    itemText = item.Groups["text"].Value.Trim();
                }
            }

            var startsTopLevelItem = itemText is not null && (topLevelIndent is null || indent <= topLevelIndent);
            if (startsTopLevelItem)
            {
                if (currentTask.Length > 0)
                {
                    subTasks.Add(currentTask.ToString().Trim());
                    currentTask.Clear();
                }

                topLevelIndent = indent;
                currentTask.Append(itemText);
            }
            else if (currentTask.Length > 0)
            {
                // A nested item or a wrapped line belongs to the current sub-task.
                currentTask.Append(' ').Append(itemText ?? line.Trim());
            }
        }

        if (currentTask.Length > 0)
        {
            subTasks.Add(currentTask.ToString().Trim());
        }

        return subTasks.Count > 0 ? subTasks : new List<string> { division };
    }

    private static string BuildParallelSynthesis(
        string task,
        IReadOnlyList<AgentContribution> outputs,
        string consensus,
        IReadOnlyList<string> disagreements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Multi-Agent Synthesis");
        sb.AppendLine();
        sb.AppendLine($"Task: {task}");
        sb.AppendLine();
        sb.AppendLine("## Consensus");
        sb.AppendLine(consensus);
        sb.AppendLine();
        sb.AppendLine("## Trade-offs and Disagreements");
        if (disagreements.Count == 0)
        {
            sb.AppendLine("- No material disagreement was detected across the agent outputs.");
        }
        else
        {
            foreach (var disagreement in disagreements)
            {
                sb.AppendLine($"- {disagreement}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Agent Contributions");
        foreach (var output in outputs)
        {
            sb.AppendLine($"### {output.Agent.Name}");
            sb.AppendLine(output.Output.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildConsensusSummary(IEnumerable<string> texts, int minimumSources)
    {
        var textList = texts.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        if (textList.Count == 0)
        {
            return "No usable agent content was available to synthesize.";
        }

        var sharedPhrases = ExtractSharedPhrases(textList, minimumSources).Take(5).ToList();
        var representativeSentence = SummarizeSentence(FindRepresentativeConsensusSentence(textList, sharedPhrases));

        if (sharedPhrases.Count > 0)
        {
            return $"The agents converge on {FormatPhraseList(sharedPhrases)}. {representativeSentence}";
        }

        return $"The agents did not repeat a single phrase, but their outputs form a compatible direction. {representativeSentence}";
    }

    private static IEnumerable<string> ExtractSharedPhrases(IReadOnlyList<string> texts, int minimumSources)
    {
        var phraseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sourceIndex = 0;

        foreach (var text in texts)
        {
            var phrasesForSource = ExtractPhrases(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var phrase in phrasesForSource)
            {
                phraseCounts[phrase] = phraseCounts.TryGetValue(phrase, out var count) ? count + 1 : 1;
                firstSeen.TryAdd(phrase, sourceIndex);
            }

            sourceIndex++;
        }

        return phraseCounts
            .Where(pair => pair.Value >= Math.Max(1, minimumSources))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => firstSeen[pair.Key])
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key);
    }

    private static IEnumerable<string> ExtractPhrases(string text)
    {
        var tokens = Tokenize(text).ToList();
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            yield return $"{tokens[i]} {tokens[i + 1]}";
        }

        for (var i = 0; i < tokens.Count - 2; i++)
        {
            yield return $"{tokens[i]} {tokens[i + 1]} {tokens[i + 2]}";
        }
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim('.', '!', '?', '-', '/'))
            .Where(token => token.Length > 2 && !StopWords.Contains(token));
    }

    private static IEnumerable<string> ExtractTensions(IEnumerable<AgentContribution> outputs)
    {
        var tensions = new List<string>();
        foreach (var output in outputs)
        {
            var sentence = ExtractSentences(output.Output)
                .FirstOrDefault(ContainsContrastMarker);
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                tensions.Add($"{output.Agent.Name}: {NormalizeSentence(sentence)}");
            }
        }

        return tensions.Count > 0
            ? tensions.Distinct(StringComparer.Ordinal).Take(6)
            : outputs.Select(output => $"{output.Agent.Name}: Emphasized {SummarizeSentence(output.Output)}")
                .Distinct(StringComparer.Ordinal)
                .Take(3);
    }

    private static bool ContainsContrastMarker(string sentence)
    {
        var normalized = $" {sentence.ToLowerInvariant()} ";
        return ContrastMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static string FindRepresentativeConsensusSentence(IReadOnlyList<string> texts, IReadOnlyList<string> sharedPhrases)
    {
        if (sharedPhrases.Count > 0)
        {
            foreach (var phrase in sharedPhrases)
            {
                var sentence = texts
                    .SelectMany(ExtractSentences)
                    .FirstOrDefault(candidate => candidate.Contains(phrase, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    return sentence;
                }
            }
        }

        return texts.SelectMany(ExtractSentences).FirstOrDefault() ?? texts[0];
    }

    private static IReadOnlyList<string> ExtractSentences(string text)
    {
        return text
            .Split(SentenceTerminators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToList();
    }

    private static string SummarizeSentence(string text)
    {
        var sentence = ExtractSentences(text).FirstOrDefault() ?? text;
        return NormalizeSentence(sentence.Truncate(220));
    }

    private static string NormalizeSentence(string sentence)
    {
        var collapsed = string.Join(
            " ",
            sentence.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        return SentenceTerminators.Contains(collapsed[^1]) ? collapsed : $"{collapsed}.";
    }

    private static string FormatPhraseList(IReadOnlyList<string> phrases)
    {
        return phrases.Count switch
        {
            0 => "the same direction",
            1 => phrases[0],
            2 => $"{phrases[0]} and {phrases[1]}",
            _ => $"{string.Join(", ", phrases.Take(phrases.Count - 1))}, and {phrases[^1]}"
        };
    }

    private static string BuildFinalPerspective(IReadOnlyList<DebatePosition> latestRound, string consensus)
    {
        if (latestRound.Count == 0)
        {
            return consensus;
        }

        var strongest = latestRound
            .OrderByDescending(position => ScoreDebatePosition(position.Argument))
            .ThenBy(position => position.Agent.Name, StringComparer.Ordinal)
            .First();

        return $"{consensus} The strongest final position came from {strongest.Agent.Name}: {SummarizeSentence(strongest.Argument)}";
    }

    private static int ScoreDebatePosition(string argument)
    {
        var normalized = $" {argument.ToLowerInvariant()} ";
        var score = Math.Min(argument.Length / 40, 8);

        foreach (var marker in new[] { " agree ", " support ", " recommend ", " because ", " consent ", " audit ", " mitigate ", " control " })
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
            {
                score += 2;
            }
        }

        foreach (var marker in new[] { " risk ", " however ", " but ", " disagree " })
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score;
    }
}
