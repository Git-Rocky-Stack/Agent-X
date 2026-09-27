using System.Collections.ObjectModel;
using System.Globalization;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class WorkflowBuilderViewModel : ObservableObject, IDisposable
{
    /// <summary>The built-in templates that have a guide, by their (stored, English) workflow name.</summary>
    private static readonly IReadOnlyDictionary<string, TemplateGuideId> TemplateGuideIds =
        new Dictionary<string, TemplateGuideId>(StringComparer.OrdinalIgnoreCase)
        {
            ["Summarize & Act"] = TemplateGuideId.SummarizeAndAct,
            ["Research Brief"] = TemplateGuideId.ResearchBrief,
            ["Document Review"] = TemplateGuideId.DocumentReview,
            ["Content Repurpose"] = TemplateGuideId.ContentRepurpose
        };

    private enum TemplateGuideId
    {
        SummarizeAndAct,
        ResearchBrief,
        DocumentReview,
        ContentRepurpose
    }

    // ── Services ─────────────────────────────────────────────
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IModelManager _modelManager;
    private readonly IDocumentService _documentService;
    private readonly IExportService? _exportService;
    private readonly IWorkflowLaunchService? _workflowLaunchService;
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private readonly IAppPathService _appPaths;
    private readonly ILocalizationService? _localization;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _focusedWorkflowRunSourceLabel = string.Empty;

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

    /// <summary>
    /// True while the result surface shows a stored run (opened from Recent Runs or Operations)
    /// rather than an execution started here. Code checks this, not the translated
    /// <see cref="RunResultContextText"/>.
    /// </summary>
    [ObservableProperty] private bool _isShowingStoredRun;

    [ObservableProperty] private string _lastSavedWorkflowDocumentName = string.Empty;
    public ObservableCollection<StepOutputItem> StepOutputs { get; } = new();

    // ── Models ───────────────────────────────────────────────
    public ObservableCollection<AiModel> AvailableModels { get; } = new();
    public NavigateHandler? NavigateRequested { get; set; }

    // ── Category Options ─────────────────────────────────────
    public List<string> Categories { get; } = new() { "Custom", "Research", "Writing", "Analysis", "Productivity" };
    public List<string> StepTypes { get; } = [.. WorkflowStepSettings.StepTypes];
    public bool HasSelectedWorkflow => SelectedWorkflow is not null;
    public long SelectedWorkflowId => SelectedWorkflow?.Id ?? 0;
    public string SelectedWorkflowName => SelectedWorkflow?.Name ?? string.Empty;
    public bool CanRunSelectedWorkflow => SelectedWorkflow is not null && !IsRunning;
    public bool ShowWorkflowStarterEmptyState => !IsEditing && !HasSelectedWorkflow;
    public bool ShowWorkflowRunnerSection => !IsEditing && HasSelectedWorkflow;

    /// <summary>
    /// The workflow list is locked while the editor is open, so the selection (which the list
    /// binds two-way) cannot drift to another workflow in the middle of an edit.
    /// </summary>
    public bool CanChangeWorkflowSelection => !IsEditing;
    public bool HasRecentRuns => RecentRuns.Count > 0;
    public bool ShowRecentRunsEmptyState => HasSelectedWorkflow && !HasRecentRuns;
    public bool HasFocusedWorkflowRunLanding => !string.IsNullOrWhiteSpace(FocusedWorkflowRunSourceLabel);
    public bool HasStepOutputs => StepOutputs.Count > 0;
    public bool HasRunOutput => !string.IsNullOrWhiteSpace(RunOutput);
    public bool HasRunOutputOrError => HasRunOutput || !string.IsNullOrWhiteSpace(RunErrorMessage);
    public bool HasRunResultContextText => !string.IsNullOrWhiteSpace(RunResultContextText);
    public bool CanSaveCurrentResultToVault => !IsRunning && !string.IsNullOrWhiteSpace(GetCurrentResultText());
    public bool HasSelectedTemplateGuide => SelectedTemplateGuide is not null;
    public string SelectedTemplateGuideSummary => SelectedTemplateGuide?.Summary ?? string.Empty;
    public string SelectedTemplateGuideBestFor => SelectedTemplateGuide?.BestFor ?? string.Empty;
    public string SelectedTemplateGuideOutcome => SelectedTemplateGuide?.Outcome ?? string.Empty;
    public IReadOnlyList<WorkflowTemplateGuideExampleItem> SelectedTemplateGuideExamples =>
        SelectedTemplateGuide?.Examples ?? Array.Empty<WorkflowTemplateGuideExampleItem>();
    public bool HasSelectedTemplateGuideExamples => SelectedTemplateGuideExamples.Count > 0;
    public IReadOnlyList<WorkflowStarterTemplateDisplayItem> WorkflowStarterTemplates =>
        Workflows
            .Where(workflow => workflow.IsBuiltIn)
            .Select(workflow =>
            {
                var guide = FindTemplateGuide(workflow.Name);
                var summary = guide?.Summary ?? workflow.Description;
                var bestFor = guide?.BestFor ?? workflow.Category;

                return new WorkflowStarterTemplateDisplayItem(
                    workflow.Id,
                    workflow.Name,
                    workflow.Category,
                    summary,
                    bestFor);
            })
            .ToArray();
    public bool HasWorkflowStarterTemplates => WorkflowStarterTemplates.Count > 0;

    private CancellationTokenSource? _runCts;
    private OperationsWorkflowRunDrillInRequest? _pendingOperationsRunRequest;

    /// <summary>
    /// The workflow the editor is editing, captured when editing starts; null while composing a
    /// new workflow. Save writes to this workflow, never to whatever the list selection became.
    /// </summary>
    private long? _editingWorkflowId;

    public WorkflowBuilderViewModel(
        IWorkflowService workflowService,
        IWorkflowEngine workflowEngine,
        IModelManager modelManager,
        IDocumentService documentService,
        IExportService? exportService = null,
        IWorkflowLaunchService? workflowLaunchService = null,
        IOperationsDrillInService? operationsDrillInService = null,
        IAppPathService? appPathService = null,
        ILocalizationService? localization = null)
    {
        _workflowService = workflowService;
        _workflowEngine = workflowEngine;
        _modelManager = modelManager;
        _documentService = documentService;
        _exportService = exportService;
        _workflowLaunchService = workflowLaunchService;
        _operationsDrillInService = operationsDrillInService;
        // Falls back to the real %LOCALAPPDATA%/AgentX paths when not supplied; tests inject a
        // disposable temp root so workflow-result artifacts never land in the real profile (AX-QA-011).
        _appPaths = appPathService ?? new AppPathService();
        // Translates the page's messages and the step settings texts; without it they are
        // shown in English.
        _localization = localization;

        Workflows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WorkflowStarterTemplates));
            OnPropertyChanged(nameof(HasWorkflowStarterTemplates));
        };

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

    [RelayCommand(CanExecute = nameof(CanSaveCurrentResultToVault))]
    private async Task SaveCurrentResultToVaultAsync()
    {
        var resultText = GetCurrentResultText();
        if (string.IsNullOrWhiteSpace(resultText))
        {
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_NoResultToSave"),
                "WfBuilder_NoResultToSave",
                "No workflow result available to save");
            return;
        }

        var document = await SaveWorkflowResultToVaultAsync(
            workflowName: SelectedWorkflowName,
            captureLabel: RunResultContextText,
            resultText: resultText,
            capturedAt: DateTime.UtcNow);

        if (document is not null)
        {
            LastSavedWorkflowDocumentName = document.FileName;
            if (TryResolveFocusedWorkflowRunFromCurrentContext(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_ResolvedRunBySaving", document.FileName),
                    "WfBuilder_ResolvedRunBySaving",
                    "Resolved the focused workflow run by saving it to Knowledge Vault as \"{0}\".",
                    document.FileName)))
            {
                return;
            }

            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_SavedResultToVault", document.FileName),
                "WfBuilder_SavedResultToVault",
                "Saved workflow result to Knowledge Vault as \"{0}\"",
                document.FileName);
        }
    }

    [RelayCommand]
    private async Task SaveHistoricalRunToVaultAsync(WorkflowRunHistoryDisplayItem? run)
    {
        if (run is null)
        {
            return;
        }

        var resultText = run.GetSaveableContent();
        if (string.IsNullOrWhiteSpace(resultText))
        {
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StoredRunNothingToSave"),
                "WfBuilder_StoredRunNothingToSave",
                "This stored run does not have result content to save");
            return;
        }

        // The name goes into the saved document's file name, which stays as it is.
        var workflowName = !string.IsNullOrWhiteSpace(SelectedWorkflowName)
            ? SelectedWorkflowName
            : "Workflow";

        var document = await SaveWorkflowResultToVaultAsync(
            workflowName: workflowName,
            captureLabel: StoredRunCaptureLabel(run),
            resultText: resultText,
            capturedAt: run.StartedAt);

        if (document is not null)
        {
            LastSavedWorkflowDocumentName = document.FileName;
            if (TryResolveFocusedWorkflowRun(run, WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_ResolvedRunBySaving", document.FileName),
                    "WfBuilder_ResolvedRunBySaving",
                    "Resolved the focused workflow run by saving it to Knowledge Vault as \"{0}\".",
                    document.FileName)))
            {
                return;
            }

            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_SavedStoredResultToVault", document.FileName),
                "WfBuilder_SavedStoredResultToVault",
                "Saved stored workflow result to Knowledge Vault as \"{0}\"",
                document.FileName);
        }
    }

    [RelayCommand]
    private void OpenKnowledgeVault()
    {
        NavigateRequested?.Invoke("KnowledgeVault");
    }

    public async Task<ExportResult> ExportCurrentResultAsync(ExportOptions options)
    {
        var artifact = BuildCurrentResultArtifact();
        if (artifact is null)
        {
            return ExportResult.Fail(WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_NoResultToExport"),
                "WfBuilder_NoResultToExport",
                "No workflow result available to export."));
        }

        var result = await ExportWorkflowResultAsync(artifact, options);
        if (result.Success)
        {
            var fileName = Path.GetFileName(result.FilePath) ?? string.Empty;
            if (TryResolveFocusedWorkflowRunFromCurrentContext(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_ResolvedRunByExporting", fileName),
                    "WfBuilder_ResolvedRunByExporting",
                    "Resolved the focused workflow run by exporting it to {0}.",
                    fileName)))
            {
                return result;
            }

            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ExportedResult", fileName),
                "WfBuilder_ExportedResult",
                "Exported workflow result to {0}",
                fileName);
        }
        else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            StatusMessage = result.ErrorMessage;
        }

        return result;
    }

    public async Task<ExportResult> ExportHistoricalRunAsync(
        WorkflowRunHistoryDisplayItem? run,
        ExportOptions options)
    {
        if (run is null)
        {
            return ExportResult.Fail(WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_NoRunSelectedToExport"),
                "WfBuilder_NoRunSelectedToExport",
                "No workflow run selected for export."));
        }

        var artifact = BuildHistoricalRunArtifact(run);
        if (artifact is null)
        {
            return ExportResult.Fail(WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StoredRunNothingToExport"),
                "WfBuilder_StoredRunNothingToExport",
                "This stored run does not have result content to export."));
        }

        var result = await ExportWorkflowResultAsync(artifact, options);
        if (result.Success)
        {
            var fileName = Path.GetFileName(result.FilePath) ?? string.Empty;
            if (TryResolveFocusedWorkflowRun(run, WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_ResolvedRunByExporting", fileName),
                    "WfBuilder_ResolvedRunByExporting",
                    "Resolved the focused workflow run by exporting it to {0}.",
                    fileName)))
            {
                return result;
            }

            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ExportedStoredResult", fileName),
                "WfBuilder_ExportedStoredResult",
                "Exported stored workflow result to {0}",
                fileName);
        }
        else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            StatusMessage = result.ErrorMessage;
        }

        return result;
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

            ApplyPendingWorkflowLaunchRequest();
            ApplyPendingOperationsWorkflowRunRequest();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize WorkflowBuilderViewModel");
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_LoadWorkflowsFailed"),
                "WfBuilder_LoadWorkflowsFailed",
                "Failed to load workflows");
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
            // The default names are what the new workflow and step are saved as, so they are
            // in the UI language. The category is a stored value and stays as it is.
            EditName = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_NewWorkflowName"),
                "WfBuilder_NewWorkflowName",
                "New Workflow");
            EditDescription = string.Empty;
            EditCategory = "Custom";
            EditSteps.Clear();

            // Add a default first step
            EditSteps.Add(new WorkflowStepItem(_localization)
            {
                StepOrder = 1,
                Name = DefaultStepName(1),
                StepType = "AiPrompt",
                PromptTemplate = "{{input}}"
            });

            _editingWorkflowId = null;
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
                StatusMessage = WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_UseTemplateToCustomize"),
                    "WfBuilder_UseTemplateToCustomize",
                    "Use Template to customize built-in workflows");
                return;
            }

            EditName = workflow.Name;
            EditDescription = workflow.Description ?? string.Empty;
            EditCategory = workflow.Category;
            EditSteps.Clear();

            foreach (var step in workflow.Steps.OrderBy(s => s.StepOrder))
            {
                EditSteps.Add(new WorkflowStepItem(_localization)
                {
                    Id = step.Id,
                    StepOrder = step.StepOrder,
                    Name = step.Name,
                    StepType = step.StepType,
                    PromptTemplate = step.PromptTemplate,
                    ModelOverride = step.ModelOverride,
                    TemperatureOverride = step.TemperatureOverride,
                    MaxTokensOverride = step.MaxTokensOverride,
                    ConfigJson = step.ConfigJson
                });
            }

            _editingWorkflowId = workflow.Id;
            IsEditing = true;

            // Select matching item in list
            SelectedWorkflow = Workflows.FirstOrDefault(w => w.Id == workflowId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to edit workflow {Id}", workflowId);
        }
    }

    public async Task<string?> GetWorkflowExportJsonAsync(long workflowId)
    {
        try
        {
            return await _workflowService.ExportWorkflowAsJsonAsync(workflowId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export workflow {Id}", workflowId);
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ExportWorkflowFailed"),
                "WfBuilder_ExportWorkflowFailed",
                "Export failed");
            return null;
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
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_CreatedFromTemplate", clonedWorkflow.Name),
                "WfBuilder_CreatedFromTemplate",
                "Created workflow \"{0}\" from template",
                clonedWorkflow.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to use workflow template {Id}", workflowId);
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_CreateFromTemplateFailed"),
                "WfBuilder_CreateFromTemplateFailed",
                "Failed to create workflow from template");
        }
    }

    [RelayCommand]
    private void SelectTemplate(long workflowId)
    {
        SelectedWorkflow = Workflows.FirstOrDefault(workflow => workflow.Id == workflowId && workflow.IsBuiltIn);
        if (SelectedWorkflow is not null)
        {
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_SelectedTemplate", SelectedWorkflow.Name),
                "WfBuilder_SelectedTemplate",
                "Selected template \"{0}\"",
                SelectedWorkflow.Name);
        }
    }

    [RelayCommand]
    private async Task SaveWorkflowAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName)) return;

        // Settings the engine cannot use are reported at their step; saving them would only
        // move the failure to the next run.
        var stepWithBadSettings = EditSteps.FirstOrDefault(step => step.HasConfigError);
        if (stepWithBadSettings is not null)
        {
            var stepNumber = stepWithBadSettings.StepOrder;
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_FixStepSettings", stepNumber),
                "WfBuilder_FixStepSettings",
                "Step {0} has settings that cannot be used. Fix them before saving the workflow.",
                stepNumber);
            return;
        }

        try
        {
            WorkflowEntity workflow;
            string? savedAsCopyOf = null;

            var existing = _editingWorkflowId is { } editingId && editingId > 0
                ? await _workflowService.GetWorkflowAsync(editingId) ?? throw new InvalidOperationException("Workflow not found")
                : null;

            if (existing is { IsBuiltIn: true })
            {
                // Built-in templates are never changed in place; the edits become a new workflow.
                savedAsCopyOf = existing.Name;
                existing = null;
            }

            if (existing is not null)
            {
                // Update the workflow that was opened for editing
                workflow = existing;
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
                        MaxTokensOverride = step.MaxTokensOverride,
                        ConfigJson = SavedConfigJson(step)
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
                        MaxTokensOverride = step.MaxTokensOverride,
                        ConfigJson = SavedConfigJson(step)
                    });
                }
            }

            _editingWorkflowId = null;
            IsEditing = false;
            await LoadWorkflowsAsync();
            StatusMessage = savedAsCopyOf is null
                ? WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_WorkflowSaved", EditName),
                    "WfBuilder_WorkflowSaved",
                    "Workflow \"{0}\" saved",
                    EditName)
                : WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_SavedAsNewWorkflow", savedAsCopyOf, EditName),
                    "WfBuilder_SavedAsNewWorkflow",
                    "Built-in workflow \"{0}\" was not changed; your edits were saved as the new workflow \"{1}\"",
                    savedAsCopyOf, EditName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save workflow");
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_SaveWorkflowFailed"),
                "WfBuilder_SaveWorkflowFailed",
                "Failed to save workflow");
        }
    }

    /// <summary>The settings a step is saved with: a cleared settings box saves no settings.</summary>
    private static string? SavedConfigJson(WorkflowStepItem step) =>
        string.IsNullOrWhiteSpace(step.ConfigJson) ? null : step.ConfigJson;

    [RelayCommand]
    private void CancelEdit()
    {
        _editingWorkflowId = null;
        IsEditing = false;
        EditSteps.Clear();
    }

    [RelayCommand]
    private void AddStep()
    {
        var nextOrder = EditSteps.Count + 1;
        EditSteps.Add(new WorkflowStepItem(_localization)
        {
            StepOrder = nextOrder,
            Name = DefaultStepName(nextOrder),
            StepType = "AiPrompt",
            PromptTemplate = "{{previous_output}}"
        });
    }

    /// <summary>The name a new step starts with.</summary>
    private string DefaultStepName(int stepNumber) => WorkflowBuilderText.Resolve(
        _localization?.GetString("WfBuilder_DefaultStepName", stepNumber),
        "WfBuilder_DefaultStepName",
        "Step {0}",
        stepNumber);

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
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_WorkflowDeleted"),
                "WfBuilder_WorkflowDeleted",
                "Workflow deleted");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete workflow {Id}", workflowId);
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_DeleteWorkflowFailed"),
                "WfBuilder_DeleteWorkflowFailed",
                "Failed to delete workflow");
        }
    }

    [RelayCommand]
    private async Task RunWorkflowAsync(long workflowId)
    {
        if (string.IsNullOrWhiteSpace(RunInput))
        {
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_EnterInputToProcess"),
                "WfBuilder_EnterInputToProcess",
                "Please enter input text to process");
            return;
        }

        ClearFocusedWorkflowRunLanding();
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
        IsShowingStoredRun = false;
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

            if (result.WasCancelled)
            {
                RunErrorMessage = CancelledByUserText();
                RunResultContextText = ShowingCancelledResultText();
                StatusMessage = WorkflowCancelledText();
            }
            else
            {
                RunResultContextText = WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_ShowingLatestResult"),
                    "WfBuilder_ShowingLatestResult",
                    "Showing latest execution result");

                if (!result.Success)
                {
                    RunErrorMessage = WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_StepsFailed"),
                        "WfBuilder_StepsFailed",
                        "One or more steps failed. Check step outputs for details.");
                }

                var duration = result.TotalDurationMs.ToString("F0");
                StatusMessage = result.Success
                    ? WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_WorkflowCompleted", duration),
                        "WfBuilder_WorkflowCompleted",
                        "Workflow completed in {0}ms",
                        duration)
                    : WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_WorkflowFailed"),
                        "WfBuilder_WorkflowFailed",
                        "Workflow failed");
            }

            await LoadWorkflowsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = WorkflowCancelledText();
            RunFailed = true;
            RunErrorMessage = CancelledByUserText();
            RunResultContextText = ShowingCancelledResultText();
            await LoadWorkflowsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Workflow execution failed");
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ExecutionFailed"),
                "WfBuilder_ExecutionFailed",
                "Workflow execution failed");
            RunFailed = true;
            RunErrorMessage = ex.Message;
            RunResultContextText = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ShowingFailedResult"),
                "WfBuilder_ShowingFailedResult",
                "Showing the failed execution result");
            await LoadWorkflowsAsync();
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private string CancelledByUserText() => WorkflowBuilderText.Resolve(
        _localization?.GetString("WfBuilder_RunCancelledByUser"),
        "WfBuilder_RunCancelledByUser",
        "Cancelled by user");

    private string ShowingCancelledResultText() => WorkflowBuilderText.Resolve(
        _localization?.GetString("WfBuilder_ShowingCancelledResult"),
        "WfBuilder_ShowingCancelledResult",
        "Showing the cancelled execution result");

    private string WorkflowCancelledText() => WorkflowBuilderText.Resolve(
        _localization?.GetString("WfBuilder_WorkflowCancelled"),
        "WfBuilder_WorkflowCancelled",
        "Workflow cancelled");

    [RelayCommand]
    private async Task CancelRunAsync()
    {
        _runCts?.Cancel();
        await (_workflowEngine?.CancelExecutionAsync() ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ImportWorkflowAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var workflow = await _workflowService.ImportWorkflowFromJsonAsync(json);
            await LoadWorkflowsAsync();
            SelectedWorkflow = Workflows.FirstOrDefault(item => item.Id == workflow.Id);
            IsEditing = false;
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ImportedWorkflow", workflow.Name),
                "WfBuilder_ImportedWorkflow",
                "Imported workflow \"{0}\"",
                workflow.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import workflow");
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ImportInvalidJson"),
                "WfBuilder_ImportInvalidJson",
                "Import failed — invalid workflow JSON");
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

        ClearFocusedWorkflowRunLanding();
        ApplyHistoricalRun(run);
        StatusMessage = ShowingStoredRunText(run);
    }

    [RelayCommand]
    private void DismissFocusedWorkflowRunLanding()
    {
        var sourceLabel = FocusedWorkflowRunSourceLabel;
        ClearFocusedWorkflowRunLanding();

        if (string.Equals(StatusMessage, sourceLabel, StringComparison.Ordinal))
        {
            StatusMessage = string.Empty;
        }
    }

    partial void OnSelectedWorkflowChanged(WorkflowListItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedWorkflow));
        OnPropertyChanged(nameof(SelectedWorkflowId));
        OnPropertyChanged(nameof(SelectedWorkflowName));
        OnPropertyChanged(nameof(CanRunSelectedWorkflow));
        OnPropertyChanged(nameof(ShowWorkflowStarterEmptyState));
        OnPropertyChanged(nameof(ShowWorkflowRunnerSection));
        OnPropertyChanged(nameof(ShowRecentRunsEmptyState));
        NotifySelectedTemplateGuideChanged();

        if (_pendingOperationsRunRequest is null || value?.Id != _pendingOperationsRunRequest.WorkflowId)
        {
            ClearFocusedWorkflowRunLanding();
        }

        _ = LoadSelectedWorkflowRunsAsync(value);
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunSelectedWorkflow));
        OnPropertyChanged(nameof(CanSaveCurrentResultToVault));
        SaveCurrentResultToVaultCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWorkflowStarterEmptyState));
        OnPropertyChanged(nameof(ShowWorkflowRunnerSection));
        OnPropertyChanged(nameof(CanChangeWorkflowSelection));
    }

    partial void OnRunOutputChanged(string value)
    {
        OnPropertyChanged(nameof(HasRunOutput));
        OnPropertyChanged(nameof(HasRunOutputOrError));
        OnPropertyChanged(nameof(CanSaveCurrentResultToVault));
        SaveCurrentResultToVaultCommand.NotifyCanExecuteChanged();
    }

    partial void OnRunErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasRunOutputOrError));
        OnPropertyChanged(nameof(CanSaveCurrentResultToVault));
        SaveCurrentResultToVaultCommand.NotifyCanExecuteChanged();
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
                RecentRuns.Add(new WorkflowRunHistoryDisplayItem(run, _localization));
            }

            ApplyPendingOperationsRunFocus(workflow.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load recent runs for workflow {WorkflowId}", workflow.Id);
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_LoadRecentRunsFailed"),
                "WfBuilder_LoadRecentRunsFailed",
                "Failed to load recent workflow runs");
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
        IsShowingStoredRun = false;
        StepOutputs.Clear();
    }

    private void NotifySelectedTemplateGuideChanged()
    {
        OnPropertyChanged(nameof(HasSelectedTemplateGuide));
        OnPropertyChanged(nameof(SelectedTemplateGuideSummary));
        OnPropertyChanged(nameof(SelectedTemplateGuideBestFor));
        OnPropertyChanged(nameof(SelectedTemplateGuideOutcome));
        OnPropertyChanged(nameof(SelectedTemplateGuideExamples));
        OnPropertyChanged(nameof(HasSelectedTemplateGuideExamples));
    }

    private void ApplyPendingWorkflowLaunchRequest()
    {
        var request = _workflowLaunchService?.ConsumePendingRequest();
        if (request is null)
        {
            return;
        }

        IsEditing = false;
        ClearRunInspection();
        RunInput = request.InputText;

        if (!string.IsNullOrWhiteSpace(request.RecommendedWorkflowName))
        {
            var recommendedWorkflow = Workflows.FirstOrDefault(workflow =>
                string.Equals(
                    workflow.Name,
                    request.RecommendedWorkflowName,
                    StringComparison.OrdinalIgnoreCase));

            if (recommendedWorkflow is not null)
            {
                SelectedWorkflow = recommendedWorkflow;
            }
        }

        StatusMessage = request.SourceLabel;
    }

    private void ApplyPendingOperationsWorkflowRunRequest()
    {
        var request = _operationsDrillInService?.ConsumePendingWorkflowRunRequest();
        if (request is null)
        {
            return;
        }

        ClearFocusedWorkflowRunLanding();
        _pendingOperationsRunRequest = request;

        var workflow = Workflows.FirstOrDefault(item => item.Id == request.WorkflowId);
        if (workflow is null)
        {
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_RequestedRunUnavailable"),
                "WfBuilder_RequestedRunUnavailable",
                "The requested workflow run is no longer available.");
            _pendingOperationsRunRequest = null;
            return;
        }

        SelectedWorkflow = workflow;
    }

    private void ApplyPendingOperationsRunFocus(long workflowId)
    {
        if (_pendingOperationsRunRequest is null || _pendingOperationsRunRequest.WorkflowId != workflowId)
        {
            return;
        }

        foreach (var run in RecentRuns)
        {
            run.IsFocused = run.RunId == _pendingOperationsRunRequest.RunId;
        }

        var focusedRun = RecentRuns.FirstOrDefault(run => run.RunId == _pendingOperationsRunRequest.RunId);
        if (focusedRun is null)
        {
            ClearFocusedWorkflowRunLanding();
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_RequestedRunNotInHistory"),
                "WfBuilder_RequestedRunNotInHistory",
                "The requested workflow run is no longer in recent history.");
            _pendingOperationsRunRequest = null;
            return;
        }

        var currentIndex = RecentRuns.IndexOf(focusedRun);
        if (currentIndex > 0)
        {
            RecentRuns.Move(currentIndex, 0);
        }

        ApplyHistoricalRun(focusedRun);
        FocusedWorkflowRunSourceLabel = _pendingOperationsRunRequest.SourceLabel;
        StatusMessage = _pendingOperationsRunRequest.SourceLabel;
        _pendingOperationsRunRequest = null;
    }

    partial void OnFocusedWorkflowRunSourceLabelChanged(string value) =>
        OnPropertyChanged(nameof(HasFocusedWorkflowRunLanding));

    private void ClearFocusedWorkflowRunLanding()
    {
        FocusedWorkflowRunSourceLabel = string.Empty;

        foreach (var run in RecentRuns)
        {
            run.IsFocused = false;
        }
    }

    private bool TryResolveFocusedWorkflowRun(
        WorkflowRunHistoryDisplayItem? run,
        string resolutionMessage)
    {
        if (run is null || !run.IsFocused || string.IsNullOrWhiteSpace(FocusedWorkflowRunSourceLabel))
        {
            return false;
        }

        ClearFocusedWorkflowRunLanding();
        StatusMessage = resolutionMessage;
        return true;
    }

    private bool TryResolveFocusedWorkflowRunFromCurrentContext(string resolutionMessage)
    {
        // The stored-run state, not the context text: that text is translated, so its
        // wording cannot say which kind of result is on screen.
        if (!HasFocusedWorkflowRunLanding || !IsShowingStoredRun)
        {
            return false;
        }

        ClearFocusedWorkflowRunLanding();
        StatusMessage = resolutionMessage;
        return true;
    }

    private WorkflowTemplateGuideContent? SelectedTemplateGuide
    {
        get
        {
            if (SelectedWorkflow is not { IsBuiltIn: true })
            {
                return null;
            }

            return FindTemplateGuide(SelectedWorkflow.Name);
        }
    }

    /// <summary>The guide for a built-in template, in the UI language, or null when it has none.</summary>
    private WorkflowTemplateGuideContent? FindTemplateGuide(string workflowName) =>
        TemplateGuideIds.TryGetValue(workflowName, out var id) ? BuildTemplateGuide(id) : null;

    private WorkflowTemplateGuideContent BuildTemplateGuide(TemplateGuideId id) => id switch
    {
        TemplateGuideId.SummarizeAndAct => new(
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideSummarizeActSummary"),
                "WfBuilder_GuideSummarizeActSummary",
                "Turn notes, transcripts, or rough source material into a short summary, key points, and actionable next steps."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideSummarizeActBestFor"),
                "WfBuilder_GuideSummarizeActBestFor",
                "Meeting notes, call transcripts, brainstorm dumps, and long documents you need to turn into clear follow-up work."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideSummarizeActOutcome"),
                "WfBuilder_GuideSummarizeActOutcome",
                "A concise overview, a distilled list of the main points, and a practical action-item list you can execute or share."),
            [
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideSummarizeActExample1"),
                    "WfBuilder_GuideSummarizeActExample1",
                    "Paste a meeting transcript and extract the follow-up actions.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideSummarizeActExample2"),
                    "WfBuilder_GuideSummarizeActExample2",
                    "Drop in a long memo and turn it into key points for your team.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideSummarizeActExample3"),
                    "WfBuilder_GuideSummarizeActExample3",
                    "Use rough brainstorming notes to produce a prioritized action list."))
            ]),

        TemplateGuideId.ResearchBrief => new(
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideResearchBriefSummary"),
                "WfBuilder_GuideResearchBriefSummary",
                "Take a topic, question, or early research dump and turn it into a structured brief with balanced findings."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideResearchBriefBestFor"),
                "WfBuilder_GuideResearchBriefBestFor",
                "Exploring a new topic, preparing for a strategy discussion, or organizing a rough set of research notes."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideResearchBriefOutcome"),
                "WfBuilder_GuideResearchBriefOutcome",
                "An executive summary, background, key findings, opposing views, and a final synthesis you can build from."),
            [
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideResearchBriefExample1"),
                    "WfBuilder_GuideResearchBriefExample1",
                    "Paste a research question and ask for a balanced briefing.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideResearchBriefExample2"),
                    "WfBuilder_GuideResearchBriefExample2",
                    "Use article notes to create a decision-ready summary.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideResearchBriefExample3"),
                    "WfBuilder_GuideResearchBriefExample3",
                    "Turn a rough topic outline into a structured brief for review."))
            ]),

        TemplateGuideId.DocumentReview => new(
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideDocumentReviewSummary"),
                "WfBuilder_GuideDocumentReviewSummary",
                "Review a document, surface what is working, and identify concrete improvements for the next draft."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideDocumentReviewBestFor"),
                "WfBuilder_GuideDocumentReviewBestFor",
                "Draft proposals, client documents, internal memos, landing-page copy, and other writing that needs critique."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideDocumentReviewOutcome"),
                "WfBuilder_GuideDocumentReviewOutcome",
                "A document summary, clear strengths and weaknesses, and a prioritized improvement list."),
            [
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideDocumentReviewExample1"),
                    "WfBuilder_GuideDocumentReviewExample1",
                    "Paste a proposal draft and get actionable revision guidance.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideDocumentReviewExample2"),
                    "WfBuilder_GuideDocumentReviewExample2",
                    "Review internal documentation before sharing it widely.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideDocumentReviewExample3"),
                    "WfBuilder_GuideDocumentReviewExample3",
                    "Use on marketing copy to find weak spots and tighten the message."))
            ]),

        TemplateGuideId.ContentRepurpose => new(
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideContentRepurposeSummary"),
                "WfBuilder_GuideContentRepurposeSummary",
                "Start from one core piece of content and reshape it into multiple publishable formats."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideContentRepurposeBestFor"),
                "WfBuilder_GuideContentRepurposeBestFor",
                "Source material you want to turn into social posts, email copy, and a longer written version."),
            WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_GuideContentRepurposeOutcome"),
                "WfBuilder_GuideContentRepurposeOutcome",
                "A core-message extraction plus adapted outputs for a thread, a professional email, and a blog-style post."),
            [
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideContentRepurposeExample1"),
                    "WfBuilder_GuideContentRepurposeExample1",
                    "Paste a webinar transcript and generate multiple distribution formats.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideContentRepurposeExample2"),
                    "WfBuilder_GuideContentRepurposeExample2",
                    "Turn a founder note into social, email, and blog content.")),
                new WorkflowTemplateGuideExampleItem(WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_GuideContentRepurposeExample3"),
                    "WfBuilder_GuideContentRepurposeExample3",
                    "Use a long-form write-up as the base for a repurposing pass."))
            ]),

        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
    };

    private string GetCurrentResultText()
    {
        if (!string.IsNullOrWhiteSpace(RunOutput))
        {
            return RunOutput;
        }

        return RunErrorMessage;
    }

    private TextArtifactExportItem? BuildCurrentResultArtifact()
    {
        var resultText = GetCurrentResultText();
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return null;
        }

        return BuildWorkflowResultArtifact(
            workflowName: SelectedWorkflowName,
            captureLabel: RunResultContextText,
            resultText: resultText,
            capturedAt: DateTime.UtcNow,
            status: RunFailed
                ? WorkflowRunHistoryDisplayItem.StatusName(_localization, "failed")
                : WorkflowRunHistoryDisplayItem.StatusName(_localization, "completed"),
            totalTokensUsed: RunTotalTokens,
            durationMs: RunDurationMs);
    }

    private TextArtifactExportItem? BuildHistoricalRunArtifact(WorkflowRunHistoryDisplayItem run)
    {
        var resultText = run.GetSaveableContent();
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return null;
        }

        // The name goes into the artifact's title and file name, which stay as they are.
        var workflowName = !string.IsNullOrWhiteSpace(SelectedWorkflowName)
            ? SelectedWorkflowName
            : "Workflow";

        return BuildWorkflowResultArtifact(
            workflowName: workflowName,
            captureLabel: StoredRunCaptureLabel(run),
            resultText: resultText,
            capturedAt: run.StartedAt,
            status: run.StatusText,
            totalTokensUsed: run.TotalTokensUsed,
            durationMs: run.DurationMs);
    }

    /// <summary>Says a stored run is on screen, for the status line and the result context.</summary>
    private string ShowingStoredRunText(WorkflowRunHistoryDisplayItem run) => WorkflowBuilderText.Resolve(
        _localization?.GetString("WfBuilder_ShowingStoredRun", run.StartedAtText),
        "WfBuilder_ShowingStoredRun",
        "Showing stored run from {0}",
        run.StartedAtText);

    /// <summary>The context line a saved or exported stored run carries.</summary>
    private string StoredRunCaptureLabel(WorkflowRunHistoryDisplayItem run) => WorkflowBuilderText.Resolve(
        _localization?.GetString("WfBuilder_StoredRunCaptureLabel", run.StartedAtText),
        "WfBuilder_StoredRunCaptureLabel",
        "Stored run from {0}",
        run.StartedAtText);

    /// <summary>
    /// The artifact a result is saved or exported as. Its metadata names, title and file name
    /// stay as they are; the values shown on the page (context and status) are in the UI language.
    /// </summary>
    private TextArtifactExportItem BuildWorkflowResultArtifact(
        string workflowName,
        string captureLabel,
        string resultText,
        DateTime capturedAt,
        string status,
        long totalTokensUsed,
        double? durationMs)
    {
        var normalizedWorkflowName = string.IsNullOrWhiteSpace(workflowName)
            ? "Workflow Result"
            : workflowName.Trim();
        var timestamp = capturedAt.ToLocalTime().ToString("yyyy-MM-dd_HHmmss");

        var metadata = new Dictionary<string, string>
        {
            ["Workflow"] = normalizedWorkflowName,
            ["Captured"] = capturedAt.ToLocalTime().ToString("yyyy-MM-dd h:mm tt"),
            ["Context"] = string.IsNullOrWhiteSpace(captureLabel)
                ? WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_DefaultCaptureLabel"),
                    "WfBuilder_DefaultCaptureLabel",
                    "Workflow result")
                : captureLabel,
            ["Status"] = status
        };

        if (totalTokensUsed > 0)
        {
            metadata["Tokens"] = totalTokensUsed.ToString();
        }

        if (durationMs is double duration && duration > 0)
        {
            metadata["DurationMs"] = $"{duration:F0}";
        }

        return new TextArtifactExportItem
        {
            Title = $"{normalizedWorkflowName} Result {timestamp}",
            Content = resultText.Trim(),
            Metadata = metadata
        };
    }

    private async Task<DocumentEntity?> SaveWorkflowResultToVaultAsync(
        string workflowName,
        string captureLabel,
        string resultText,
        DateTime capturedAt)
    {
        try
        {
            var normalizedWorkflowName = string.IsNullOrWhiteSpace(workflowName)
                ? "Workflow Result"
                : workflowName.Trim();
            var artifact = BuildWorkflowResultArtifact(
                normalizedWorkflowName,
                captureLabel,
                resultText,
                capturedAt,
                status: WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_RunStatusSaved"),
                    "WfBuilder_RunStatusSaved",
                    "Saved"),
                totalTokensUsed: 0,
                durationMs: null);

            var tempDir = Path.Combine(_appPaths.GetTempPath(), "WorkflowResults");
            Directory.CreateDirectory(tempDir);

            var safeFileName = PathHelper.SanitizeFileName(artifact.Title);
            var tempFilePath = Path.Combine(tempDir, $"{safeFileName}.txt");

            var fileContent = BuildWorkflowResultDocumentContent(artifact);

            await File.WriteAllTextAsync(tempFilePath, fileContent);

            return await _documentService.ImportExternalContentAsync(
                tempFilePath,
                fileTypeOverride: "WorkflowResult",
                displayName: $"{safeFileName}.txt",
                sourceUrl: null,
                collectionId: null,
                ct: default);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save workflow result to vault");
            StatusMessage = WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_SaveResultToVaultFailed"),
                "WfBuilder_SaveResultToVaultFailed",
                "Failed to save workflow result to Knowledge Vault");
            return null;
        }
    }

    private async Task<ExportResult> ExportWorkflowResultAsync(
        TextArtifactExportItem artifact,
        ExportOptions options)
    {
        if (_exportService is null)
        {
            return ExportResult.Fail(WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_ExportServiceUnavailable"),
                "WfBuilder_ExportServiceUnavailable",
                "Export service unavailable."));
        }

        return await _exportService.ExportTextArtifactAsync(artifact, options);
    }

    private static string BuildWorkflowResultDocumentContent(TextArtifactExportItem artifact)
    {
        var metadataLines = artifact.Metadata?
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToArray() ?? Array.Empty<string>();

        return string.Join(
            Environment.NewLine,
            metadataLines
                .Concat(
                [
                    string.Empty,
                    "Result",
                    "------",
                    artifact.Content
                ]));
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
        RunResultContextText = ShowingStoredRunText(run);
        IsShowingStoredRun = true;

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
    private readonly ILocalizationService? _localization;
    private WorkflowStepSettingsProblem? _configProblem;
    private string _configError = string.Empty;

    /// <param name="localization">Translates the settings hint and error; without it they are in English.</param>
    public WorkflowStepItem(ILocalizationService? localization = null)
    {
        _localization = localization;
    }

    [ObservableProperty] private long _id;
    [ObservableProperty] private int _stepOrder;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _stepType = WorkflowStepSettings.AiPrompt;
    [ObservableProperty] private string _promptTemplate = string.Empty;
    [ObservableProperty] private string? _modelOverride;
    [ObservableProperty] private double? _temperatureOverride;
    [ObservableProperty] private int? _maxTokensOverride;

    /// <summary>
    /// Step-specific settings as JSON (lookup collection, transform, branch condition, output
    /// format), edited in the step's settings box and checked by <see cref="WorkflowStepSettings"/>.
    /// </summary>
    [ObservableProperty] private string? _configJson;

    /// <summary>The settings box's text: <see cref="ConfigJson"/>, with no settings as empty text.</summary>
    public string ConfigText
    {
        get => ConfigJson ?? string.Empty;
        set => ConfigJson = value;
    }

    /// <summary>True when the step's type reads settings, which is when the settings box is shown.</summary>
    public bool HasSettings => WorkflowStepSettings.HasSettings(StepType);

    /// <summary>Settings that work for the step's type, shown in the empty settings box.</summary>
    public string ConfigExample => WorkflowStepSettings.Example(StepType);

    /// <summary>What the step's type reads from its settings.</summary>
    public string ConfigHint => DescribeSettings(StepType);

    /// <summary>What is wrong with the settings, or empty when the step can use them.</summary>
    public string ConfigError => _configError;

    /// <summary>True when the engine cannot use the settings as written; the workflow is then not saved.</summary>
    public bool HasConfigError => _configProblem is not null;

    partial void OnStepTypeChanged(string value)
    {
        OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(ConfigExample));
        OnPropertyChanged(nameof(ConfigHint));
        CheckSettings();
    }

    partial void OnConfigJsonChanged(string? value)
    {
        OnPropertyChanged(nameof(ConfigText));
        CheckSettings();
    }

    private void CheckSettings()
    {
        _configProblem = WorkflowStepSettings.Validate(StepType, ConfigJson);
        _configError = _configProblem is null ? string.Empty : DescribeProblem(_configProblem);
        OnPropertyChanged(nameof(ConfigError));
        OnPropertyChanged(nameof(HasConfigError));
    }

    private string DescribeSettings(string? stepType)
    {
        switch (stepType)
        {
            case WorkflowStepSettings.DocumentLookup:
                return WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_StepSettingsHintDocumentLookup"),
                    "WfBuilder_StepSettingsHintDocumentLookup",
                    "Optional. {\"collectionId\": 3} searches only the collection with that ID; without settings, every document is searched. The prompt template is the search query.");

            case WorkflowStepSettings.TextTransform:
                var transforms = string.Join(", ", WorkflowStepSettings.TextTransforms);
                return WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_StepSettingsHintTextTransform", transforms),
                    "WfBuilder_StepSettingsHintTextTransform",
                    "Optional. \"transform\" is one of: {0}. Without settings, the text is made uppercase.",
                    transforms);

            case WorkflowStepSettings.ConditionalBranch:
                var conditions = string.Join(", ", WorkflowStepSettings.Conditions);
                return WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_StepSettingsHintConditionalBranch", conditions),
                    "WfBuilder_StepSettingsHintConditionalBranch",
                    "Required. \"condition\" is one of: {0}. It tests the previous step's output against \"value\", and the step outputs \"trueBranch\" or \"falseBranch\" (the previous output when that one is left out).",
                    conditions);

            case WorkflowStepSettings.OutputFormat:
                var formats = string.Join(", ", WorkflowStepSettings.OutputFormats);
                return WorkflowBuilderText.Resolve(
                    _localization?.GetString("WfBuilder_StepSettingsHintOutputFormat", formats),
                    "WfBuilder_StepSettingsHintOutputFormat",
                    "Optional. \"format\" is one of: {0}. \"prefix\" and \"suffix\" add text before and after the output.",
                    formats);

            default:
                return string.Empty;
        }
    }

    private string DescribeProblem(WorkflowStepSettingsProblem problem)
    {
        var setting = problem.Setting ?? string.Empty;
        var value = problem.Value ?? string.Empty;
        var choices = string.Join(", ", problem.Choices);
        var example = problem.Example ?? string.Empty;

        return problem.Kind switch
        {
            WorkflowStepSettingsProblemKind.Required => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsRequired"),
                "WfBuilder_StepSettingsRequired",
                "This step type needs settings. The empty box shows an example to start from."),

            WorkflowStepSettingsProblemKind.InvalidJson => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsInvalidJson", problem.Line, problem.Position),
                "WfBuilder_StepSettingsInvalidJson",
                "The settings are not valid JSON. Check line {0}, near position {1}.",
                problem.Line, problem.Position),

            WorkflowStepSettingsProblemKind.NotAnObject => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsNotAnObject", example),
                "WfBuilder_StepSettingsNotAnObject",
                "The settings must be one JSON object in braces, for example: {0}",
                example),

            WorkflowStepSettingsProblemKind.UnknownSetting => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsUnknownSetting", setting, choices),
                "WfBuilder_StepSettingsUnknownSetting",
                "This step type has no setting named \"{0}\" (names are case-sensitive). Its settings are: {1}.",
                setting, choices),

            WorkflowStepSettingsProblemKind.NotText => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsNotText", setting),
                "WfBuilder_StepSettingsNotText",
                "\"{0}\" must be text in double quotes.",
                setting),

            WorkflowStepSettingsProblemKind.NotAWholeNumber => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsNotAWholeNumber", setting, example),
                "WfBuilder_StepSettingsNotAWholeNumber",
                "\"{0}\" must be a whole number, for example {1}.",
                setting, example),

            WorkflowStepSettingsProblemKind.UnknownChoice => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsUnknownChoice", setting, value, choices),
                "WfBuilder_StepSettingsUnknownChoice",
                "\"{0}\" cannot be \"{1}\". Use one of: {2}.",
                setting, value, choices),

            WorkflowStepSettingsProblemKind.InvalidPattern => WorkflowBuilderText.Resolve(
                _localization?.GetString("WfBuilder_StepSettingsInvalidPattern", setting),
                "WfBuilder_StepSettingsInvalidPattern",
                "\"{0}\" is not a valid regular expression for the matches condition.",
                setting),

            _ => string.Empty,
        };
    }
}

/// <summary>Picks the text of a workflow builder message.</summary>
internal static class WorkflowBuilderText
{
    /// <summary>
    /// Returns <paramref name="localized"/>, the text the localization service found, unless
    /// there was no service or it found no resource (it then answers with the key itself); in
    /// that case returns <paramref name="english"/>, formatted with <paramref name="args"/>.
    /// </summary>
    public static string Resolve(string? localized, string key, string english, params object[] args)
    {
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
        {
            return localized;
        }

        return args.Length == 0 ? english : string.Format(CultureInfo.CurrentCulture, english, args);
    }
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

public sealed partial class WorkflowRunHistoryDisplayItem : ObservableObject
{
    private readonly ILocalizationService? _localization;

    /// <param name="run">The stored run.</param>
    /// <param name="localization">Translates the status and details; without it they are in English.</param>
    public WorkflowRunHistoryDisplayItem(WorkflowRunHistoryItem run, ILocalizationService? localization = null)
    {
        _localization = localization;
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

    [ObservableProperty] private bool _isFocused;

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
    public string StatusText => StatusName(_localization, Status);

    /// <summary>The name shown for a stored run status ("completed", "failed" and so on).</summary>
    internal static string StatusName(ILocalizationService? localization, string status) => status switch
    {
        "completed" => WorkflowBuilderText.Resolve(
            localization?.GetString("WfBuilder_RunStatusCompleted"), "WfBuilder_RunStatusCompleted", "Completed"),
        "failed" => WorkflowBuilderText.Resolve(
            localization?.GetString("WfBuilder_RunStatusFailed"), "WfBuilder_RunStatusFailed", "Failed"),
        "cancelled" => WorkflowBuilderText.Resolve(
            localization?.GetString("WfBuilder_RunStatusCancelled"), "WfBuilder_RunStatusCancelled", "Cancelled"),
        "running" => WorkflowBuilderText.Resolve(
            localization?.GetString("WfBuilder_RunStatusRunning"), "WfBuilder_RunStatusRunning", "Running"),
        _ => WorkflowBuilderText.Resolve(
            localization?.GetString("WfBuilder_RunStatusPending"), "WfBuilder_RunStatusPending", "Pending")
    };

    public string DetailText
    {
        get
        {
            var parts = new List<string>
            {
                TotalSteps == 1
                    ? WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_RunDetailStepsOne", StepsCompleted, TotalSteps),
                        "WfBuilder_RunDetailStepsOne",
                        "{0}/{1} step",
                        StepsCompleted, TotalSteps)
                    : WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_RunDetailStepsMany", StepsCompleted, TotalSteps),
                        "WfBuilder_RunDetailStepsMany",
                        "{0}/{1} steps",
                        StepsCompleted, TotalSteps)
            };

            if (TotalTokensUsed > 0)
            {
                parts.Add(TotalTokensUsed == 1
                    ? WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_RunDetailTokensOne", TotalTokensUsed),
                        "WfBuilder_RunDetailTokensOne",
                        "{0} token",
                        TotalTokensUsed)
                    : WorkflowBuilderText.Resolve(
                        _localization?.GetString("WfBuilder_RunDetailTokensMany", TotalTokensUsed),
                        "WfBuilder_RunDetailTokensMany",
                        "{0} tokens",
                        TotalTokensUsed));
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

    public string GetSaveableContent()
    {
        if (!string.IsNullOrWhiteSpace(FinalOutput))
        {
            return FinalOutput;
        }

        return ErrorMessage;
    }
}

public sealed class WorkflowTemplateGuideContent
{
    public WorkflowTemplateGuideContent(
        string summary,
        string bestFor,
        string outcome,
        IReadOnlyList<WorkflowTemplateGuideExampleItem> examples)
    {
        Summary = summary;
        BestFor = bestFor;
        Outcome = outcome;
        Examples = examples;
    }

    public string Summary { get; }
    public string BestFor { get; }
    public string Outcome { get; }
    public IReadOnlyList<WorkflowTemplateGuideExampleItem> Examples { get; }
}

public sealed class WorkflowTemplateGuideExampleItem
{
    public WorkflowTemplateGuideExampleItem(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class WorkflowStarterTemplateDisplayItem
{
    public WorkflowStarterTemplateDisplayItem(
        long id,
        string name,
        string category,
        string summary,
        string bestFor)
    {
        Id = id;
        Name = name;
        Category = category;
        Summary = summary;
        BestFor = bestFor;
    }

    public long Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Summary { get; }
    public string BestFor { get; }
}
