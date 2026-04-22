namespace AgentX.Core.AI.Agents;

/// <summary>
/// Orchestrates multiple specialized agents to collaborate on complex tasks.
/// Supports sequential, parallel, and debate strategies.
/// </summary>
public interface IMultiAgentOrchestrator
{
    /// <summary>
    /// Runs a task using multiple coordinated agents.
    /// </summary>
    /// <param name="task">The task to accomplish.</param>
    /// <param name="agents">The agents to use.</param>
    /// <param name="strategy">The orchestration strategy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The orchestration result with final answer and agent contributions.</returns>
    Task<OrchestrationResult> RunAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        OrchestratorStrategy strategy = OrchestratorStrategy.Sequential,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a debate between multiple agents with different perspectives.
    /// </summary>
    /// <param name="task">The topic or question to debate.</param>
    /// <param name="agents">Agents with different perspectives.</param>
    /// <param name="rounds">Number of debate rounds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The debate result with final synthesis.</returns>
    Task<DebateResult> RunDebateAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        int rounds = 2,
        CancellationToken ct = default);

    /// <summary>
    /// Runs agents in parallel and combines their outputs.
    /// </summary>
    /// <param name="task">The task for each agent.</param>
    /// <param name="agents">Agents to run in parallel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The combined result.</returns>
    Task<ParallelResult> RunParallelAsync(
        string task,
        IReadOnlyList<AgentRole> agents,
        CancellationToken ct = default);
}

/// <summary>
/// Strategies for orchestrating multiple agents.
/// </summary>
public enum OrchestratorStrategy
{
    /// <summary>Agents run one after another, each building on previous work.</summary>
    Sequential,

    /// <summary>Agents run simultaneously, results are combined at the end.</summary>
    Parallel,

    /// <summary>Agents debate and critique each other's outputs.</summary>
    Debate,

    /// <summary>Specialist agents each handle a part of the task.</summary>
    DivideAndConquer,

    /// <summary>One agent generates, another critiques, a third refines.</summary>
    GenerateCritiqueRefine
}

/// <summary>
/// Defines a specialized agent role in the orchestration.
/// </summary>
public class AgentRole
{
    /// <summary>
    /// Unique identifier for this agent role.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the agent.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The agent's specialty or expertise.
    /// </summary>
    public string Expertise { get; set; } = string.Empty;

    /// <summary>
    /// System prompt that defines the agent's behavior and perspective.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// The agent's temperature (lower=more focused, higher=more creative).
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Whether this agent should provide citations for its claims.
    /// </summary>
    public bool RequiresCitations { get; set; }

    /// <summary>
    /// Creates a researcher agent for factual information gathering.
    /// </summary>
    public static AgentRole Researcher()
    {
        return new AgentRole
        {
            Id = "researcher",
            Name = "Researcher",
            Expertise = "Gathering and verifying factual information",
            SystemPrompt = "You are a meticulous researcher. Gather accurate information, cite sources, and verify claims. Focus on facts and evidence.",
            Temperature = 0.3,
            RequiresCitations = true
        };
    }

    /// <summary>
    /// Creates a critic agent for evaluating and critiquing content.
    /// </summary>
    public static AgentRole Critic()
    {
        return new AgentRole
        {
            Id = "critic",
            Name = "Critic",
            Expertise = "Evaluating arguments and identifying weaknesses",
            SystemPrompt = "You are a thoughtful critic. Identify logical fallacies, weak arguments, unsupported claims, and areas needing improvement. Be constructive but thorough.",
            Temperature = 0.5,
            RequiresCitations = false
        };
    }

    /// <summary>
    /// Creates a synthesizer agent for combining multiple perspectives.
    /// </summary>
    public static AgentRole Synthesizer()
    {
        return new AgentRole
        {
            Id = "synthesizer",
            Name = "Synthesizer",
            Expertise = "Combining multiple viewpoints into coherent conclusions",
            SystemPrompt = "You are a skilled synthesizer. Combine multiple perspectives into a balanced, comprehensive conclusion. Acknowledge trade-offs and present a nuanced view.",
            Temperature = 0.6,
            RequiresCitations = false
        };
    }

    /// <summary>
    /// Creates a creative agent for brainstorming and ideation.
    /// </summary>
    public static AgentRole Creative()
    {
        return new AgentRole
        {
            Id = "creative",
            Name = "Creative Thinker",
            Expertise = "Generating novel ideas and unconventional approaches",
            SystemPrompt = "You are a creative thinker. Generate novel ideas, explore unconventional approaches, and think outside the box. Don't be constrained by conventional wisdom.",
            Temperature = 0.9,
            RequiresCitations = false
        };
    }

    /// <summary>
    /// Creates a technical expert agent.
    /// </summary>
    public static AgentRole TechnicalExpert(string domain)
    {
        return new AgentRole
        {
            Id = $"expert_{domain.ToLowerInvariant()}",
            Name = $"{domain} Expert",
            Expertise = $"Specialized knowledge in {domain}",
            SystemPrompt = $"You are an expert in {domain}. Provide technical depth, accurate terminology, and domain-specific best practices.",
            Temperature = 0.4,
            RequiresCitations = true
        };
    }
}

/// <summary>
/// Result from a multi-agent orchestration.
/// </summary>
public class OrchestrationResult
{
    /// <summary>
    /// The task that was orchestrated.
    /// </summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>
    /// The strategy used.
    /// </summary>
    public OrchestratorStrategy Strategy { get; set; }

    /// <summary>
    /// The final answer produced.
    /// </summary>
    public string FinalAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Individual agent contributions.
    /// </summary>
    public List<AgentContribution> Contributions { get; set; } = new();

    /// <summary>
    /// Total time taken.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether orchestration completed successfully.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Any errors that occurred.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// A single agent's contribution to the orchestrated result.
/// </summary>
public class AgentContribution
{
    /// <summary>
    /// The agent role that contributed.
    /// </summary>
    public AgentRole Agent { get; set; } = new();

    /// <summary>
    /// The agent's output.
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// When this contribution was made.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Time taken for this agent to produce the output.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of a debate between agents.
/// </summary>
public class DebateResult
{
    /// <summary>
    /// The topic debated.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// All debate rounds with agent positions.
    /// </summary>
    public List<DebateRound> Rounds { get; set; } = new();

    /// <summary>
    /// Final synthesis of all perspectives.
    /// </summary>
    public string Synthesis { get; set; } = string.Empty;

    /// <summary>
    /// The winning perspective (if any clear winner).
    /// </summary>
    public string? WinningPerspective { get; set; }
}

/// <summary>
/// A single round of debate.
/// </summary>
public class DebateRound
{
    /// <summary>
    /// Round number.
    /// </summary>
    public int RoundNumber { get; set; }

    /// <summary>
    /// Each agent's position in this round.
    /// </summary>
    public List<DebatePosition> Positions { get; set; } = new();
}

/// <summary>
/// An agent's position in a debate round.
/// </summary>
public class DebatePosition
{
    /// <summary>
    /// The agent presenting this position.
    /// </summary>
    public AgentRole Agent { get; set; } = new();

    /// <summary>
    /// The agent's argument or position.
    /// </summary>
    public string Argument { get; set; } = string.Empty;

    /// <summary>
    /// Any points raised against other agents.
    /// </summary>
    public List<string> Counterpoints { get; set; } = new();
}

/// <summary>
/// Result of parallel agent execution.
/// </summary>
public class ParallelResult
{
    /// <summary>
    /// The task given to all agents.
    /// </summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>
    /// Individual agent outputs.
    /// </summary>
    public List<AgentContribution> Outputs { get; set; } = new();

    /// <summary>
    /// Combined result from all agents.
    /// </summary>
    public string CombinedOutput { get; set; } = string.Empty;

    /// <summary>
    /// Any consensus found between agents.
    /// </summary>
    public string? Consensus { get; set; }

    /// <summary>
    /// Points of disagreement between agents.
    /// </summary>
    public List<string> Disagreements { get; set; } = new();
}
