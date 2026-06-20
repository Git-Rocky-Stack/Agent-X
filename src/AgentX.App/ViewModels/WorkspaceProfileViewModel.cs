using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

// =====================================================================
// WORKSPACE PROFILE VIEW MODEL
//
// Manages workspace profiles that let users save and restore complete
// workspace configurations (active model, collections, custom settings).
// Two-panel master/detail layout: profile list on the left, editor on
// the right.
//
// Depends on IWorkspaceProfileService for all persistence operations.
// =====================================================================

public partial class WorkspaceProfileViewModel : ObservableObject, IDisposable
{
    // -- Services -----------------------------------------------------
    private readonly IWorkspaceProfileService _profileService;

    // -- Page State ----------------------------------------------------
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    // -- Selection -----------------------------------------------------
    [ObservableProperty] private ProfileDisplayItem? _selectedProfile;

    // -- Create-New Inputs ---------------------------------------------
    [ObservableProperty] private string _newProfileName = string.Empty;
    [ObservableProperty] private string _newProfileDescription = string.Empty;

    // -- Editor Fields (bound when a profile is selected) ---------------
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private string _editActiveModelId = string.Empty;
    [ObservableProperty] private string _editActiveCollectionIds = string.Empty;
    [ObservableProperty] private string _editCustomSettings = string.Empty;
    [ObservableProperty] private bool _editIsDefault;

    // -- Collections ---------------------------------------------------
    public ObservableCollection<ProfileDisplayItem> Profiles { get; } = new();

    // -- Computed Properties -------------------------------------------
    public bool HasProfiles => Profiles.Count > 0;
    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool CanCreateProfile => !string.IsNullOrWhiteSpace(NewProfileName);

    // -- Constructor ---------------------------------------------------
    public WorkspaceProfileViewModel(IWorkspaceProfileService profileService)
    {
        _profileService = profileService;
        Log.Debug("WorkspaceProfileViewModel created with services");
    }

    // =================================================================
    // INITIALIZATION
    // =================================================================

    public async Task InitializeAsync()
    {
        Log.Information("WorkspaceProfileViewModel initializing...");

        try
        {
            IsLoading = true;
            ClearError();
            ClearStatus();

            await LoadProfilesInternalAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize WorkspaceProfileViewModel");
            SetError("Failed to load workspace profiles. Please try refreshing.");
        }
        finally
        {
            IsLoading = false;
        }

        Log.Information("WorkspaceProfileViewModel initialized with {Count} profiles", Profiles.Count);
    }

    // =================================================================
    // PROPERTY CHANGE HOOKS
    // =================================================================

    partial void OnSelectedProfileChanged(ProfileDisplayItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedProfile));

        if (value is not null)
        {
            // Populate the editor fields with the selected profile's data
            EditName = value.Name;
            EditDescription = value.Description ?? string.Empty;
            EditActiveModelId = value.ActiveModelId ?? string.Empty;
            EditActiveCollectionIds = value.ActiveCollectionIds ?? string.Empty;
            EditCustomSettings = value.CustomSettings ?? string.Empty;
            EditIsDefault = value.IsDefault;
        }
        else
        {
            // Clear the editor when nothing is selected
            EditName = string.Empty;
            EditDescription = string.Empty;
            EditActiveModelId = string.Empty;
            EditActiveCollectionIds = string.Empty;
            EditCustomSettings = string.Empty;
            EditIsDefault = false;
        }
    }

    partial void OnNewProfileNameChanged(string value)
    {
        CreateProfileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCreateProfile));
    }

    // =================================================================
    // COMMANDS
    // =================================================================

    /// <summary>
    /// Reloads the full profile list from the service.
    /// </summary>
    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        Log.Debug("Load profiles requested");

        try
        {
            IsLoading = true;
            ClearError();

            await LoadProfilesInternalAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reload profiles");
            SetError("Failed to refresh profiles. Please try again.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Creates a new workspace profile from the create-new input fields.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateProfile))]
    private async Task CreateProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;

        var name = NewProfileName.Trim();
        var description = string.IsNullOrWhiteSpace(NewProfileDescription)
            ? null
            : NewProfileDescription.Trim();

        Log.Information("Creating workspace profile: {Name}", name);
        ClearError();
        ClearStatus();

        try
        {
            var entity = await _profileService.CreateProfileAsync(name, description);

            var newItem = MapEntityToDisplay(entity);
            Profiles.Add(newItem);
            OnPropertyChanged(nameof(HasProfiles));

            // Clear input fields
            NewProfileName = string.Empty;
            NewProfileDescription = string.Empty;

            // Select the newly created profile
            SelectedProfile = newItem;

            SetStatus($"Profile \"{name}\" created successfully.");
            Log.Information("Workspace profile created: {Name} (ID: {Id})", name, entity.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create workspace profile: {Name}", name);
            SetError($"Failed to create profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the editor fields back to the currently selected profile.
    /// </summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (SelectedProfile is null) return;

        Log.Information("Saving workspace profile: {ProfileId}", SelectedProfile.Id);
        ClearError();
        ClearStatus();

        try
        {
            // Fetch the full entity so we can update it
            var entity = await _profileService.GetProfileAsync(SelectedProfile.Id);
            if (entity is null)
            {
                SetError("Profile no longer exists. It may have been deleted.");
                return;
            }

            // Apply editor values to the entity
            entity.Name = EditName.Trim();
            entity.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim();
            entity.ActiveModelId = string.IsNullOrWhiteSpace(EditActiveModelId) ? null : EditActiveModelId.Trim();
            entity.ActiveCollectionIds = string.IsNullOrWhiteSpace(EditActiveCollectionIds) ? null : EditActiveCollectionIds.Trim();
            entity.CustomSettings = string.IsNullOrWhiteSpace(EditCustomSettings) ? null : EditCustomSettings.Trim();

            await _profileService.UpdateProfileAsync(entity);

            // Update the display item in-place
            SelectedProfile.Name = entity.Name;
            SelectedProfile.Description = entity.Description;
            SelectedProfile.ActiveModelId = entity.ActiveModelId;
            SelectedProfile.ActiveCollectionIds = entity.ActiveCollectionIds;
            SelectedProfile.CustomSettings = entity.CustomSettings;
            SelectedProfile.UpdatedAt = entity.UpdatedAt;
            SelectedProfile.UpdatedAtFormatted = FormatHelper.TimeAgoWithMonths(entity.UpdatedAt);

            // Handle default status change
            if (EditIsDefault && !SelectedProfile.IsDefault)
            {
                await _profileService.SetDefaultProfileAsync(SelectedProfile.Id);

                // Clear all other defaults in the UI
                foreach (var profile in Profiles)
                {
                    profile.IsDefault = profile.Id == SelectedProfile.Id;
                }
            }

            // Force the list to re-render the updated item
            RefreshProfileInList(SelectedProfile);

            SetStatus($"Profile \"{entity.Name}\" saved successfully.");
            Log.Information("Workspace profile saved: {ProfileId}", SelectedProfile.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save workspace profile: {ProfileId}", SelectedProfile?.Id);
            SetError($"Failed to save profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes the currently selected profile.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null) return;

        var profileId = SelectedProfile.Id;
        var profileName = SelectedProfile.Name;

        Log.Information("Deleting workspace profile: {ProfileId} ({Name})", profileId, profileName);
        ClearError();
        ClearStatus();

        try
        {
            await _profileService.DeleteProfileAsync(profileId);

            // Remove from local collection
            var toRemove = Profiles.FirstOrDefault(p => p.Id == profileId);
            if (toRemove is not null)
            {
                Profiles.Remove(toRemove);
            }

            SelectedProfile = null;
            OnPropertyChanged(nameof(HasProfiles));

            SetStatus($"Profile \"{profileName}\" deleted.");
            Log.Information("Workspace profile deleted: {ProfileId}", profileId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete workspace profile: {ProfileId}", profileId);
            SetError($"Failed to delete profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the currently selected profile as the default workspace profile.
    /// </summary>
    [RelayCommand]
    private async Task SetDefaultAsync()
    {
        if (SelectedProfile is null) return;

        Log.Information("Setting default workspace profile: {ProfileId}", SelectedProfile.Id);
        ClearError();
        ClearStatus();

        try
        {
            await _profileService.SetDefaultProfileAsync(SelectedProfile.Id);

            // Update all items: only the selected one is default
            foreach (var profile in Profiles)
            {
                profile.IsDefault = profile.Id == SelectedProfile.Id;
            }

            EditIsDefault = true;

            // Force re-render of the list to update badges
            var items = Profiles.ToList();
            Profiles.Clear();
            foreach (var item in items)
            {
                Profiles.Add(item);
            }
            OnPropertyChanged(nameof(HasProfiles));

            // Restore selection
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == SelectedProfile?.Id);

            SetStatus($"Profile \"{SelectedProfile?.Name}\" is now the default.");
            Log.Information("Default workspace profile set: {ProfileId}", SelectedProfile?.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set default workspace profile");
            SetError($"Failed to set default profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Duplicates the currently selected profile with a new name.
    /// </summary>
    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile is null) return;

        var sourceId = SelectedProfile.Id;
        var newName = $"{SelectedProfile.Name} (Copy)";

        Log.Information("Duplicating workspace profile: {SourceId} as \"{NewName}\"", sourceId, newName);
        ClearError();
        ClearStatus();

        try
        {
            var duplicated = await _profileService.DuplicateProfileAsync(sourceId, newName);

            var newItem = MapEntityToDisplay(duplicated);
            Profiles.Add(newItem);
            OnPropertyChanged(nameof(HasProfiles));

            // Select the duplicated profile
            SelectedProfile = newItem;

            SetStatus($"Profile duplicated as \"{newName}\".");
            Log.Information("Workspace profile duplicated: {SourceId} -> {NewId}", sourceId, duplicated.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to duplicate workspace profile: {SourceId}", sourceId);
            SetError($"Failed to duplicate profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Selects a profile from the sidebar list.
    /// Called from code-behind click handlers.
    /// </summary>
    [RelayCommand]
    private void SelectProfile(ProfileDisplayItem? profile)
    {
        if (profile is null) return;

        Log.Debug("Profile selected: {ProfileId} ({Name})", profile.Id, profile.Name);
        SelectedProfile = profile;
    }

    // =================================================================
    // PRIVATE HELPERS
    // =================================================================

    private async Task LoadProfilesInternalAsync()
    {
        Profiles.Clear();
        SelectedProfile = null;

        var entities = await _profileService.GetAllProfilesAsync();

        foreach (var entity in entities)
        {
            Profiles.Add(MapEntityToDisplay(entity));
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasSelectedProfile));
    }

    private static ProfileDisplayItem MapEntityToDisplay(WorkspaceProfileEntity entity)
    {
        return new ProfileDisplayItem
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            ActiveModelId = entity.ActiveModelId,
            ActiveCollectionIds = entity.ActiveCollectionIds,
            CustomSettings = entity.CustomSettings,
            IsDefault = entity.IsDefault,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            CreatedAtFormatted = entity.CreatedAt.ToString("MMM d, yyyy"),
            UpdatedAtFormatted = FormatHelper.TimeAgoWithMonths(entity.UpdatedAt)
        };
    }

    /// <summary>
    /// Replaces a profile in the ObservableCollection so bindings refresh.
    /// </summary>
    private void RefreshProfileInList(ProfileDisplayItem updatedItem)
    {
        var index = -1;
        for (var i = 0; i < Profiles.Count; i++)
        {
            if (Profiles[i].Id == updatedItem.Id)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            Profiles[index] = updatedItem;
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatusMessage = true;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        HasStatusMessage = false;
    }

    // =================================================================
    // DISPOSAL
    // =================================================================

    public void Dispose()
    {
        Log.Debug("WorkspaceProfileViewModel disposed");
    }
}

// =====================================================================
// PROFILE DISPLAY ITEM
//
// Lightweight observable sub-ViewModel that wraps a WorkspaceProfileEntity
// for display in the two-panel Workspace Profiles UI.
// =====================================================================

public partial class ProfileDisplayItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _activeModelId;
    [ObservableProperty] private string? _activeCollectionIds;
    [ObservableProperty] private string? _customSettings;
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private DateTime _createdAt;
    [ObservableProperty] private DateTime _updatedAt;
    [ObservableProperty] private string _createdAtFormatted = string.Empty;
    [ObservableProperty] private string _updatedAtFormatted = string.Empty;

    /// <summary>
    /// A short summary line for the sidebar: shows active model or a fallback.
    /// </summary>
    public string SummaryLine => string.IsNullOrWhiteSpace(ActiveModelId)
        ? "No model configured"
        : $"Model: {ActiveModelId}";
}
