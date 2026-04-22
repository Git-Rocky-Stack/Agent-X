using System.Text.Json;
using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// Registry for managing tools that can be called by AI models during inference.
/// Provides registration, lookup, and execution capabilities for functions.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all registered tool definitions.
    /// </summary>
    IReadOnlyList<ToolDefinition> RegisteredTools { get; }

    /// <summary>
    /// Registers a new tool that can be called by the AI model.
    /// </summary>
    /// <param name="tool">The tool definition to register.</param>
    /// <exception cref="ArgumentException">Thrown if a tool with the same name already exists.</exception>
    void RegisterTool(ToolDefinition tool);

    /// <summary>
    /// Unregisters a previously registered tool.
    /// </summary>
    /// <param name="toolName">The name of the tool to unregister.</param>
    /// <returns>True if the tool was found and removed; false otherwise.</returns>
    bool UnregisterTool(string toolName);

    /// <summary>
    /// Retrieves a tool definition by name.
    /// </summary>
    /// <param name="toolName">The name of the tool to retrieve.</param>
    /// <returns>The tool definition, or null if not found.</returns>
    ToolDefinition? GetTool(string toolName);

    /// <summary>
    /// Checks whether a tool with the given name is registered.
    /// </summary>
    /// <param name="toolName">The name of the tool to check.</param>
    /// <returns>True if the tool is registered; false otherwise.</returns>
    bool HasTool(string toolName);

    /// <summary>
    /// Executes a tool call by name with the provided arguments.
    /// </summary>
    /// <param name="toolName">The name of the tool to execute.</param>
    /// <param name="arguments">The arguments to pass to the tool (as JSON).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the tool execution.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the tool is not registered or has no handler.</exception>
    Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default);

    /// <summary>
    /// Executes a tool call directly from a ToolCall object.
    /// </summary>
    /// <param name="toolCall">The tool call to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the tool execution.</returns>
    Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct = default);
}

/// <summary>
/// Default implementation of IToolRegistry with thread-safe registration and execution.
/// </summary>
public sealed class ToolRegistry : IToolRegistry, IDisposable
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyList<ToolDefinition> RegisteredTools
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _tools.Values.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public void RegisterTool(ToolDefinition tool)
    {
        if (tool is null)
            throw new ArgumentNullException(nameof(tool));

        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("Tool name cannot be empty.", nameof(tool));

        _lock.EnterWriteLock();
        try
        {
            if (_tools.ContainsKey(tool.Name))
                throw new ArgumentException($"Tool '{tool.Name}' is already registered.");

            _tools[tool.Name] = tool;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool UnregisterTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        _lock.EnterWriteLock();
        try
        {
            return _tools.Remove(toolName);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public ToolDefinition? GetTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        _lock.EnterReadLock();
        try
        {
            return _tools.TryGetValue(toolName, out var tool) ? tool : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public bool HasTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        _lock.EnterReadLock();
        try
        {
            return _tools.ContainsKey(toolName);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        var tool = GetTool(toolName);
        if (tool is null)
            return ToolResult.Failure($"Tool '{toolName}' is not registered.");

        if (tool.Handler is null)
            return ToolResult.Failure($"Tool '{toolName}' has no handler configured.");

        try
        {
            return await tool.Handler(arguments).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Failure($"Tool '{toolName}' execution was cancelled.");
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Tool '{toolName}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        if (toolCall is null)
            throw new ArgumentNullException(nameof(toolCall));

        return ExecuteToolAsync(toolCall.Name, toolCall.ParsedArguments, ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lock.EnterWriteLock();
        try
        {
            _tools.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        _lock.Dispose();
    }
}
