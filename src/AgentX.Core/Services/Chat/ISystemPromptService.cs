namespace AgentX.Core.Services.Chat;

using AgentX.Core.Data.Entities;

/// <summary>
/// Manages system prompt templates. Provides CRUD operations, favorites,
/// usage tracking, and seeding of built-in prompts.
/// </summary>
public interface ISystemPromptService
{
    /// <summary>
    /// Returns all prompts, optionally filtered by category.
    /// Results are ordered by IsFavorite descending, then UsageCount descending.
    /// </summary>
    Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null);

    /// <summary>
    /// Retrieves a single prompt by ID.
    /// </summary>
    Task<SystemPromptEntity?> GetPromptAsync(long id);

    /// <summary>
    /// Creates a new user-defined prompt.
    /// </summary>
    Task<SystemPromptEntity> CreatePromptAsync(string name, string content, string category);

    /// <summary>
    /// Updates an existing prompt's name, content, and category.
    /// </summary>
    Task UpdatePromptAsync(long id, string name, string content, string category);

    /// <summary>
    /// Deletes a prompt. Built-in prompts cannot be deleted.
    /// </summary>
    Task DeletePromptAsync(long id);

    /// <summary>
    /// Toggles the favorite status of a prompt.
    /// </summary>
    Task ToggleFavoriteAsync(long id);

    /// <summary>
    /// Increments the usage counter for a prompt.
    /// </summary>
    Task IncrementUsageAsync(long id);

    /// <summary>
    /// Seeds the database with built-in prompts if they do not already exist.
    /// Should be called once during application startup.
    /// </summary>
    Task SeedBuiltInPromptsAsync();
}
