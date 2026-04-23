using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Workflows.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Workflows;

/// <summary>
/// EF Core-backed implementation of <see cref="IWorkflowService"/>.
/// Manages all workflow and step persistence, JSON import/export,
/// and built-in workflow seeding.
/// </summary>
public class WorkflowService : IWorkflowService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _log;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions _stepResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public WorkflowService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<WorkflowService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WorkflowEntity> CreateWorkflowAsync(
        string name,
        string? description,
        string category)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Workflow name must not be empty.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("Workflow category must not be empty.", nameof(category));
            }

            var now = DateTime.UtcNow;

            var workflow = new WorkflowEntity
            {
                Name = name.Trim(),
                Description = description?.Trim(),
                Category = category.Trim(),
                IsBuiltIn = false,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                RunCount = 0,
            };

            _db.Workflows.Add(workflow);
            await _db.SaveChangesAsync();

            _log.Information(
                "Created workflow {WorkflowId} '{Name}' in category '{Category}'",
                workflow.Id, workflow.Name, workflow.Category);

            return workflow;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to create workflow '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowEntity?> GetWorkflowAsync(long workflowId)
    {
        try
        {
            var workflow = await _db.Workflows
                .Include(w => w.Steps.OrderBy(s => s.StepOrder))
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            if (workflow is null)
            {
                _log.Warning("Workflow {WorkflowId} not found", workflowId);
            }

            return workflow;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowEntity>> GetAllWorkflowsAsync(bool includeBuiltIn = true)
    {
        try
        {
            var query = _db.Workflows
                .Include(w => w.Steps.OrderBy(s => s.StepOrder))
                .AsQueryable();

            if (!includeBuiltIn)
            {
                query = query.Where(w => !w.IsBuiltIn);
            }

            var workflows = await query
                .OrderBy(w => w.Category)
                .ThenBy(w => w.Name)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} workflows (includeBuiltIn={IncludeBuiltIn})",
                workflows.Count, includeBuiltIn);

            return workflows;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get all workflows");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowRunHistoryItem>> GetRecentRunsAsync(
        long workflowId,
        int maxCount = 8,
        CancellationToken ct = default)
    {
        if (workflowId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workflowId));
        }

        var boundedCount = Math.Clamp(maxCount, 1, 25);

        try
        {
            var runs = await _db.WorkflowRuns
                .AsNoTracking()
                .Where(run => run.WorkflowId == workflowId)
                .OrderByDescending(run => run.StartedAt)
                .Take(boundedCount)
                .ToListAsync(ct);

            return runs
                .Select(run => new WorkflowRunHistoryItem
                {
                    RunId = run.Id,
                    WorkflowId = run.WorkflowId,
                    Status = run.Status,
                    InitialInput = run.InitialInput ?? string.Empty,
                    FinalOutput = run.FinalOutput ?? string.Empty,
                    ErrorMessage = run.ErrorMessage,
                    StartedAt = run.StartedAt,
                    CompletedAt = run.CompletedAt,
                    StepsCompleted = run.StepsCompleted,
                    TotalSteps = run.TotalSteps,
                    TotalTokensUsed = run.TotalTokensUsed,
                    DurationMs = run.CompletedAt.HasValue
                        ? Math.Max(0, (run.CompletedAt.Value - run.StartedAt).TotalMilliseconds)
                        : null,
                    StepResults = DeserializeStepResults(run.StepOutputsJson)
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get recent runs for workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateWorkflowAsync(WorkflowEntity workflow)
    {
        try
        {
            if (workflow is null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            var existing = await _db.Workflows.FindAsync(workflow.Id);
            if (existing is null)
            {
                _log.Warning(
                    "Cannot update: workflow {WorkflowId} not found",
                    workflow.Id);
                throw new InvalidOperationException(
                    $"Workflow {workflow.Id} not found.");
            }

            existing.Name = workflow.Name;
            existing.Description = workflow.Description;
            existing.Category = workflow.Category;
            existing.Icon = workflow.Icon;
            existing.IsEnabled = workflow.IsEnabled;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.Information(
                "Updated workflow {WorkflowId} '{Name}'",
                existing.Id, existing.Name);
        }
        catch (ArgumentNullException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update workflow {WorkflowId}", workflow?.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteWorkflowAsync(long workflowId)
    {
        try
        {
            var workflow = await _db.Workflows.FindAsync(workflowId);
            if (workflow is null)
            {
                _log.Warning(
                    "Cannot delete: workflow {WorkflowId} not found",
                    workflowId);
                return;
            }

            if (workflow.IsBuiltIn)
            {
                _log.Warning(
                    "Cannot delete built-in workflow {WorkflowId} '{Name}'",
                    workflowId, workflow.Name);
                throw new InvalidOperationException(
                    $"Built-in workflow '{workflow.Name}' cannot be deleted.");
            }

            _db.Workflows.Remove(workflow);
            await _db.SaveChangesAsync();

            _log.Information(
                "Deleted workflow {WorkflowId} '{Name}' (cascade deletes steps and runs)",
                workflowId, workflow.Name);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AddStepAsync(long workflowId, WorkflowStepEntity step)
    {
        try
        {
            if (step is null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            var workflow = await _db.Workflows
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            if (workflow is null)
            {
                _log.Error(
                    "Cannot add step: workflow {WorkflowId} not found",
                    workflowId);
                throw new InvalidOperationException(
                    $"Workflow {workflowId} not found.");
            }

            // Auto-assign StepOrder to the end if not explicitly set
            if (step.StepOrder == 0 && workflow.Steps.Count > 0)
            {
                step.StepOrder = workflow.Steps.Max(s => s.StepOrder) + 1;
            }

            step.WorkflowId = workflowId;
            _db.WorkflowSteps.Add(step);

            workflow.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.Information(
                "Added step '{StepName}' (Order={StepOrder}, Type={StepType}) to workflow {WorkflowId}",
                step.Name, step.StepOrder, step.StepType, workflowId);
        }
        catch (ArgumentNullException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to add step to workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateStepAsync(WorkflowStepEntity step)
    {
        try
        {
            if (step is null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            var existing = await _db.WorkflowSteps.FindAsync(step.Id);
            if (existing is null)
            {
                _log.Warning(
                    "Cannot update: workflow step {StepId} not found",
                    step.Id);
                throw new InvalidOperationException(
                    $"Workflow step {step.Id} not found.");
            }

            existing.Name = step.Name;
            existing.StepType = step.StepType;
            existing.PromptTemplate = step.PromptTemplate;
            existing.ModelOverride = step.ModelOverride;
            existing.TemperatureOverride = step.TemperatureOverride;
            existing.MaxTokensOverride = step.MaxTokensOverride;
            existing.ConfigJson = step.ConfigJson;
            existing.StepOrder = step.StepOrder;

            // Update the parent workflow's timestamp
            var workflow = await _db.Workflows.FindAsync(existing.WorkflowId);
            if (workflow is not null)
            {
                workflow.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            _log.Information(
                "Updated workflow step {StepId} '{StepName}'",
                existing.Id, existing.Name);
        }
        catch (ArgumentNullException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update workflow step {StepId}", step?.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveStepAsync(long stepId)
    {
        try
        {
            var step = await _db.WorkflowSteps.FindAsync(stepId);
            if (step is null)
            {
                _log.Warning(
                    "Cannot remove: workflow step {StepId} not found",
                    stepId);
                return;
            }

            // Update the parent workflow's timestamp
            var workflow = await _db.Workflows.FindAsync(step.WorkflowId);
            if (workflow is not null)
            {
                workflow.UpdatedAt = DateTime.UtcNow;
            }

            _db.WorkflowSteps.Remove(step);
            await _db.SaveChangesAsync();

            _log.Information(
                "Removed workflow step {StepId} '{StepName}' from workflow {WorkflowId}",
                stepId, step.Name, step.WorkflowId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to remove workflow step {StepId}", stepId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ReorderStepsAsync(long workflowId, IReadOnlyList<long> stepIdsInOrder)
    {
        try
        {
            if (stepIdsInOrder is null || stepIdsInOrder.Count == 0)
            {
                throw new ArgumentException(
                    "Step ID list must not be null or empty.",
                    nameof(stepIdsInOrder));
            }

            var steps = await _db.WorkflowSteps
                .Where(s => s.WorkflowId == workflowId)
                .ToListAsync();

            if (steps.Count == 0)
            {
                _log.Warning(
                    "No steps found for workflow {WorkflowId} during reorder",
                    workflowId);
                return;
            }

            // Validate that all provided IDs belong to this workflow
            var stepIds = steps.Select(s => s.Id).ToHashSet();
            foreach (var id in stepIdsInOrder)
            {
                if (!stepIds.Contains(id))
                {
                    throw new InvalidOperationException(
                        $"Step {id} does not belong to workflow {workflowId}.");
                }
            }

            // Assign new order based on position in the provided list
            for (var i = 0; i < stepIdsInOrder.Count; i++)
            {
                var step = steps.FirstOrDefault(s => s.Id == stepIdsInOrder[i]);
                if (step is not null)
                {
                    step.StepOrder = i;
                }
            }

            // Update the parent workflow's timestamp
            var workflow = await _db.Workflows.FindAsync(workflowId);
            if (workflow is not null)
            {
                workflow.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            _log.Information(
                "Reordered {Count} steps in workflow {WorkflowId}",
                stepIdsInOrder.Count, workflowId);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to reorder steps in workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> ExportWorkflowAsJsonAsync(long workflowId)
    {
        try
        {
            var workflow = await _db.Workflows
                .Include(w => w.Steps.OrderBy(s => s.StepOrder))
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            if (workflow is null)
            {
                _log.Warning(
                    "Cannot export: workflow {WorkflowId} not found",
                    workflowId);
                throw new InvalidOperationException(
                    $"Workflow {workflowId} not found.");
            }

            // Create a portable DTO that excludes navigation properties and internal IDs
            var exportData = new WorkflowExportDto
            {
                Name = workflow.Name,
                Description = workflow.Description,
                Icon = workflow.Icon,
                Category = workflow.Category,
                Steps = workflow.Steps
                    .OrderBy(s => s.StepOrder)
                    .Select(s => new WorkflowStepExportDto
                    {
                        StepOrder = s.StepOrder,
                        Name = s.Name,
                        StepType = s.StepType,
                        PromptTemplate = s.PromptTemplate,
                        ModelOverride = s.ModelOverride,
                        TemperatureOverride = s.TemperatureOverride,
                        MaxTokensOverride = s.MaxTokensOverride,
                        ConfigJson = s.ConfigJson,
                    })
                    .ToList(),
                ExportedAt = DateTime.UtcNow,
                Version = "1.0",
            };

            var json = JsonSerializer.Serialize(exportData, _jsonOptions);

            _log.Information(
                "Exported workflow {WorkflowId} '{Name}' ({StepCount} steps)",
                workflowId, workflow.Name, workflow.Steps.Count);

            return json;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowEntity> ImportWorkflowFromJsonAsync(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException(
                    "JSON string must not be null or empty.",
                    nameof(json));
            }

            var importData = JsonSerializer.Deserialize<WorkflowExportDto>(json, _jsonOptions);

            if (importData is null)
            {
                throw new InvalidOperationException(
                    "Failed to deserialize workflow JSON: result was null.");
            }

            if (string.IsNullOrWhiteSpace(importData.Name))
            {
                throw new InvalidOperationException(
                    "Imported workflow must have a name.");
            }

            var now = DateTime.UtcNow;

            var workflow = new WorkflowEntity
            {
                Name = importData.Name,
                Description = importData.Description,
                Icon = importData.Icon,
                Category = importData.Category ?? "Custom",
                IsBuiltIn = false,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                RunCount = 0,
            };

            if (importData.Steps is not null)
            {
                foreach (var stepDto in importData.Steps.OrderBy(s => s.StepOrder))
                {
                    workflow.Steps.Add(new WorkflowStepEntity
                    {
                        StepOrder = stepDto.StepOrder,
                        Name = stepDto.Name ?? $"Step {stepDto.StepOrder + 1}",
                        StepType = stepDto.StepType ?? "AiPrompt",
                        PromptTemplate = stepDto.PromptTemplate ?? string.Empty,
                        ModelOverride = stepDto.ModelOverride,
                        TemperatureOverride = stepDto.TemperatureOverride,
                        MaxTokensOverride = stepDto.MaxTokensOverride,
                        ConfigJson = stepDto.ConfigJson,
                    });
                }
            }

            _db.Workflows.Add(workflow);
            await _db.SaveChangesAsync();

            _log.Information(
                "Imported workflow {WorkflowId} '{Name}' with {StepCount} steps",
                workflow.Id, workflow.Name, workflow.Steps.Count);

            return workflow;
        }
        catch (JsonException ex)
        {
            _log.Error(ex, "Failed to parse workflow JSON during import");
            throw new InvalidOperationException(
                "The provided JSON is not a valid workflow export.", ex);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to import workflow from JSON");
            throw;
        }
    }

    private IReadOnlyList<WorkflowStepResult> DeserializeStepResults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<WorkflowStepResult>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<WorkflowStepResult>>(json, _stepResultJsonOptions)
                ?? new List<WorkflowStepResult>();
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Failed to deserialize workflow step outputs");
            return Array.Empty<WorkflowStepResult>();
        }
    }

    /// <inheritdoc />
    public async Task SeedBuiltInWorkflowsAsync()
    {
        try
        {
            var existingBuiltInCount = await _db.Workflows
                .CountAsync(w => w.IsBuiltIn);

            if (existingBuiltInCount > 0)
            {
                _log.Debug(
                    "Skipping seed: {Count} built-in workflows already exist",
                    existingBuiltInCount);
                return;
            }

            var templates = new List<WorkflowEntity>
            {
                WorkflowTemplate.SummarizeAndAct(),
                WorkflowTemplate.ResearchBrief(),
                WorkflowTemplate.DocumentReview(),
                WorkflowTemplate.ContentRepurpose(),
            };

            _db.Workflows.AddRange(templates);
            await _db.SaveChangesAsync();

            _log.Information(
                "Seeded {Count} built-in workflows with a total of {StepCount} steps",
                templates.Count,
                templates.Sum(t => t.Steps.Count));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to seed built-in workflows");
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal DTOs for JSON import/export (keep internal IDs out of JSON)
    // ─────────────────────────────────────────────────────────────────────

    private sealed class WorkflowExportDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public string? Category { get; set; }
        public List<WorkflowStepExportDto>? Steps { get; set; }
        public DateTime ExportedAt { get; set; }
        public string Version { get; set; } = "1.0";
    }

    private sealed class WorkflowStepExportDto
    {
        public int StepOrder { get; set; }
        public string? Name { get; set; }
        public string? StepType { get; set; }
        public string? PromptTemplate { get; set; }
        public string? ModelOverride { get; set; }
        public double? TemperatureOverride { get; set; }
        public int? MaxTokensOverride { get; set; }
        public string? ConfigJson { get; set; }
    }
}
