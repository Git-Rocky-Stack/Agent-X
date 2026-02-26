using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// EF Core-backed implementation of <see cref="ISystemPromptService"/>.
/// Manages system prompt templates including built-in seeding, CRUD, favorites, and usage tracking.
/// </summary>
public class SystemPromptService : ISystemPromptService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _log;

    public SystemPromptService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<SystemPromptService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null)
    {
        try
        {
            var query = _db.SystemPrompts.AsQueryable();

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(p => p.Category == category);
            }

            var prompts = await query
                .OrderByDescending(p => p.IsFavorite)
                .ThenByDescending(p => p.UsageCount)
                .ThenBy(p => p.Name)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} prompts (category={Category})",
                prompts.Count, category ?? "all");

            return prompts;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get system prompts (category={Category})", category);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SystemPromptEntity?> GetPromptAsync(long id)
    {
        try
        {
            return await _db.SystemPrompts.FindAsync(id);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get system prompt {PromptId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SystemPromptEntity> CreatePromptAsync(
        string name,
        string content,
        string category)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Prompt name cannot be empty.", nameof(name));
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Prompt content cannot be empty.", nameof(content));
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Prompt category cannot be empty.", nameof(category));

            var now = DateTime.UtcNow;

            var prompt = new SystemPromptEntity
            {
                Name = name.Trim(),
                Content = content.Trim(),
                Category = category.Trim(),
                IsBuiltIn = false,
                IsFavorite = false,
                CreatedAt = now,
                UpdatedAt = now,
                UsageCount = 0,
            };

            _db.SystemPrompts.Add(prompt);
            await _db.SaveChangesAsync();

            _log.Information(
                "Created system prompt {PromptId} '{Name}' in category '{Category}'",
                prompt.Id, prompt.Name, prompt.Category);

            return prompt;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to create system prompt '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdatePromptAsync(long id, string name, string content, string category)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Prompt name cannot be empty.", nameof(name));
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Prompt content cannot be empty.", nameof(content));
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Prompt category cannot be empty.", nameof(category));

            var prompt = await _db.SystemPrompts.FindAsync(id);
            if (prompt is null)
            {
                _log.Warning("Cannot update: system prompt {PromptId} not found", id);
                return;
            }

            prompt.Name = name.Trim();
            prompt.Content = content.Trim();
            prompt.Category = category.Trim();
            prompt.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.Information(
                "Updated system prompt {PromptId} '{Name}'",
                id, prompt.Name);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update system prompt {PromptId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeletePromptAsync(long id)
    {
        try
        {
            var prompt = await _db.SystemPrompts.FindAsync(id);
            if (prompt is null)
            {
                _log.Warning("Cannot delete: system prompt {PromptId} not found", id);
                return;
            }

            if (prompt.IsBuiltIn)
            {
                _log.Warning(
                    "Cannot delete built-in system prompt {PromptId} '{Name}'",
                    id, prompt.Name);
                throw new InvalidOperationException(
                    $"Built-in prompt '{prompt.Name}' cannot be deleted.");
            }

            _db.SystemPrompts.Remove(prompt);
            await _db.SaveChangesAsync();

            _log.Information("Deleted system prompt {PromptId} '{Name}'", id, prompt.Name);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete system prompt {PromptId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ToggleFavoriteAsync(long id)
    {
        try
        {
            var prompt = await _db.SystemPrompts.FindAsync(id);
            if (prompt is null)
            {
                _log.Warning("Cannot toggle favorite: system prompt {PromptId} not found", id);
                return;
            }

            prompt.IsFavorite = !prompt.IsFavorite;
            prompt.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Debug(
                "Toggled favorite for prompt {PromptId} '{Name}' to {IsFavorite}",
                id, prompt.Name, prompt.IsFavorite);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to toggle favorite for prompt {PromptId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task IncrementUsageAsync(long id)
    {
        try
        {
            var prompt = await _db.SystemPrompts.FindAsync(id);
            if (prompt is null)
            {
                _log.Warning("Cannot increment usage: system prompt {PromptId} not found", id);
                return;
            }

            prompt.UsageCount += 1;
            prompt.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Debug(
                "Incremented usage for prompt {PromptId} '{Name}' to {UsageCount}",
                id, prompt.Name, prompt.UsageCount);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to increment usage for prompt {PromptId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SeedBuiltInPromptsAsync()
    {
        try
        {
            var existingBuiltInCount = await _db.SystemPrompts
                .CountAsync(p => p.IsBuiltIn);

            if (existingBuiltInCount > 0)
            {
                _log.Debug(
                    "Skipping seed: {Count} built-in prompts already exist",
                    existingBuiltInCount);
                return;
            }

            var now = DateTime.UtcNow;

            var builtInPrompts = new List<SystemPromptEntity>
            {
                new()
                {
                    Name = "General Assistant",
                    Content = "You are a helpful, friendly assistant. Provide clear, concise answers.",
                    Category = "General",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Code Helper",
                    Content = "You are an expert programmer. Write clean, well-documented code. Explain your approach.",
                    Category = "Code",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Writing Editor",
                    Content = "You are a professional editor. Improve clarity, grammar, and style while preserving the author's voice.",
                    Category = "Writing",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Research Analyst",
                    Content = "You are a thorough research analyst. Provide balanced, evidence-based analysis with clear citations.",
                    Category = "Analysis",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Creative Writer",
                    Content = "You are a creative writer. Craft engaging, vivid prose with attention to narrative and style.",
                    Category = "Creative",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Data Analyzer",
                    Content = "You are a data analyst. Extract insights from data, identify patterns, and present findings clearly.",
                    Category = "Analysis",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Summarizer",
                    Content = "You are a summarization expert. Create concise, accurate summaries that capture key points.",
                    Category = "General",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Translator",
                    Content = "You are a professional translator. Provide accurate, natural translations while preserving meaning and tone.",
                    Category = "General",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Technical Explainer",
                    Content = "You are a technical communicator. Explain complex concepts in simple, accessible language with examples.",
                    Category = "General",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new()
                {
                    Name = "Socratic Teacher",
                    Content = "You are a Socratic teacher. Guide learning through thoughtful questions rather than direct answers.",
                    Category = "General",
                    IsBuiltIn = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            };

            _db.SystemPrompts.AddRange(builtInPrompts);
            await _db.SaveChangesAsync();

            _log.Information("Seeded {Count} built-in system prompts", builtInPrompts.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to seed built-in system prompts");
            throw;
        }
    }
}
