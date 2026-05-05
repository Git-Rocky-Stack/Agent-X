using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentX.Core.AI.Models;
using AgentX.Core.Observability;
using Serilog;

namespace AgentX.Core.AI.Agents;

/// <summary>
/// Implementation of structured reasoning service.
/// </summary>
public sealed partial class ReasoningService : IReasoningService
{
    private readonly IAiService _aiService;
    private readonly ILogger _log;

    public ReasoningService(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _log = logger?.ForContext<ReasoningService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReasoningChain> GenerateChainOfThoughtAsync(
        string query,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _log.Debug("Generating chain of thought for query");

        var prompt = BuildCoTPrompt(query);

        try
        {
            var response = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(prompt) },
                systemPrompt: CoTSystemPrompt,
                options: new ChatOptions { Temperature = 0.7, MaxTokens = 3000 },
                ct: ct);

            var chain = ParseCoTResponse(query, response);
            chain.Duration = stopwatch.Elapsed;

            _log.Information("CoT generated: {StepCount} steps, confidence={Confidence}",
                chain.Steps.Count, chain.Confidence);

            return chain;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "CoT generation failed");
            return new ReasoningChain
            {
                Query = query,
                Steps = new(),
                Conclusion = string.Empty,
                Confidence = 0,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<TreeOfThoughts> GenerateTreeOfThoughtsAsync(
        string query,
        int branchCount = 3,
        CancellationToken ct = default)
    {
        _log.Debug("Generating tree of thoughts with {BranchCount} branches", branchCount);

        var approaches = new[]
        {
            "Analytical: Break down the problem systematically",
            "Creative: Consider unconventional solutions",
            "Skeptical: Challenge assumptions and verify claims",
            "Practical: Focus on actionable solutions",
            "Comprehensive: Consider all angles and perspectives"
        };

        var branches = new List<ReasoningBranch>();
        var selectedApproaches = approaches.Take(branchCount).ToList();

        for (int i = 0; i < branchCount; i++)
        {
            var branchPrompt = BuildBranchPrompt(query, selectedApproaches[i]);

            try
            {
                var response = await _aiService.ChatAsync(
                    messages: new List<ChatMessage> { ChatMessage.User(branchPrompt) },
                    systemPrompt: "You are an expert reasoner exploring a specific approach to a problem.",
                    options: new ChatOptions { Temperature = 0.8, MaxTokens = 2000 },
                    ct: ct);

                branches.Add(new ReasoningBranch
                {
                    BranchId = i,
                    Approach = selectedApproaches[i],
                    Steps = new List<ReasoningStep>
                    {
                        new ReasoningStep
                        {
                            StepNumber = 1,
                            Thought = response,
                            ReasoningType = selectedApproaches[i].Split(':')[0]
                        }
                    },
                    Conclusion = ExtractConclusion(response),
                    Confidence = 0.7
                });
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Branch {BranchId} generation failed", i);
            }
        }

        // Synthesize branches
        var synthesisPrompt = BuildSynthesisPrompt(query, branches);

        try
        {
            var synthesis = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(synthesisPrompt) },
                systemPrompt: "You are an expert at synthesizing multiple perspectives into a coherent conclusion.",
                options: new ChatOptions { Temperature = 0.5, MaxTokens = 2000 },
                ct: ct);

            var result = new TreeOfThoughts
            {
                Query = query,
                Branches = branches,
                Synthesis = synthesis,
                BestBranch = branches.OrderByDescending(b => b.Confidence).FirstOrDefault()
            };

            // Score branches
            foreach (var branch in branches)
            {
                result.BranchScores[branch.BranchId] = branch.Confidence;
            }

            _log.Information("Tree of thoughts generated: {BranchCount} branches", branches.Count);
            return result;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Synthesis failed");
            return new TreeOfThoughts { Query = query, Branches = branches };
        }
    }

    /// <inheritdoc />
    public async Task<ProblemDecomposition> DecomposeProblemAsync(
        string query,
        CancellationToken ct = default)
    {
        _log.Debug("Decomposing problem");

        var prompt = BuildDecompositionPrompt(query);

        try
        {
            var response = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(prompt) },
                systemPrompt: "You are an expert at breaking down complex problems into manageable parts.",
                options: new ChatOptions
                {
                    Temperature = 0.5,
                    MaxTokens = 2500,
                    ResponseFormat = ResponseFormat.JsonObject
                },
                ct: ct);

            var decomposition = ParseDecomposition(query, response);
            _log.Information("Problem decomposed into {Count} sub-problems", decomposition.SubProblems.Count);
            return decomposition;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Decomposition failed");
            return new ProblemDecomposition { OriginalProblem = query };
        }
    }

    /// <inheritdoc />
    public async Task<DecomposedSolution> SolveByDecompositionAsync(
        string query,
        CancellationToken ct = default)
    {
        _log.Information("Solving by decomposition");

        var decomposition = await DecomposeProblemAsync(query, ct);
        var solution = new DecomposedSolution
        {
            OriginalProblem = query,
            Decomposition = decomposition,
            Solutions = new Dictionary<int, string>(),
            FailedSubProblems = new List<int>()
        };

        // Solve sub-problems in recommended order
        foreach (var subProblemId in decomposition.RecommendedOrder)
        {
            var subProblem = decomposition.SubProblems.FirstOrDefault(sp => sp.Id == subProblemId);
            if (subProblem is null) continue;

            try
            {
                var solvePrompt = $"Solve this specific problem:\n\n{subProblem.Description}\n\nProvide a clear, concise solution.";

                var subSolution = await _aiService.ChatAsync(
                    messages: new List<ChatMessage> { ChatMessage.User(solvePrompt) },
                    systemPrompt: "You are an expert problem solver. Provide clear, actionable solutions.",
                    options: new ChatOptions { Temperature = 0.6, MaxTokens = 1500 },
                    ct: ct);

                solution.Solutions[subProblemId] = subSolution;
                subProblem.Solution = subSolution;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to solve sub-problem {Id}", subProblemId);
                solution.FailedSubProblems.Add(subProblemId);
            }
        }

        // Synthesize final answer
        var synthesisPrompt = BuildSolutionSynthesisPrompt(query, decomposition, solution.Solutions);

        try
        {
            solution.FinalAnswer = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(synthesisPrompt) },
                systemPrompt: "You are an expert at combining partial solutions into a comprehensive answer.",
                options: new ChatOptions { Temperature = 0.5, MaxTokens = 3000 },
                ct: ct);

            solution.IsComplete = solution.FailedSubProblems.Count == 0;
            _log.Information("Decomposition solving complete: {Success}/{Total} sub-problems solved",
                solution.Solutions.Count, decomposition.SubProblems.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Final synthesis failed");
            solution.IsComplete = false;
        }

        return solution;
    }

    private const string CoTSystemPrompt =
        @"You are an expert at step-by-step reasoning. Break down problems systematically, showing your thinking clearly.
        Use this format:
        Step 1: [Your thought]
        Observation: [What you notice or conclude]
        Step 2: [Your next thought]
        ...and so on until:
        Conclusion: [Your final answer with confidence level]";

    private static string BuildCoTPrompt(string query) =>
        $"Think through this problem step by step:\n\n{query}\n\nShow your reasoning clearly and provide a confident conclusion.";

    private static string BuildBranchPrompt(string query, string approach) =>
        $"Using this approach: {approach}\n\nThink through this problem:\n{query}\n\nProvide your reasoning and conclusion from this perspective.";

    private static string BuildSynthesisPrompt(string query, List<ReasoningBranch> branches)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Synthesize these different perspectives into a coherent answer.");
        sb.AppendLine();
        sb.AppendLine("Original Question:");
        sb.AppendLine(query);
        sb.AppendLine();
        sb.AppendLine("Different Perspectives:");

        foreach (var branch in branches)
        {
            sb.AppendLine($"- {branch.Approach}:");
            sb.AppendLine(branch.Conclusion);
            sb.AppendLine();
        }

        sb.AppendLine("Provide a synthesis that captures the best insights from each perspective.");
        return sb.ToString();
    }

    private static string BuildDecompositionPrompt(string query) =>
        $@"Break down this complex problem into 3-7 smaller sub-problems:

{query}

Respond with JSON in this format:
{{
  ""subProblems"": [
    {{
      ""id"": 1,
      ""description"": ""specific sub-problem"",
      ""type"": ""factual|analytical|creative"",
      ""difficulty"": 1-5
    }}
  ],
  ""dependencies"": [
    {{
      ""prerequisite"": 1,
      ""dependent"": 2,
      ""reason"": ""why 1 must be solved before 2""
    }}
  ],
  ""recommendedOrder"": [1, 2, 3]
}}";

    private static string BuildSolutionSynthesisPrompt(
        string query,
        ProblemDecomposition decomposition,
        Dictionary<int, string> solutions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Combine these sub-problem solutions into a comprehensive answer:");
        sb.AppendLine();
        sb.AppendLine($"Original: {query}");
        sb.AppendLine();
        sb.AppendLine("Sub-problem solutions:");

        foreach (var kvp in solutions)
        {
            var subProblem = decomposition.SubProblems.FirstOrDefault(sp => sp.Id == kvp.Key);
            sb.AppendLine($"{kvp.Key}. {subProblem?.Description ?? "Unknown"}");
            sb.AppendLine($"   Solution: {kvp.Value.Truncate(300)}...");
            sb.AppendLine();
        }

        sb.AppendLine("Provide a complete, well-structured answer to the original problem.");
        return sb.ToString();
    }

    private static ReasoningChain ParseCoTResponse(string query, string response)
    {
        var chain = new ReasoningChain { Query = query };
        var lines = response.Split('\n');

        int stepNum = 0;
        StringBuilder currentStep = new();
        string? currentObservation = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"{stepNum + 1}.", StringComparison.OrdinalIgnoreCase))
            {
                if (currentStep.Length > 0)
                {
                    chain.Steps.Add(new ReasoningStep
                    {
                        StepNumber = ++stepNum,
                        Thought = currentStep.ToString().Trim(),
                        Observation = currentObservation
                    });
                    currentStep.Clear();
                    currentObservation = null;
                }

                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    currentStep.Append(trimmed[(colonIdx + 1)..].Trim());
                }
            }
            else if (trimmed.StartsWith("Observation:", StringComparison.OrdinalIgnoreCase))
            {
                currentObservation = trimmed["Observation:".Length..].Trim();
            }
            else if (trimmed.StartsWith("Conclusion:", StringComparison.OrdinalIgnoreCase))
            {
                chain.Conclusion = trimmed["Conclusion:".Length..].Trim();

                // Extract confidence if present
                var confidenceMatch = ConfidenceRegex().Match(chain.Conclusion);
                if (confidenceMatch.Success)
                {
                    chain.Conclusion = chain.Conclusion[..confidenceMatch.Index].Trim();
                    if (double.TryParse(confidenceMatch.Groups[1].Value, out var conf))
                    {
                        chain.Confidence = conf / 100.0;
                    }
                }
            }
            else if (currentStep.Length > 0)
            {
                currentStep.Append(' ').Append(trimmed);
            }
        }

        // Add final step if present
        if (currentStep.Length > 0)
        {
            chain.Steps.Add(new ReasoningStep
            {
                StepNumber = ++stepNum,
                Thought = currentStep.ToString().Trim(),
                Observation = currentObservation
            });
        }

        // Default confidence if not found
        if (chain.Confidence == 0 && chain.Steps.Count > 0)
        {
            chain.Confidence = 0.7;
        }

        return chain;
    }

    private ProblemDecomposition ParseDecomposition(string query, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var decomposition = new ProblemDecomposition { OriginalProblem = query };

            if (root.TryGetProperty("subProblems", out var subProblems))
            {
                foreach (var sp in subProblems.EnumerateArray())
                {
                    decomposition.SubProblems.Add(new SubProblem
                    {
                        Id = sp.TryGetProperty("id", out var id) ? id.GetInt32() : decomposition.SubProblems.Count + 1,
                        Description = sp.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        ProblemType = sp.TryGetProperty("type", out var type) ? type.GetString() ?? "analytical" : "analytical",
                        Difficulty = sp.TryGetProperty("difficulty", out var diff) ? diff.GetInt32() : 3
                    });
                }
            }

            if (root.TryGetProperty("dependencies", out var deps))
            {
                foreach (var dep in deps.EnumerateArray())
                {
                    decomposition.Dependencies.Add(new ProblemDependency
                    {
                        PrerequisiteId = dep.TryGetProperty("prerequisite", out var pre) ? pre.GetInt32() : 0,
                        DependentId = dep.TryGetProperty("dependent", out var dependent) ? dependent.GetInt32() : 0,
                        Reason = dep.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty
                    });
                }
            }

            if (root.TryGetProperty("recommendedOrder", out var order))
            {
                decomposition.RecommendedOrder = order.EnumerateArray().Select(e => e.GetInt32()).ToList();
            }

            return decomposition;
        }
        catch (Exception ex)
        {
            // Emit a redacted summary (P2-10) so operators can group failures by
            // hash without exposing chunk content the model may have echoed back.
            _log.Warning(ex,
                "Failed to parse problem decomposition JSON; returning empty decomposition. Response summary: {Summary}",
                LogRedaction.ForLog(json));
            return new ProblemDecomposition { OriginalProblem = query };
        }
    }

    private static string ExtractConclusion(string response)
    {
        var lines = response.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("Step", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }
        return response.Truncate(200);
    }

    [GeneratedRegex(@"(\d+)%?\s*confidence", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidenceRegex();
}
