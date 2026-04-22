namespace AgentX.Core.AI.Agents;

/// <summary>
/// Provides structured reasoning capabilities for complex problem-solving.
/// </summary>
public interface IReasoningService
{
    /// <summary>
    /// Generates a step-by-step chain of thought reasoning for the given query.
    /// </summary>
    /// <param name="query">The question or problem to reason through.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A reasoning chain with individual steps and the final conclusion.</returns>
    Task<ReasoningChain> GenerateChainOfThoughtAsync(
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Generates reasoning using multiple parallel thought paths (tree-of-thought).
    /// </summary>
    /// <param name="query">The question or problem.</param>
    /// <param name="branchCount">Number of parallel reasoning branches.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Multiple reasoning branches with synthesis.</returns>
    Task<TreeOfThoughts> GenerateTreeOfThoughtsAsync(
        string query,
        int branchCount = 3,
        CancellationToken ct = default);

    /// <summary>
    /// Decomposes a complex problem into smaller sub-problems.
    /// </summary>
    /// <param name="query">The complex problem to decompose.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A problem decomposition with sub-problems and solving order.</returns>
    Task<ProblemDecomposition> DecomposeProblemAsync(
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Solves a complex problem by decomposing it first, then solving each part.
    /// </summary>
    /// <param name="query">The complex problem.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete solution with all sub-problems resolved.</returns>
    Task<DecomposedSolution> SolveByDecompositionAsync(
        string query,
        CancellationToken ct = default);
}

/// <summary>
/// Represents a chain of reasoning steps leading to a conclusion.
/// </summary>
public class ReasoningChain
{
    /// <summary>
    /// The original query or problem.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Individual reasoning steps in order.
    /// </summary>
    public List<ReasoningStep> Steps { get; set; } = new();

    /// <summary>
    /// The final conclusion or answer.
    /// </summary>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>
    /// Confidence in the conclusion (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Total time taken to generate the reasoning.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// A single step in a reasoning chain.
/// </summary>
public class ReasoningStep
{
    /// <summary>
    /// Step number or identifier.
    /// </summary>
    public int StepNumber { get; set; }

    /// <summary>
    /// The thought or reasoning in this step.
    /// </summary>
    public string Thought { get; set; } = string.Empty;

    /// <summary>
    /// Any intermediate conclusion or observation.
    /// </summary>
    public string? Observation { get; set; }

    /// <summary>
    /// Reason for taking this step or the type of reasoning used.
    /// </summary>
    public string? ReasoningType { get; set; }
}

/// <summary>
/// Tree-of-thoughts with multiple reasoning branches.
/// </summary>
public class TreeOfThoughts
{
    /// <summary>
    /// The original query.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Parallel reasoning branches.
    /// </summary>
    public List<ReasoningBranch> Branches { get; set; } = new();

    /// <summary>
    /// Synthesis combining insights from all branches.
    /// </summary>
    public string Synthesis { get; set; } = string.Empty;

    /// <summary>
    /// The most promising branch based on evaluation.
    /// </summary>
    public ReasoningBranch? BestBranch { get; set; }

    /// <summary>
    /// Evaluation scores for each branch.
    /// </summary>
    public Dictionary<int, double> BranchScores { get; set; } = new();
}

/// <summary>
/// A single reasoning branch in tree-of-thoughts.
/// </summary>
public class ReasoningBranch
{
    /// <summary>
    /// Branch identifier.
    /// </summary>
    public int BranchId { get; set; }

    /// <summary>
    /// The reasoning approach for this branch.
    /// </summary>
    public string Approach { get; set; } = string.Empty;

    /// <summary>
    /// Reasoning steps in this branch.
    /// </summary>
    public List<ReasoningStep> Steps { get; set; } = new();

    /// <summary>
    /// The conclusion reached by this branch.
    /// </summary>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score for this branch.
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Result of decomposing a complex problem.
/// </summary>
public class ProblemDecomposition
{
    /// <summary>
    /// The original complex problem.
    /// </summary>
    public string OriginalProblem { get; set; } = string.Empty;

    /// <summary>
    /// Identified sub-problems.
    /// </summary>
    public List<SubProblem> SubProblems { get; set; } = new();

    /// <summary>
    /// Dependencies between sub-problems (which must be solved before others).
    /// </summary>
    public List<ProblemDependency> Dependencies { get; set; } = new();

    /// <summary>
    /// Recommended solving order.
    /// </summary>
    public List<int> RecommendedOrder { get; set; } = new();
}

/// <summary>
/// A sub-problem from decomposition.
/// </summary>
public class SubProblem
{
    /// <summary>
    /// Sub-problem identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The sub-problem description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Type of sub-problem (factual, analytical, creative, etc.).
    /// </summary>
    public string ProblemType { get; set; } = string.Empty;

    /// <summary>
    /// Estimated difficulty (1-5).
    /// </summary>
    public int Difficulty { get; set; }

    /// <summary>
    /// The solution once found.
    /// </summary>
    public string? Solution { get; set; }
}

/// <summary>
/// Dependency between two sub-problems.
/// </summary>
public class ProblemDependency
{
    /// <summary>
    /// The sub-problem that must be solved first.
    /// </summary>
    public int PrerequisiteId { get; set; }

    /// <summary>
    /// The sub-problem that depends on the prerequisite.
    /// </summary>
    public int DependentId { get; set; }

    /// <summary>
    /// Description of why this dependency exists.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of solving a problem via decomposition.
/// </summary>
public class DecomposedSolution
{
    /// <summary>
    /// The original problem.
    /// </summary>
    public string OriginalProblem { get; set; } = string.Empty;

    /// <summary>
    /// The decomposition used.
    /// </summary>
    public ProblemDecomposition Decomposition { get; set; } = new();

    /// <summary>
    /// Solutions for each sub-problem.
    /// </summary>
    public Dictionary<int, string> Solutions { get; set; } = new();

    /// <summary>
    /// The synthesized final answer.
    /// </summary>
    public string FinalAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Whether all sub-problems were solved successfully.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Any sub-problems that failed to solve.
    /// </summary>
    public List<int> FailedSubProblems { get; set; } = new();
}
