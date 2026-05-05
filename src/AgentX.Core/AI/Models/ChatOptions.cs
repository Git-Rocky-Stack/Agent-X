namespace AgentX.Core.AI.Models;

/// <summary>
/// A single segment of a multi-block system prompt. Used by providers that
/// support per-block prompt caching (currently Anthropic) so that a stable
/// instruction prefix can be cached while a per-request content block (e.g.
/// retrieved RAG context) is re-sent fresh on each call.
/// </summary>
/// <param name="Text">The block's text content.</param>
/// <param name="Cacheable">
/// When true and the active provider supports prompt caching, this block is
/// marked with <c>cache_control: {"type":"ephemeral"}</c>. Note that providers
/// usually impose a minimum cacheable-block length (Anthropic: 1024 tokens
/// for Sonnet/Opus, 2048 for Haiku) — sub-threshold blocks pass the marker
/// through but never produce a cache hit.
/// </param>
public sealed record SystemPromptBlock(string Text, bool Cacheable);


/// <summary>
/// Specifies the expected response format from the AI model.
/// </summary>
public enum ResponseFormat
{
    /// <summary>Default: free-form text response.</summary>
    Text = 0,

    /// <summary>
    /// Constrains the model to produce valid JSON output.
    /// Provider implementations use their native JSON mode where available.
    /// </summary>
    JsonObject = 1
}

/// <summary>
/// Configuration options for AI chat inference, controlling model behavior
/// such as temperature, token limits, and sampling parameters.
/// </summary>
public class ChatOptions
{
    /// <summary>
    /// The model identifier to use for this request. When null, the active model is used.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Controls randomness in the output. Higher values (e.g. 1.0) produce more creative
    /// responses, while lower values (e.g. 0.2) produce more deterministic responses.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum number of tokens to generate in the response.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Size of the context window used to generate the next token.
    /// </summary>
    public int ContextWindow { get; set; } = 4096;

    /// <summary>
    /// Nucleus sampling parameter. Controls the cumulative probability threshold
    /// for token selection. A value of 0.9 means the model considers tokens
    /// comprising the top 90% of probability mass.
    /// </summary>
    public double TopP { get; set; } = 0.9;

    /// <summary>
    /// Penalizes tokens based on their frequency in the generated text so far,
    /// reducing repetition of common phrases.
    /// </summary>
    public double FrequencyPenalty { get; set; }

    /// <summary>
    /// Penalizes tokens based on whether they have appeared in the generated text so far,
    /// encouraging the model to explore new topics.
    /// </summary>
    public double PresencePenalty { get; set; }

    /// <summary>
    /// Sequences that will cause the model to stop generating further tokens
    /// when encountered.
    /// </summary>
    public string[]? StopSequences { get; set; }

    /// <summary>
    /// Specifies the expected response format. When set to <see cref="Models.ResponseFormat.JsonObject"/>,
    /// the model is constrained to produce valid JSON output.
    /// </summary>
    public ResponseFormat ResponseFormat { get; set; } = ResponseFormat.Text;

    /// <summary>
    /// Tools/functions that the model can call during inference.
    /// When provided, the model may request tool calls instead of generating text directly.
    /// </summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// When true, the model must call one of the provided tools.
    /// When false, the model may choose between generating text or calling a tool.
    /// </summary>
    public bool ForceToolCall { get; set; }

    /// <summary>
    /// Optional specific tool name that must be called.
    /// When set, the model will only call this specific tool.
    /// Requires <see cref="ForceToolCall"/> to be true.
    /// </summary>
    public string? ForceToolName { get; set; }

    /// <summary>
    /// When true, providers that support prompt caching (currently Anthropic) will
    /// mark the system prompt with <c>cache_control: {"type":"ephemeral"}</c>.
    /// Reuse of an identical system prompt within ~5 minutes pays ~10% of normal
    /// input-token cost. Use for static, repeatable prompts (RAG system prompt,
    /// evaluator / reranker prompts, ReAct tool list). Providers that don't
    /// support caching ignore this flag.
    /// </summary>
    public bool CacheSystemPrompt { get; set; }

    /// <summary>
    /// Multi-block system prompt for providers that support per-block prompt
    /// caching (currently Anthropic). When non-null and non-empty, this list
    /// takes precedence over the singular <c>systemPrompt</c> parameter on the
    /// provider call: each block becomes a separate text segment with optional
    /// <c>cache_control</c>. Use to split a stable instruction prefix
    /// (Cacheable=true) from per-request content like retrieved RAG context
    /// (Cacheable=false) so the prefix is reused from cache across turns.
    /// Providers that don't support multi-block system prompts ignore this
    /// property — callers must still pass a concatenated <c>systemPrompt</c>
    /// string for graceful degradation on those providers.
    /// </summary>
    public IReadOnlyList<SystemPromptBlock>? SystemPromptBlocks { get; set; }

    /// <summary>
    /// FU-5: structured-output schema enforcement. When non-null, providers that
    /// support JSON Schema response formatting (currently OpenAI's
    /// <c>response_format: json_schema</c> with <c>strict: true</c>) constrain
    /// the model's output to match this schema at decode time — rejecting
    /// truncations and missing required fields server-side rather than
    /// surfacing them as parse errors on the client. The string value MUST be
    /// a valid JSON-Schema document. Providers that don't support
    /// <c>json_schema</c> fall back to plain <see cref="ResponseFormat.JsonObject"/>
    /// constraint and rely on the client's hand-rolled deserializer for
    /// schema fidelity.
    /// </summary>
    public string? JsonSchema { get; set; }

    /// <summary>
    /// FU-5: human-readable name for the schema, surfaced as
    /// <c>response_format.json_schema.name</c> on OpenAI. Should be a stable,
    /// alphanumeric identifier (e.g. <c>"rag_eval_metrics"</c>). Required when
    /// <see cref="JsonSchema"/> is set on OpenAI; ignored elsewhere.
    /// </summary>
    public string? JsonSchemaName { get; set; }
}
