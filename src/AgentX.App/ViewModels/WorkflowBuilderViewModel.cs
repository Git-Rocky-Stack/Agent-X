using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using AgentX.Core.Data.Entities;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class WorkflowBuilderViewModel : ObservableObject, IDisposable
{
    // ── Services ─────────────────────────────────────────────
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IModelManager _modelManager;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private string _pageTitle = "Prompt Workflows";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Workflow List ────────────────────────────────────────
    public ObservableCollection<WorkflowListItem> Workflows { get; } = new();
    [ObservableProperty] private WorkflowListItem? _selectedWorkflow;
    [ObservableProperty] private bool _hasWorkflows;
    public ObservableCollection<WorkflowRunHistoryDisplayItem> RecentRuns { get; } = new();

    // ── Editor State ─────────────────────────────────────────
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private string _editCategory = "Custom";
    public ObservableCollection<WorkflowStepItem> EditSteps { get; } = new();

    // ── Runner State ─────────────────────────────────────────
    [ObservableProperty] private string _runInput = string.Empty;
    [ObservableProperty] private string _runOutput = string.Empty;
    [ObservableProperty] private int _runProgress;
    [ObservableProperty] private int _runTotalSteps;
    [ObservableProperty] private string _currentStepName = string.Empty;
    [ObservableProperty] private long _runTotalTokens;
    [ObservableProperty] private double _runDurationMs;
    [ObservableProperty] private bool _runCompleted;
    [ObservableProperty] private bool _runFailed;
    [ObservableProperty] private string _runErrorMessage = string.Empty;
    [ObservableProperty] private string _runResultContextText = string.Empty;
    public ObservableCollection<StepOutputItem> StepOutputs { get; } = new();

    // ── Models ───────────────────────────────────────────────
    public ObservableCollection<AiModel> AvailableModels { get; } = new();

    // ── Category Options ─────────────────────────────────────
    public List<string> Categories { get; } = new() { "Custom", "Research", "Writing", "Analysis", "Productivity" };
    public List<string> StepTypes { get; } = new() { "AiPrompt", "DocumentLookup", "TextTransform" };
    public bool HasSelectedWorkflow => SelectedWorkflow is not null;
    public long SelectedWorkflowId => SelectedWorkflow?.Id ?? 0;
    public string SelectedWorkflowName => SelectedWorkflow?.Name ?? string.Empty;
    public bool CanRunSelectedWorkflow => SelectedWorkflow is not null && !IsRunning;
    public bool HasRecentRuns => RecentRuns.Count > 0;
    public bool ShowRecentRunsEmptyState => HasSelectedWorkflow && !HasRecentRuns;
    public bool HasStepOutputs => StepOutputs.Count > 0;
    public bool HasRunOutput => !string.IsNullOrWhiteSpace(RunOutput);
    public bool HasRunOutputOrError => HasRunOutput || !string.IsNullOrWhiteSpace(RunErrorMessage);
    public bool HasRunResultContextText => !string.IsNullOrWhiteSpace(RunResultContextText);

    private CancellationTokenSource? _runCts;

    public WorkflowBuilderViewModel(
        IWorkflowService workflowService,
        IWorkflowEngine workflowEngine,
        IModelManager modelManager)
    {
        _workflowService = workflowService;
        _workflowEngine = workflowEngine;
        _modelManager = modelManager;

        RecentRuns.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRecentRuns));
            OnPropertyChanged(nameof(ShowRecentRunsEmptyState));
        };

        StepOutputs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasStepOutputs));
        };
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Seed built-in workflows on first load
            await _workflowService.SeedBuiltInWorkflowsAsync();

            // Load all workflows
            await LoadWorkflowsAsync();

            // Load available models
            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize WorkflowBuilderViewModel");
            StatusMessage = "Failed to load workflows";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadWorkflowsAsync()
    {
        try
        {
            var selectedWorkflowId = SelectedWorkflow?.Id;
            var workflows = await _workflowService.GetAllWorkflowsAsync();
            Workflows.Clear();
            foreach (var wf in workflows)
            {
                Workflows.Add(new WorkflowListItem
                {
                    Id = wf.Id,
                    Name = wf.Name,
                    Description = wf.Description ?? string.Empty,
                    Category = wf.Category,
                    Icon = wf.Icon ?? "\uE945",
                    IsBuiltIn = wf.IsBuiltIn,
                    StepCount = wf.Steps.Count,
                    RunCount = wf.RunCount
                });
            }
            HasWorkflows = Workflows.Count > 0;

            if (selectedWorkflowId.HasValue)
            {
                SelectedWorkflow = Workflows.FirstOrDefault(workflow => workflow.Id == selectedWorkflowId.Value);
            }
            else if (Workflows.Count == 0)
            {
                SelectedWorkflow = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load workflows");
        }
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await _modelManager.GetAvailableModelsAsync();
            AvailableModels.Clear();
            foreach (var model in models)
            {
                AvailableModels.Add(model);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load available models");
        }
    }

    [RelayCommand]
    private void CreateWorkflow()
    {
        try
        {
            EditName = "New Workflow";
            EditDescription = string.Empty;
            EditCategory = "Custom";
            EditSteps.Clear();

            // Add a default first step
            EditSteps.Add(new WorkflowStepItem
            {
                StepOrder = 1,
                Name = "Step 1",
                StepType = "AiPrompt",
                PromptTemplate = "{{input}}"
            });

            IsEditing = true;
            SelectedWorkflow = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create new workflow");
        }
    }

    [RelayCommand]
    private async Task EditWorkflowAsync(long workflowId)
    {
        try
        {
            var workflow = await _workflowService.GetWorkflowAsync(workflowId);
            if (workflow is null) return;

            if (workflow.IsBuiltIn)
            {
                StatusMessage = "Use Template to customize built-in workflows";
                return;
            }

            EditName = workflow.Name;
            EditDescription = workflow.Description ?? string.Empty;
            EditCategory = workflow.Category;
            EditSteps.Clear();

            foreach (var step in workflow.Steps.OrderBy(s => s.StepOrder))
            {
                EditSteps.Add(new WorkflowStepItem
                {
                    Id = step.Id,
                    StepOrder = step.StepOrder,
                    Name = step.Name,
                    StepType = step.StepType,
                    PromptTemplate = step.PromptTemplate,
                    ModelOverride = step.ModelOverride,
                    TemperatureOverride = step.TemperatureOverride,
                    MaxTokensOverride = step.MaxTokensOverride
                });
            }

            IsEditing = true;

            // Select matching item in list
            SelectedWorkflow = Workflows.FirstOrDefault(w => w.Id == workflowId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to edit workflow {Id}", workflowId);
        }
    }

    [RelayCommand]
    private async Task UseTemplateAsync(long workflowId)
    {
        try
        {
            var workflow = await _workflowService.GetWorkflowAsync(workflowId);
            if (workflow is null)
            {
                return;
            }

            if (!workflow.IsBuiltIn)
            {
                await EditWorkflowAsync(workflowId);
                return;
            }

            var clonedWorkflow = await _workflowService.CreateWorkflowFromTemplateAsync(workflowId);
            await LoadWorkflowsAsync();
            await EditWorkflowAsync(clonedWorkflow.Id);
            StatusMessage = $"Created workflow \"{clonedWorkflow.Name}\" from template";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to use workflow template {Id}", workflowId);
            StatusMessage = "Failed to create workflow from template";
        }
    }

    [RelayCommand]
    private async Task SaveWorkflowAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName)) return;

        try
        {
            WorkflowEntity workflow;

            if (SelectedWorkflow is not null && SelectedWorkflow.Id > 0)
            {
                // Update existing
                workflow = await _workflowService.GetWorkflowAsync(SelectedWorkflow.Id) ?? throw new InvalidOperationException("Workflow not found");
                workflow.Name = EditName;
                workflow.Description = EditDescription;
                workflow.Category = EditCategory;
                workflow.UpdatedAt = DateTime.UtcNow;

                // Remove old steps and add new ones
                workflow.Steps.Clear();
                await _workflowService.UpdateWorkflowAsync(workflow);

                for (int i = 0; i < EditSteps.Count; i++)
                {
                    var step = EditSteps[i];
                    await _workflowService.AddStepAsync(workflow.Id, new WorkflowStepEntity
                    {
                        WorkflowId = workflow.Id,
                        StepOrder = i + 1,
                        Name = step.Name,
                        StepType = step.StepType,
                        PromptTemplate = step.PromptTemplate,
                        ModelOverride = step.ModelOverride,
                        TemperatureOverride = step.TemperatureOverride,
                        MaxTokensOverride = step.MaxTokensOverride
                    });
                }
            }
            else
            {
                // Create new
                workflow = await _workflowService.CreateWorkflowAsync(EditName, EditDescription, EditCategory);

                for (int i = 0; i < EditSteps.Count; i++)
                {
                    var step = EditSteps[i];
                    await _workflowService.AddStepAsync(workflow.Id, new WorkflowStepEntity
                    {
                        WorkflowId = workflow.Id,
                        StepOrder = i + 1,
                        Name = step.Name,
                        StepType = step.StepType,
                        PromptTemplate = step.PromptTemplate,
                        ModelOverride = step.ModelOverride,
                        TemperatureOverride = step.TemperatureOverride,
                        MaxTokensOverride = step.MaxTokensOverride
                    });
                }
            }

            IsEditing = false;
            await LoadWorkflowsAsync();
            StatusMessage = $"Workflow \"{EditName}\" saved";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save workflow");
            StatusMessage = "Failed to save workflow";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditSteps.Clear();
    }

    [RelayCommand]
    private void AddStep()
    {
        var nextOrder = EditSteps.Count + 1;
        EditSteps.Add(new WorkflowStepItem
        {
            StepOrder = nextOrder,
            Name = $"Step {nextOrder}",
            StepType = "AiPrompt",
            PromptTemplate = "{{previous_output}}"
        });
    }

    [RelayCommand]
    private void RemoveStep(WorkflowStepItem step)
    {
        EditSteps.Remove(step);
        // Reorder remaining steps
        for (int i = 0; i < EditSteps.Count; i++)
        {
            EditSteps[i].StepOrder = i + 1;
        }
    }

    [RelayCommand]
    private void MoveStepUp(WorkflowStepItem step)
    {
        var index = EditSteps.IndexOf(step);
        if (index > 0)
        {
            EditSteps.Move(index, index - 1);
            for (int i = 0; i < EditSteps.Count; i++)
                EditSteps[i].StepOrder = i + 1;
        }
    }

    [RelayCommand]
    private void MoveStepDown(WorkflowStepItem step)
    {
        var index = EditSteps.IndexOf(step);
        if (index < EditSteps.Count - 1)
        {
            EditSteps.Move(index, index + 1);
            for (int i = 0; i < EditSteps.Count; i++)
                EditSteps[i].StepOrder = i + 1;
        }
    }

    [RelayCommand]
    private async Task DeleteWorkflowAsync(long workflowId)
    {
        try
        {
            await _workflowService.DeleteWorkflowAsync(workflowId);
            await LoadWorkflowsAsync();
            StatusMessage = "Workflow deleted";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete workflow {Id}", workflowId);
            StatusMessage = "Failed to delete workflow";
        }
    }

    [RelayCommand]
    private async Task RunWorkflowAsync(long workflowId)
    {
        if (string.IsNullOrWhiteSpace(RunInput))
        {
            StatusMessage = "Please enter input text to process";
            return;
        }

        IsRunning = true;
        RunCompleted = false;
        RunFailed = false;
        RunOutput = string.Empty;
        RunErrorMessage = string.Empty;
        RunTotalTokens = 0;
        RunDurationMs = 0;
        RunProgress = 0;
        StepOutputs.Clear();
        RunResultContextText = string.Empty;
        _runCts = new CancellationTokenSource();

        try
        {
            var workflow = await _workflowService.GetWorkflowAsync(workflowId);
            if (workflow is null) return;

            RunTotalSteps = workflow.Steps.Count;

            var progress = new Progress<WorkflowStepResult>(stepResult =>
            {
                RunProgress = stepResult.StepOrder;
                CurrentStepName = stepResult.StepName;

                StepOutputs.Add(new StepOutputItem
                {
                    StepOrder = stepResult.StepOrder,
                    StepName = stepResult.StepName,
                    Output = stepResult.Output,
                    TokensUsed = stepResult.TokensUsed,
                    DurationMs = stepResult.DurationMs,
                    ModelUsed = stepResult.ModelUsed ?? string.Empty,
                    Success = stepResult.Success,
                    ErrorMessage = stepResult.ErrorMessage
                });
            });

            var result = await _workflowEngine.ExecuteWorkflowAsync(
                workflowId, RunInput, progress, _runCts.Token);

            RunOutput = result.FinalOutput;
            RunTotalTokens = result.TotalTokensUsed;
            RunDurationMs = result.TotalDurationMs;
            RunCompleted = result.Success;
            RunFailed = !result.Success;
            RunResultContextText = "Showing latest execution result";

            if (!result.Success)
            {
                RunErrorMessage = "One or more steps failed. Check step outputs for details.";
            }

            StatusMessage = result.Success
                ? $"Workflow completed in {result.TotalDurationMs:F0}ms"
                : "Workflow failed";

            await LoadWorkflowsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Workflow cancelled";
            RunFailed = true;
            RunErrorMessage = "Cancelled by user";
            RunResultContextText = "Showing the cancelled execution result";
            await LoadWorkflowsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Workflow execution failed");
            StatusMessage = "Workflow execution failed";
            RunFailed = true;
            RunErrorMessage = ex.Message;
            RunResultContextText = "Showing the failed execution result";
            await LoadWorkflowsAsync();
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    [RelayCommand]
    private async Task CancelRunAsync()
    {
        _runCts?.Cancel();
        await (_workflowEngine?.CancelExecutionAsync() ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ExportWorkflowAsync(long workflowId)
    {
        try
        {
            var json = await _workflowService.ExportWorkflowAsJsonAsync(workflowId);
            // Copy to clipboard — let the page handle the file picker
            StatusMessage = "Workflow JSON copied to clipboard";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export workflow");
            StatusMessage = "Export failed";
        }
    }

    [RelayCommand]
    private async Task ImportWorkflowAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var workflow = await _workflowService.ImportWorkflowFromJsonAsync(json);
            await LoadWorkflowsAsync();
            StatusMessage = $"Imported workflow \"{workflow.Name}\"";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import workflow");
            StatusMessage = "Import failed — invalid workflow JSON";
        }
    }

    public void Dispose()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
    }

    [RelayCommand]
    private void OpenHistoricalRun(WorkflowRunHistoryDisplayItem? run)
    {
        if (run is null)
        {
            return;
        }

        ApplyHistoricalRun(run);
        StatusMessage = $"Showing stored run from {run.StartedAtText}";
    }

    partial void OnSelectedWorkflowChanged(WorkflowListItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedWorkflow));
        OnPropertyChanged(nameof(SelectedWorkflowId));
        OnPropertyChanged(nameof(SelectedWorkflowName));
        OnPropertyChanged(nameof(CanRunSelectedWorkflow));
        OnPropertyChanged(nameof(ShowRecentRunsEmptyState));

        _ = LoadSelectedWorkflowRunsAsync(value);
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunSelectedWorkflow));
    }

    partial void OnRunOutputChanged(string value)
    {
        OnPropertyChanged(nameof(HasRunOutput));
        OnPropertyChanged(nameof(HasRunOutputOrError));
    }

    partial void OnRunErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasRunOutputOrError));
    }

    partial void OnRunResultContextTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasRunResultContextText));
    }

    private async Task LoadSelectedWorkflowRunsAsync(WorkflowListItem? workflow)
    {
        RecentRuns.Clear();

        if (workflow is null)
        {
            ClearRunInspection();
            return;
        }

        try
        {
            var runs = await _workflowService.GetRecentRunsAsync(workflow.Id);
            foreach (var run in runs)
            {
                RecentRuns.Add(new WorkflowRunHistoryDisplayItem(run));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load recent runs for workflow {WorkflowId}", workflow.Id);
            StatusMessage = "Failed to load recent workflow runs";
        }
    }

    private void ClearRunInspection()
    {
        IsRunning = false;
        RunCompleted = false;
        RunFailed = false;
        RunErrorMessage = string.Empty;
        RunOutput = string.Empty;
        RunProgress = 0;
        RunTotalSteps = 0;
        CurrentStepName = string.Empty;
        RunTotalTokens = 0;
        RunDurationMs = 0;
        RunResultContextText = string.Empty;
        StepOutputs.Clear();
    }

    private void ApplyHistoricalRun(WorkflowRunHistoryDisplayItem run)
    {
        IsRunning = false;
        RunCompleted = string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase);
        RunFailed = string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "cancelled", StringComparison.OrdinalIgnoreCase);
        RunErrorMessage = run.ErrorMessage;
        RunOutput = run.FinalOutput;
        RunProgress = run.StepsCompleted;
        RunTotalSteps = run.TotalSteps;
        CurrentStepName = run.StepResults.LastOrDefault()?.StepName ?? string.Empty;
        RunTotalTokens = run.TotalTokensUsed;
        RunDurationMs = run.DurationMs ?? 0;
        RunResultContextText = $"Showing stored run from {run.StartedAtText}";

        StepOutputs.Clear();
        foreach (var step in run.StepResults)
        {
            StepOutputs.Add(new StepOutputItem
            {
                StepOrder = step.StepOrder,
                StepName = step.StepName,
                Output = step.Output,
                TokensUsed = step.TokensUsed,
                DurationMs = step.DurationMs,
                ModelUsed = step.ModelUsed ?? string.Empty,
                Success = step.Success,
                ErrorMessage = step.ErrorMessage
            });
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
// VIEW MODELS for list items and step display
// ═══════════════════════════════════════════════════════════════════

public partial class WorkflowListItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _category = "Custom";
    [ObservableProperty] private string _icon = "\uE945";
    [ObservableProperty] private bool _isBuiltIn;
    [ObservableProperty] private int _stepCount;
    [ObservableProperty] private int _runCount;
}

public partial class WorkflowStepItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private int _stepOrder;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _stepType = "AiPrompt";
    [ObservableProperty] private string _promptTemplate = string.Empty;
    [ObservableProperty] private string? _modelOverride;
    [ObservableProperty] private double? _temperatureOverride;
    [ObservableProperty] private int? _maxTokensOverride;
}

public partial class StepOutputItem : ObservableObject
{
    [ObservableProperty] private int _stepOrder;
    [ObservableProperty] private string _stepName = string.Empty;
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private long _tokensUsed;
    [ObservableProperty] private double _durationMs;
    [ObservableProperty] private string _modelUsed = string.Empty;
    [ObservableProperty] private bool _success;
    [ObservableProperty] private string? _errorMessage;
}

public sealed class WorkflowRunHistoryDisplayItem
{
    public WorkflowRunHistoryDisplayItem(WorkflowRunHistoryItem run)
    {
        RunId = run.RunId;
        Status = run.Status;
        StartedAt = run.StartedAt;
        StartedAtText = run.StartedAt.ToLocalTime().ToString("MMM d, h:mm tt");
        FinalOutput = run.FinalOutput;
        ErrorMessage = run.ErrorMessage ?? string.Empty;
        StepsCompleted = run.StepsCompleted;
        TotalSteps = run.TotalSteps;
        TotalTokensUsed = run.TotalTokensUsed;
        DurationMs = run.DurationMs;
        StepResults = run.StepResults;
    }

    public long RunId { get; }
    public string Status { get; }
    public DateTime StartedAt { get; }
    public string StartedAtText { get; }
    public string FinalOutput { get; }
    public string ErrorMessage { get; }
    public int StepsCompleted { get; }
    public int TotalSteps { get; }
    public long TotalTokensUsed { get; }
    public double? DurationMs { get; }
    public IReadOnlyList<WorkflowStepResult> StepResults { get; }
    public string StatusText => Status switch
    {
        "completed" => "Completed",
        "failed" => "Failed",
        "cancelled" => "Cancelled",
        "running" => "Running",
        _ => "Pending"
    };

    public string DetailText
    {
        get
        {
            var parts = new List<string>
            {
                $"{StepsCompleted}/{TotalSteps} steps"
            };

            if (TotalTokensUsed > 0)
            {
                parts.Add($"{TotalTokensUsed} tokens");
            }

            if (DurationMs is double durationMs && durationMs > 0)
            {
                parts.Add($"{durationMs:F0} ms");
            }

            return string.Join(" • ", parts);
        }
    }

    public string PreviewText
    {
        get
        {
            var source = !string.IsNullOrWhiteSpace(FinalOutput)
                ? FinalOutput
                : ErrorMessage;

            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            source = source.Replace(Environment.NewLine, " ").Trim();
            return source.Length <= 140 ? source : $"{source[..137]}...";
        }
    }

    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewText);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
}
