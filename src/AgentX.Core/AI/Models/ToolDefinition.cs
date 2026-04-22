using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentX.Core.AI.Models;

/// <summary>
/// Defines a tool or function that can be called by the AI model during inference.
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// Unique identifier for the tool (e.g., "search_web", "get_weather").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what the tool does.
    /// The model uses this to decide when to call the tool.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema defining the parameters expected by this tool.
    /// </summary>
    public ToolParameterSchema? Parameters { get; set; }

    /// <summary>
    /// Optional handler function that executes the tool logic.
    /// When null, the tool call is returned to the caller for execution.
    /// </summary>
    public Func<JsonElement, Task<ToolResult>>? Handler { get; set; }
}

/// <summary>
/// JSON Schema definition for tool parameters.
/// </summary>
public class ToolParameterSchema
{
    /// <summary>
    /// The JSON type of the parameters (usually "object").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    /// <summary>
    /// Required parameter names.
    /// </summary>
    [JsonPropertyName("required")]
    public string[]? Required { get; set; }

    /// <summary>
    /// Property definitions mapping parameter names to their schemas.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; set; } = new();
}

/// <summary>
/// Schema definition for a single property.
/// </summary>
public class PropertySchema
{
    /// <summary>
    /// The JSON type of this property (string, number, boolean, array, object).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>
    /// Human-readable description of the property.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// For enum types, the allowed values.
    /// </summary>
    [JsonPropertyName("enum")]
    public string[]? Enum { get; set; }

    /// <summary>
    /// For array types, the schema of array items.
    /// </summary>
    [JsonPropertyName("items")]
    public PropertySchema? Items { get; set; }
}

/// <summary>
/// Represents a tool/function call requested by the AI model.
/// </summary>
public class ToolCall
{
    /// <summary>
    /// Unique identifier for this tool call (for matching responses).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the tool/function to call.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The arguments to pass to the tool, as a JSON string.
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Parsed arguments as a JsonElement for easier access.
    /// </summary>
    [JsonIgnore]
    public JsonElement ParsedArguments
    {
        get
        {
            if (string.IsNullOrEmpty(Arguments))
                return default;

            try
            {
                using var doc = JsonDocument.Parse(Arguments);
                return doc.RootElement.Clone();
            }
            catch
            {
                return default;
            }
        }
    }
}

/// <summary>
/// Result returned from executing a tool.
/// </summary>
public class ToolResult
{
    /// <summary>
    /// The tool call ID this result corresponds to.
    /// </summary>
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>
    /// The result content (text or structured data).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the tool execution was successful.
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Optional metadata about the result (e.g., tokens used, execution time).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Creates a successful tool result.
    /// </summary>
    public static ToolResult Success(string content, string toolCallId = "")
    {
        return new ToolResult
        {
            ToolCallId = toolCallId,
            Content = content,
            IsSuccess = true
        };
    }

    /// <summary>
    /// Creates a failed tool result.
    /// </summary>
    public static ToolResult Failure(string error, string toolCallId = "")
    {
        return new ToolResult
        {
            ToolCallId = toolCallId,
            Content = string.Empty,
            IsSuccess = false,
            Error = error
        };
    }
}

/// <summary>
/// Represents a single step in a ReAct (Reasoning + Acting) loop.
/// </summary>
public class ReActStep
{
    /// <summary>
    /// The thought/reasoning output by the model.
    /// </summary>
    public string Thought { get; set; } = string.Empty;

    /// <summary>
    /// The action the model decided to take (tool call or final answer).
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The tool calls made in this step (if any).
    /// </summary>
    public List<ToolCall> ToolCalls { get; set; } = new();

    /// <summary>
    /// The observations/results from tool execution.
    /// </summary>
    public List<ToolResult> Observations { get; set; } = new();

    /// <summary>
    /// Timestamp when this step occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this step produced the final answer.
    /// </summary>
    public bool IsFinal { get; set; }
}

/// <summary>
/// Result of a complete ReAct agent execution.
/// </summary>
public class ReActResult
{
    /// <summary>
    /// The final answer produced by the agent.
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// All reasoning steps taken by the agent.
    /// </summary>
    public List<ReActStep> Steps { get; set; } = new();

    /// <summary>
    /// Total execution time.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Number of tool calls made.
    /// </summary>
    public int ToolCallCount => Steps.Sum(s => s.ToolCalls.Count);

    /// <summary>
    /// Whether the agent completed successfully.
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// Error message if the agent failed.
    /// </summary>
    public string? Error { get; set; }
}
