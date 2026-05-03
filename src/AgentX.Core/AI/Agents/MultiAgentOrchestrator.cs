using System.Diagnostics;
using System.Text;
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
                    var debateResult = await RunDebateAsync(task, agents, rounds: 2, ct);
                    result.FinalAnswer = debateResult.Synthesis;
                    result.IsSuccess = debateResult.Rounds.Any(round => round.Positions.Count > 0) &&
                                       !string.IsNullOrEmpty(debateResult.Synthesis);
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
    public async Task<DebateResult> RunDebateAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        int rounds,
        CancellationToken ct = default)
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
                catch (Exception ex)
                {
                    _log.Warning(ex, "Agent {AgentName} failed in round {Round}", agent.Name, round);
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
        _log.Information("Running {AgentCount} agents in parallel", agents.Count);

        var tasks = agents.Select(agent => RunAgentAsync(agent, task, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        var outputs = results.OfType<AgentContribution>().ToList();
        var combined = await SynthesizeParallelOutputsAsync(task, outputs, ct);

        return new ParallelResult
        {
            Task = task,
            Outputs = outputs,
            CombinedOutput = combined.combined,
            Consensus = combined.consensus,
            Disagreements = combined.disagreements
        };
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
            catch (Exception ex)
            {
                _log.Warning(ex, "Agent {AgentName} failed", agent.Name);
                result.Errors.Add($"{agent.Name}: {ex.Message}");
            }
        }

        result.FinalAnswer = finalAnswer;
        result.IsSuccess = result.Errors.Count == 0;
        return result;
    }

    private async Task<OrchestrationResult> RunParallelOrchestrationAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct)
    {
        var parallelResult = await RunParallelAsync(task, agents, ct);

        return new OrchestrationResult
        {
            Task = task,
            Strategy = OrchestratorStrategy.Parallel,
            FinalAnswer = parallelResult.CombinedOutput,
            Contributions = parallelResult.Outputs,
            IsSuccess = parallelResult.Outputs.Count > 0 && !string.IsNullOrEmpty(parallelResult.CombinedOutput)
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
            var subTaskPromises = new List<Task<AgentContribution?>>();

            for (int i = 0; i < Math.Min(agents.Count, subTasks.Count); i++)
            {
                var agent = agents[i];
                var subTask = subTasks[i];

                subTaskPromises.Add(Task.Run(async () =>
                {
                    try
                    {
                        var response = await _aiService.ChatAsync(
                            messages: new List<ChatMessage> { ChatMessage.User(subTask) },
                            systemPrompt: agent.SystemPrompt,
                            options: new ChatOptions { Temperature = agent.Temperature, MaxTokens = 2000 },
                            ct: ct);

                        return new AgentContribution
                        {
                            Agent = agent,
                            Output = response,
                            Timestamp = DateTime.UtcNow
                        };
                    }
                    catch
                    {
                        return null;
                    }
                }, ct));
            }

            var contributions = await Task.WhenAll(subTaskPromises);
            result.Contributions = contributions.Where(c => c is not null).ToList()!;

            // Synthesize final answer
            var synthesisPrompt = $"Original task: {task}\n\n" +
                "Partial results:\n" + string.Join("\n", result.Contributions.Select(c => $"{c.Agent.Name}: {c.Output.Truncate(200)}"));

            result.FinalAnswer = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(synthesisPrompt) },
                systemPrompt: "You are a synthesizer. Combine partial results into a complete answer.",
                options: new ChatOptions { Temperature = 0.5, MaxTokens = 3000 },
                ct: ct);

            result.IsSuccess = true;
        }
        catch (Exception ex)
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
        catch (Exception ex)
        {
            _log.Error(ex, "GCR orchestration failed");
            result.IsSuccess = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private async Task<AgentContribution?> RunAgentAsync(AgentRole agent, string task, CancellationToken ct)
    {
        try
        {
            var response = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(task) },
                systemPrompt: agent.SystemPrompt,
                options: new ChatOptions { Temperature = agent.Temperature, MaxTokens = 2000 },
                ct: ct);

            return new AgentContribution
            {
                Agent = agent,
                Output = response,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Agent {AgentName} failed", agent.Name);
            return null;
        }
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

    private static List<string> ParseSubTasks(string division)
    {
        // Accept the common bullet and numbered formats returned by the task planner prompt.
        var subTasks = new List<string>();
        var lines = division.Split('\n');
        var currentTask = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.Trim().StartsWith("-") || line.Trim().StartsWith("*") || line.Trim().StartsWith("1."))
            {
                if (currentTask.Length > 0)
                {
                    subTasks.Add(currentTask.ToString().Trim());
                    currentTask.Clear();
                }
                currentTask.Append(line.TrimStart().TrimStart('-', '*', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' '));
            }
            else if (currentTask.Length > 0)
            {
                currentTask.Append(' ').Append(line.Trim());
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
