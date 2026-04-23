using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Workflows.Models;

namespace AgentX.Core.Services.Workflows;

/// <summary>
/// Manages workflow persistence and lifecycle. Provides CRUD operations
/// for workflows and their steps, JSON import/export, and built-in
/// workflow seeding via EF Core.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Creates a new empty workflow with the specified name, description, and category.
    /// </summary>
    /// <param name="name">The display name for the workflow.</param>
    /// <param name="description">Optional description of what the workflow does.</param>
    /// <param name="category">The category for grouping (e.g., Custom, Research, Writing, Analysis).</param>
    /// <returns>The newly created workflow entity with its generated ID.</returns>
    Task<WorkflowEntity> CreateWorkflowAsync(string name, string? description, string category);

    /// <summary>
    /// Retrieves a workflow by ID, including all of its steps ordered by <see cref="WorkflowStepEntity.StepOrder"/>.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to retrieve.</param>
    /// <returns>The workflow entity with steps loaded, or null if not found.</returns>
    Task<WorkflowEntity?> GetWorkflowAsync(long workflowId);

    /// <summary>
    /// Returns all workflows, optionally including built-in templates.
    /// Results are ordered by category, then by name.
    /// </summary>
    /// <param name="includeBuiltIn">When true (default), built-in workflows are included in results.</param>
    /// <returns>A read-only list of all matching workflows.</returns>
    Task<IReadOnlyList<WorkflowEntity>> GetAllWorkflowsAsync(bool includeBuiltIn = true);

    /// <summary>
    /// Returns recent persisted runs for a single workflow, newest first.
    /// The results are shaped for read-only inspection in the UI.
    /// </summary>
    /// <param name="workflowId">The workflow whose recent runs should be returned.</param>
    /// <param name="maxCount">Maximum number of runs to return. Defaults to 8.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<WorkflowRunHistoryItem>> GetRecentRunsAsync(
        long workflowId,
        int maxCount = 8,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new editable workflow by cloning an existing workflow and all of its steps.
    /// Intended for turning built-in starter templates into user-owned workflows.
    /// </summary>
    /// <param name="sourceWorkflowId">The workflow to clone.</param>
    /// <param name="nameOverride">Optional name override for the new workflow.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkflowEntity> CreateWorkflowFromTemplateAsync(
        long sourceWorkflowId,
        string? nameOverride = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing workflow's metadata (name, description, category, icon, enabled state).
    /// Does not modify the workflow's steps; use step-specific methods for that.
    /// </summary>
    /// <param name="workflow">The workflow entity with updated property values.</param>
    Task UpdateWorkflowAsync(WorkflowEntity workflow);

    /// <summary>
    /// Permanently deletes a workflow and all of its steps and run history.
    /// Built-in workflows cannot be deleted.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to delete.</param>
    Task DeleteWorkflowAsync(long workflowId);

    /// <summary>
    /// Adds a new step to an existing workflow. The step's <see cref="WorkflowStepEntity.WorkflowId"/>
    /// will be set automatically.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to add the step to.</param>
    /// <param name="step">The step entity to add.</param>
    Task AddStepAsync(long workflowId, WorkflowStepEntity step);

    /// <summary>
    /// Updates an existing step's properties (name, type, prompt template, overrides, config).
    /// </summary>
    /// <param name="step">The step entity with updated property values.</param>
    Task UpdateStepAsync(WorkflowStepEntity step);

    /// <summary>
    /// Removes a step from its workflow by ID.
    /// Remaining steps are not automatically reordered.
    /// </summary>
    /// <param name="stepId">The ID of the step to remove.</param>
    Task RemoveStepAsync(long stepId);

    /// <summary>
    /// Reorders the steps within a workflow by assigning new <see cref="WorkflowStepEntity.StepOrder"/>
    /// values based on the position of each step ID in the provided list.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow whose steps to reorder.</param>
    /// <param name="stepIdsInOrder">Step IDs in the desired execution order.</param>
    Task ReorderStepsAsync(long workflowId, IReadOnlyList<long> stepIdsInOrder);

    /// <summary>
    /// Exports a workflow and all its steps as a JSON string suitable for
    /// sharing, backup, or importing into another instance.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to export.</param>
    /// <returns>A JSON string representing the workflow and its steps.</returns>
    Task<string> ExportWorkflowAsJsonAsync(long workflowId);

    /// <summary>
    /// Imports a workflow from a JSON string previously exported by
    /// <see cref="ExportWorkflowAsJsonAsync"/>. The imported workflow is
    /// created as a new non-built-in workflow with a fresh ID.
    /// </summary>
    /// <param name="json">The JSON string to import.</param>
    /// <returns>The newly created workflow entity.</returns>
    Task<WorkflowEntity> ImportWorkflowFromJsonAsync(string json);

    /// <summary>
    /// Seeds the database with built-in workflow templates if none exist.
    /// This method is idempotent: calling it multiple times has no additional effect
    /// once the built-in workflows have been created.
    /// </summary>
    Task SeedBuiltInWorkflowsAsync();
}
