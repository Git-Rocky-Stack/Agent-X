using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workflows.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class WorkflowBuilderViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyDictionary<string, WorkflowTemplateGuideContent> TemplateGuideCatalog =
        new Dictionary<string, WorkflowTemplateGuideContent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Summarize & Act"] = new(
                "Turn notes, transcripts, or rough source material into a short summary, key points, and actionable next steps.",
                "Meeting notes, call transcripts, brainstorm dumps, and long documents you need to turn into clear follow-up work.",
                "A concise overview, a distilled list of the main points, and a practical action-item list you can execute or share.",
                [
                    new WorkflowTemplateGuideExampleItem("Paste a meeting transcript and extract the follow-up actions."),
                    new WorkflowTemplateGuideExampleItem("Drop in a long memo and turn it into key points for your team."),
                    new WorkflowTemplateGuideExampleItem("Use rough brainstorming notes to produce a prioritized action list.")
                ]),
            ["Research Brief"] = new(
                "Take a topic, question, or early research dump and turn it into a structured brief with balanced findings.",
                "Exploring a new topic, preparing for a strategy discussion, or organizing a rough set of research notes.",
                "An executive summary, background, key findings, opposing views, and a final synthesis you can build from.",
                [
                    new WorkflowTemplateGuideExampleItem("Paste a research question and ask for a balanced briefing."),
                    new WorkflowTemplateGuideExampleItem("Use article notes to create a decision-ready summary."),
                    new WorkflowTemplateGuideExampleItem("Turn a rough topic outline into a structured brief for review.")
                ]),
            ["Document Review"] = new(
                "Review a document, surface what is working, and identify concrete improvements for the next draft.",
                "Draft proposals, client documents, internal memos, landing-page copy, and other writing that needs critique.",
                "A document summary, clear strengths and weaknesses, and a prioritized improvement list.",
                [
                    new WorkflowTemplateGuideExampleItem("Paste a proposal draft and get actionable revision guidance."),
                    new WorkflowTemplateGuideExampleItem("Review internal documentation before sharing it widely."),
                    new WorkflowTemplateGuideExampleItem("Use on marketing copy to find weak spots and tighten the message.")
                ]),
            ["Content Repurpose"] = new(
                "Start from one core piece of content and reshape it into multiple publishable formats.",
                "Source material you want to turn into social posts, email copy, and a longer written version.",
                "A core-message extraction plus adapted outputs for a thread, a professional email, and a blog-style post.",
                [
                    new WorkflowTemplateGuideExampleItem("Paste a webinar transcript and generate multiple distribution formats."),
                    new WorkflowTemplateGuideExampleItem("Turn a founder note into social, email, and blog content."),
                    new WorkflowTemplateGuideExampleItem("Use a long-form write-up as the base for a repurposing pass.")
                ])
        };

    // ── Services ─────────────────────────────────────────────
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IModelManager _modelManager;
    private readonly IDocumentService _documentService;
    private readonly IExportService? _exportService;
    private readonly IWorkflowLaunchService? _workflowLaunchService;
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private readonly IAppPathService _appPaths;

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
    [ObservableProperty] private string _lastSavedWorkflowDocumentName = string.Empty;
    public ObservableCollection<StepOutputItem> StepOutputs { get; } = new();

    // ── Models ───────────────────────────────────────────────
    public ObservableCollection<AiModel> AvailableModels { get; } = new();
    public NavigateHandler? NavigateRequested { get; set; }

    // ── Category Options ─────────────────────────────────────
    public List<string> Categories { get; } = new() { "Custom", "Research", "Writing", "Analysis", "Productivity" };
    public List<string> StepTypes { get; } = new() { "AiPrompt", "DocumentLookup", "TextTransform" };
    public bool HasSelectedWorkflow => SelectedWorkflow is not null;
    public long SelectedWorkflowId => SelectedWorkflow?.Id ?? 0;
    public string SelectedWorkflowName => SelectedWorkflow?.Name ?? string.Empty;
    public bool CanRunSelectedWorkflow => SelectedWorkflow is not null && !IsRunning;
    public bool ShowWorkflowStarterEmptyState => !IsEditing && !HasSelectedWorkflow;
    public bool ShowWorkflowRunnerSection => !IsEditing && HasSelectedWorkflow;
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
                var summary = TemplateGuideCatalog.TryGetValue(workflow.Name, out var guide)
                    ? guide.Summary
                    : workflow.Description;

                var bestFor = TemplateGuideCatalog.TryGetValue(workflow.Name, out guide)
                    ? guide.BestFor
                    : workflow.Category;

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

    public WorkflowBuilderViewModel(
        IWorkflowService workflowService,
        IWorkflowEngine workflowEngine,
        IModelManager modelManager,
        IDocumentService documentService,
        IExportService? exportService = null,
        IWorkflowLaunchService? workflowLaunchService = null,
        IOperationsDrillInService? operationsDrillInService = null,
        IAppPathService? appPathService = null)
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
            StatusMessage = "No workflow result available to save";
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
            if (TryResolveFocusedWorkflowRunFromCurrentContext(
                    $"Resolved the focused workflow run by saving it to Knowledge Vault as \"{document.FileName}\"."))
            {
                return;
            }

            StatusMessage = $"Saved workflow result to Knowledge Vault as \"{document.FileName}\"";
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
            StatusMessage = "This stored run does not have result content to save";
            return;
        }

        var workflowName = !string.IsNullOrWhiteSpace(SelectedWorkflowName)
            ? SelectedWorkflowName
            : "Workflow";

        var document = await SaveWorkflowResultToVaultAsync(
            workflowName: workflowName,
            captureLabel: $"Stored run from {run.StartedAtText}",
            resultText: resultText,
            capturedAt: run.StartedAt);

        if (document is not null)
        {
            LastSavedWorkflowDocumentName = document.FileName;
            if (TryResolveFocusedWorkflowRun(run,
                    $"Resolved the focused workflow run by saving it to Knowledge Vault as \"{document.FileName}\"."))
            {
                return;
            }

            StatusMessage = $"Saved stored workflow result to Knowledge Vault as \"{document.FileName}\"";
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
            return ExportResult.Fail("No workflow result available to export.");
        }

        var result = await ExportWorkflowResultAsync(artifact, options);
        if (result.Success)
        {
            var fileName = Path.GetFileName(result.FilePath);
            if (TryResolveFocusedWorkflowRunFromCurrentContext(
                    $"Resolved the focused workflow run by exporting it to {fileName}."))
            {
                return result;
            }

            StatusMessage = $"Exported workflow result to {fileName}";
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
            return ExportResult.Fail("No workflow run selected for export.");
        }

        var artifact = BuildHistoricalRunArtifact(run);
        if (artifact is null)
        {
            return ExportResult.Fail("This stored run does not have result content to export.");
        }

        var result = await ExportWorkflowResultAsync(artifact, options);
        if (result.Success)
        {
            var fileName = Path.GetFileName(result.FilePath);
            if (TryResolveFocusedWorkflowRun(run,
                    $"Resolved the focused workflow run by exporting it to {fileName}."))
            {
                return result;
            }

            StatusMessage = $"Exported stored workflow result to {fileName}";
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

    public async Task<string?> GetWorkflowExportJsonAsync(long workflowId)
    {
        try
        {
            return await _workflowService.ExportWorkflowAsJsonAsync(workflowId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export workflow {Id}", workflowId);
            StatusMessage = "Export failed";
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
            StatusMessage = $"Created workflow \"{clonedWorkflow.Name}\" from template";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to use workflow template {Id}", workflowId);
            StatusMessage = "Failed to create workflow from template";
        }
    }

    [RelayCommand]
    private void SelectTemplate(long workflowId)
    {
        SelectedWorkflow = Workflows.FirstOrDefault(workflow => workflow.Id == workflowId && workflow.IsBuiltIn);
        if (SelectedWorkflow is not null)
        {
            StatusMessage = $"Selected template \"{SelectedWorkflow.Name}\"";
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
    private async Task ImportWorkflowAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var workflow = await _workflowService.ImportWorkflowFromJsonAsync(json);
            await LoadWorkflowsAsync();
            SelectedWorkflow = Workflows.FirstOrDefault(item => item.Id == workflow.Id);
            IsEditing = false;
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

        ClearFocusedWorkflowRunLanding();
        ApplyHistoricalRun(run);
        StatusMessage = $"Showing stored run from {run.StartedAtText}";
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
                RecentRuns.Add(new WorkflowRunHistoryDisplayItem(run));
            }

            ApplyPendingOperationsRunFocus(workflow.Id);
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
            StatusMessage = "The requested workflow run is no longer available.";
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
            StatusMessage = "The requested workflow run is no longer in recent history.";
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
        if (!HasFocusedWorkflowRunLanding
            || string.IsNullOrWhiteSpace(RunResultContextText)
            || !RunResultContextText.StartsWith("Showing stored run from", StringComparison.OrdinalIgnoreCase))
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

            return TemplateGuideCatalog.TryGetValue(SelectedWorkflow.Name, out var guide)
                ? guide
                : null;
        }
    }

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
            status: RunFailed ? "Failed" : "Completed",
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

        var workflowName = !string.IsNullOrWhiteSpace(SelectedWorkflowName)
            ? SelectedWorkflowName
            : "Workflow";

        return BuildWorkflowResultArtifact(
            workflowName: workflowName,
            captureLabel: $"Stored run from {run.StartedAtText}",
            resultText: resultText,
            capturedAt: run.StartedAt,
            status: run.StatusText,
            totalTokensUsed: run.TotalTokensUsed,
            durationMs: run.DurationMs);
    }

    private static TextArtifactExportItem BuildWorkflowResultArtifact(
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
            ["Context"] = string.IsNullOrWhiteSpace(captureLabel) ? "Workflow result" : captureLabel,
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
                status: "Saved",
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
            StatusMessage = "Failed to save workflow result to Knowledge Vault";
            return null;
        }
    }

    private async Task<ExportResult> ExportWorkflowResultAsync(
        TextArtifactExportItem artifact,
        ExportOptions options)
    {
        if (_exportService is null)
        {
            return ExportResult.Fail("Export service unavailable.");
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

public sealed partial class WorkflowRunHistoryDisplayItem : ObservableObject
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
