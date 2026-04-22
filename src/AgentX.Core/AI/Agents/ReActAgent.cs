using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.AI.Agents;

/// <summary>
/// ReAct (Reasoning + Acting) agent implementation.
/// Uses a structured prompt format to guide the model through thought-action-observation cycles.
/// </summary>
public sealed partial class ReActAgent : IReActAgent
{
    private readonly IAiService _aiService;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger _log;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ReActAgent(IAiService aiService, IToolRegistry toolRegistry, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _log = logger?.ForContext<ReActAgent>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReActResult> ExecuteAsync(
        string task,
        IReadOnlyList<ToolDefinition> availableTools,
        string? systemPrompt = null,
        int maxIterations = 10,
        CancellationToken ct = default)
    {
        return await ExecuteStreamingAsync(task, availableTools, systemPrompt, maxIterations, null, ct);
    }

    /// <inheritdoc />
    public async Task<ReActResult> ExecuteStreamingAsync(
        string task,
        IReadOnlyList<ToolDefinition> availableTools,
        string? systemPrompt = null,
        int maxIterations = 10,
        Action<ReActStep>? onStep = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ReActResult { Steps = new List<ReActStep>() };

        try
        {
            var messages = new List<ChatMessage>();
            var effectiveSystemPrompt = BuildReActSystemPrompt(systemPrompt, availableTools);
            var chatOptions = new ChatOptions
            {
                Temperature = 0.7,
                MaxTokens = 2000,
                Tools = availableTools
            };

            _log.Information("Starting ReAct execution for task: {Task}", task);

            // Initial user message
            messages.Add(ChatMessage.User(task));

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                _log.Debug("ReAct iteration {Iteration}/{MaxIterations}", iteration + 1, maxIterations);

                // Get response from model
                var responseBuilder = new StringBuilder();
                List<ToolCall>? toolCalls = null;

                await foreach (var token in _aiService.StreamChatAsync(messages, effectiveSystemPrompt, chatOptions, ct))
                {
                    responseBuilder.Append(token);
                }

                var responseText = responseBuilder.ToString();

                // Check if response contains tool calls (try to parse JSON tool calls)
                toolCalls = TryExtractToolCalls(responseText);

                // Create the step
                var step = new ReActStep
                {
                    Thought = responseText,
                    Action = toolCalls?.Count > 0 ? $"Tool calls: {toolCalls.Count}" : "Final answer",
                    ToolCalls = toolCalls ?? new List<ToolCall>(),
                    Timestamp = DateTime.UtcNow,
                    IsFinal = toolCalls is null || toolCalls.Count == 0
                };

                // Notify callback
                onStep?.Invoke(step);

                // Add assistant message to conversation
                if (toolCalls?.Count > 0)
                {
                    messages.Add(ChatMessage.AssistantWithTools(toolCalls));
                }
                else
                {
                    messages.Add(ChatMessage.Assistant(responseText));
                }

                // If no tool calls, this is the final answer
                if (toolCalls is null || toolCalls.Count == 0)
                {
                    result.Answer = responseText;
                    result.IsSuccess = true;
                    result.Steps.Add(step);
                    _log.Information("ReAct completed with final answer after {Iteration} iterations", iteration + 1);
                    break;
                }

                // Execute tool calls
                foreach (var toolCall in toolCalls)
                {
                    var toolResult = await _toolRegistry.ExecuteToolAsync(toolCall, ct);
                    step.Observations.Add(toolResult);

                    // Add tool result to conversation
                    messages.Add(ChatMessage.ToolResult(toolCall.Id, toolResult.Content));

                    _log.Debug("Tool {ToolName} executed: {Success}",
                        toolCall.Name, toolResult.IsSuccess ? "Success" : "Failed");
                }

                result.Steps.Add(step);
            }

            // Check if we exhausted iterations
            if (string.IsNullOrEmpty(result.Answer) && result.Steps.Count >= maxIterations)
            {
                result.Error = $"Agent exceeded maximum iterations ({maxIterations}) without producing a final answer.";
                result.IsSuccess = false;
                _log.Warning("ReAct exceeded max iterations");
            }
        }
        catch (OperationCanceledException)
        {
            result.Error = "Execution was cancelled.";
            result.IsSuccess = false;
            _log.Information("ReAct execution cancelled");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.IsSuccess = false;
            _log.Error(ex, "ReAct execution failed");
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Builds the ReAct-specific system prompt with tool definitions and format instructions.
    /// </summary>
    private static string BuildReActSystemPrompt(string? basePrompt, IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(basePrompt))
        {
            sb.AppendLine(basePrompt);
            sb.AppendLine();
        }

        sb.AppendLine("You are a helpful assistant with access to tools.");
        sb.AppendLine("Follow this format for your responses:");
        sb.AppendLine();
        sb.AppendLine("Thought: [your reasoning about what to do next]");
        sb.AppendLine("Action: [tool name]");
        sb.AppendLine("Action Input: [JSON arguments for the tool]");
        sb.AppendLine();
        sb.AppendLine("OR when you have the final answer:");
        sb.AppendLine();
        sb.AppendLine("Thought: [your reasoning]");
        sb.AppendLine("Final Answer: [your response to the user]");
        sb.AppendLine();

        if (tools.Count > 0)
        {
            sb.AppendLine("Available tools:");
            foreach (var tool in tools)
            {
                sb.AppendLine($"- {tool.Name}: {tool.Description}");
                if (tool.Parameters?.Properties?.Count > 0)
                {
                    sb.AppendLine("  Parameters:");
                    foreach (var prop in tool.Parameters.Properties)
                    {
                        var required = tool.Parameters.Required?.Contains(prop.Key) == true;
                        sb.AppendLine($"    - {prop.Key} ({prop.Value.Type}){(required ? " required" : "")}: {prop.Value.Description ?? "No description"}");
                    }
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Attempts to extract tool calls from model response.
    /// Supports both structured JSON and parsed tool call formats.
    /// </summary>
    private static List<ToolCall>? TryExtractToolCalls(string response)
    {
        var toolCalls = new List<ToolCall>();

        // Try to find Action/Action Input pattern
        var actionMatch = ActionRegex().Match(response);
        if (actionMatch.Success)
        {
            var toolName = actionMatch.Groups["action"].Value.Trim();
            var argumentsStr = actionMatch.Groups["input"].Value.Trim();
            var toolCallId = Guid.NewGuid().ToString();

            toolCalls.Add(new ToolCall
            {
                Id = toolCallId,
                Name = toolName,
                Arguments = SanitizeJson(argumentsStr)
            });

            return toolCalls;
        }

        // Try to find JSON tool call blocks
        foreach (Match match in ToolCallRegex().Matches(response))
        {
            try
            {
                var jsonStr = match.Groups["json"].Value;
                var jsonDoc = JsonDocument.Parse(jsonStr);
                var root = jsonDoc.RootElement;

                var toolCall = new ToolCall
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Arguments = root.TryGetProperty("arguments", out var args) ? args.GetRawText() : "{}"
                };

                if (!string.IsNullOrEmpty(toolCall.Name))
                {
                    toolCalls.Add(toolCall);
                }
            }
            catch
            {
                // Skip invalid JSON
            }
        }

        return toolCalls.Count > 0 ? toolCalls : null;
    }

    /// <summary>
    /// Cleans and sanitizes JSON-like strings for parsing.
    /// </summary>
    private static string SanitizeJson(string input)
    {
        // Remove common prefixes/suffixes
        var cleaned = input.Trim();

        // If it looks like a JSON object, ensure it's properly formatted
        if (cleaned.StartsWith("{") && !cleaned.EndsWith("}"))
        {
            // Find the last }
            var lastBrace = cleaned.LastIndexOf('}');
            if (lastBrace > 0)
            {
                cleaned = cleaned.Substring(0, lastBrace + 1);
            }
        }

        // Try parsing as-is first
        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            return cleaned;
        }
        catch
        {
            // Wrap in braces if needed
            if (!cleaned.StartsWith("{"))
            {
                cleaned = $"{{ {cleaned} }}";
            }
        }

        return cleaned;
    }

    [GeneratedRegex(@"Action\s*:\s*(?<action>[^\n]+)\s*\n\s*Action Input\s*:\s*(?<input>.+?)(?:\n\s*(?:Thought|Action|Final Answer)|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ActionRegex();

    [GeneratedRegex(@"\{""name""\s*:\s*""(?<name>[^""]+)""\s*,\s*""arguments""\s*:\s*(?<json>\{[^}]*\})", RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallRegex();
}
