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
                    result.IsSuccess = !string.IsNullOrEmpty(debateResult.Synthesis);
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

        var outputs = results.Where(r => r is not null).ToList()!;
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
            IsSuccess = !string.IsNullOrEmpty(parallelResult.CombinedOutput)
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

    private static async Task<(string combined, string? consensus, List<string> disagreements)> SynthesizeParallelOutputsAsync(
        string task, List<AgentContribution> outputs, CancellationToken ct)
    {
        // For now, just concatenate. In a full implementation, this would use AI to synthesize
        var combined = string.Join("\n\n---\n\n", outputs.Select(o => $"[{o.Agent.Name}]\n{o.Output}"));
        return (combined, null, new List<string>());
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

    private static async Task<string> SynthesizeDebateAsync(string topic, DebateResult result, CancellationToken ct)
    {
        // This would use AI to synthesize - for now returning a placeholder
        var sb = new StringBuilder();
        sb.AppendLine($"Debate on: {topic}");
        sb.AppendLine($"Rounds: {result.Rounds.Count}");
        sb.AppendLine("Key positions:");
        foreach (var round in result.Rounds)
        {
            foreach (var pos in round.Positions)
            {
                sb.AppendLine($"- {pos.Agent.Name}: {pos.Argument.Truncate(100)}...");
            }
        }
        return sb.ToString();
    }

    private static string? IdentifyWinningPerspective(DebateResult result)
    {
        // Simple heuristic - in a full implementation, this would use more sophisticated analysis
        if (result.Rounds.Count == 0) return null;
        return result.Rounds[^1].Positions.FirstOrDefault()?.Agent.Name;
    }

    private static List<string> ParseSubTasks(string division)
    {
        // Simple parsing - in production, use structured output
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
}
