using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Workspace;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class WorkspaceProfileViewModelTests
{
    private readonly Mock<IWorkspaceProfileService> _profileService = new();

    [Fact]
    public async Task InitializeAsync_loads_profiles_and_leaves_editor_cleared()
    {
        _profileService.Setup(service => service.GetAllProfilesAsync())
            .ReturnsAsync(
            [
                CreateProfile(1, "Research", isDefault: true),
                CreateProfile(2, "Writing")
            ]);

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, EnglishResources.Create());

        await viewModel.InitializeAsync();

        viewModel.HasProfiles.Should().BeTrue();
        viewModel.HasSelectedProfile.Should().BeFalse();
        viewModel.Profiles.Should().HaveCount(2);
        viewModel.Profiles[0].Name.Should().Be("Research");
        viewModel.EditName.Should().BeEmpty();
        viewModel.EditIsDefault.Should().BeFalse();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task CreateProfileAsync_adds_profile_selects_it_and_clears_inputs()
    {
        var created = CreateProfile(10, "Strategy Sprint", description: "Daily research context");

        _profileService.Setup(service => service.CreateProfileAsync("Strategy Sprint", "Daily research context"))
            .ReturnsAsync(created);

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, EnglishResources.Create())
        {
            NewProfileName = "  Strategy Sprint  ",
            NewProfileDescription = "  Daily research context  "
        };

        await viewModel.CreateProfileCommand.ExecuteAsync(null);

        viewModel.Profiles.Should().ContainSingle();
        viewModel.SelectedProfile.Should().NotBeNull();
        viewModel.SelectedProfile!.Id.Should().Be(10);
        viewModel.SelectedProfile.Name.Should().Be("Strategy Sprint");
        viewModel.NewProfileName.Should().BeEmpty();
        viewModel.NewProfileDescription.Should().BeEmpty();
        viewModel.EditName.Should().Be("Strategy Sprint");
        viewModel.StatusMessage.Should().Be("Profile \"Strategy Sprint\" created successfully.");
        viewModel.HasStatusMessage.Should().BeTrue();
        viewModel.CanCreateProfile.Should().BeFalse();
    }

    [Fact]
    public async Task SaveProfileAsync_updates_selected_profile_and_promotes_default()
    {
        var existing = CreateProfile(
            2,
            "Writing",
            description: "Draft mode",
            activeModelId: "llama3.1:8b",
            activeCollectionIds: "4,7",
            customSettings: "{\"theme\":\"light\"}",
            isDefault: false);

        _profileService.Setup(service => service.GetAllProfilesAsync())
            .ReturnsAsync(
            [
                CreateProfile(1, "Research", isDefault: true),
                existing
            ]);
        _profileService.Setup(service => service.GetProfileAsync(2))
            .ReturnsAsync(existing);
        _profileService.Setup(service => service.UpdateProfileAsync(It.IsAny<WorkspaceProfileEntity>()))
            .Callback<WorkspaceProfileEntity>(profile => profile.UpdatedAt = new DateTime(2026, 4, 22, 18, 30, 0, DateTimeKind.Utc))
            .Returns(Task.CompletedTask);
        _profileService.Setup(service => service.SetDefaultProfileAsync(2))
            .Returns(Task.CompletedTask);

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, EnglishResources.Create());
        await viewModel.InitializeAsync();

        viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Id == 2);
        viewModel.EditName = "  Writing Focus  ";
        viewModel.EditDescription = "  Updated description  ";
        viewModel.EditActiveModelId = "  mistral:latest  ";
        viewModel.EditActiveCollectionIds = "  9,12  ";
        viewModel.EditCustomSettings = "  {\"theme\":\"dark\"}  ";
        viewModel.EditIsDefault = true;

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        _profileService.Verify(
            service => service.UpdateProfileAsync(
                It.Is<WorkspaceProfileEntity>(profile =>
                    profile.Id == 2 &&
                    profile.Name == "Writing Focus" &&
                    profile.Description == "Updated description" &&
                    profile.ActiveModelId == "mistral:latest" &&
                    profile.ActiveCollectionIds == "9,12" &&
                    profile.CustomSettings == "{\"theme\":\"dark\"}")),
            Times.Once);
        _profileService.Verify(service => service.SetDefaultProfileAsync(2), Times.Once);

        viewModel.SelectedProfile.Should().NotBeNull();
        viewModel.SelectedProfile!.Name.Should().Be("Writing Focus");
        viewModel.SelectedProfile.IsDefault.Should().BeTrue();
        viewModel.Profiles.Single(profile => profile.Id == 1).IsDefault.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("Profile \"Writing Focus\" saved successfully.");
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task SaveProfileAsync_with_the_default_switch_turned_off_clears_the_default()
    {
        var existing = CreateProfile(1, "Research", isDefault: true);
        _profileService.Setup(service => service.GetAllProfilesAsync())
            .ReturnsAsync([existing]);
        _profileService.Setup(service => service.GetProfileAsync(1))
            .ReturnsAsync(existing);
        _profileService.Setup(service => service.UpdateProfileAsync(It.IsAny<WorkspaceProfileEntity>()))
            .Returns(Task.CompletedTask);
        _profileService.Setup(service => service.ClearDefaultProfileAsync(1))
            .Returns(Task.CompletedTask);

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, EnglishResources.Create());
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = viewModel.Profiles.Single();
        viewModel.EditIsDefault = false;

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        // Previously only turning the switch on was handled and this save kept the default.
        _profileService.Verify(service => service.ClearDefaultProfileAsync(1), Times.Once);
        _profileService.Verify(service => service.SetDefaultProfileAsync(It.IsAny<long>()), Times.Never);
        viewModel.SelectedProfile!.IsDefault.Should().BeFalse();
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task SaveProfileAsync_saves_the_same_profile_repeatedly_with_the_real_service()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = new WorkspaceProfileService(db);
        await service.CreateProfileAsync("Research");
        await service.CreateProfileAsync("Writing");

        var viewModel = new WorkspaceProfileViewModel(service, EnglishResources.Create());
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Name == "Research");

        viewModel.EditActiveModelId = "llama3.2";
        viewModel.EditIsDefault = true;
        await viewModel.SaveProfileCommand.ExecuteAsync(null);
        viewModel.HasError.Should().BeFalse(viewModel.ErrorMessage);

        // The second save of the same profile failed with an EF tracking error before.
        viewModel.EditActiveModelId = "mistral:latest";
        viewModel.EditIsDefault = false;
        await viewModel.SaveProfileCommand.ExecuteAsync(null);
        viewModel.HasError.Should().BeFalse(viewModel.ErrorMessage);

        using var verify = factory.CreateContext();
        var stored = await verify.WorkspaceProfiles.AsNoTracking().SingleAsync(profile => profile.Name == "Research");
        stored.ActiveModelId.Should().Be("mistral:latest");
        stored.IsDefault.Should().BeFalse();
        (await verify.WorkspaceProfiles.AsNoTracking().CountAsync(profile => profile.IsDefault)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteProfileAsync_removes_selected_profile_and_clears_selection()
    {
        _profileService.Setup(service => service.GetAllProfilesAsync())
            .ReturnsAsync([CreateProfile(5, "Temporary")]);
        _profileService.Setup(service => service.DeleteProfileAsync(5))
            .Returns(Task.CompletedTask);

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, EnglishResources.Create());
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = viewModel.Profiles[0];

        await viewModel.DeleteProfileCommand.ExecuteAsync(null);

        viewModel.Profiles.Should().BeEmpty();
        viewModel.SelectedProfile.Should().BeNull();
        viewModel.HasProfiles.Should().BeFalse();
        viewModel.HasSelectedProfile.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("Profile \"Temporary\" deleted.");
    }

    [Fact]
    public async Task DuplicateProfileAsync_adds_copy_and_selects_it()
    {
        _profileService.Setup(service => service.GetAllProfilesAsync())
            .ReturnsAsync([CreateProfile(7, "Research")]);
        _profileService.Setup(service => service.DuplicateProfileAsync(7, "Research (Copy)"))
            .ReturnsAsync(CreateProfile(8, "Research (Copy)"));

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, EnglishResources.Create());
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = viewModel.Profiles[0];

        await viewModel.DuplicateProfileCommand.ExecuteAsync(null);

        viewModel.Profiles.Should().HaveCount(2);
        viewModel.SelectedProfile.Should().NotBeNull();
        viewModel.SelectedProfile!.Id.Should().Be(8);
        viewModel.SelectedProfile.Name.Should().Be("Research (Copy)");
        viewModel.StatusMessage.Should().Be("Profile duplicated as \"Research (Copy)\".");
    }

    [Fact]
    public async Task DuplicateProfileAsync_names_the_copy_in_the_users_language()
    {
        var german = ReswLocalization.For("de");
        var copyName = german.GetString("Workspace_CopyName", "Research");
        _profileService.Setup(service => service.GetAllProfilesAsync())
            .ReturnsAsync([CreateProfile(7, "Research")]);
        _profileService.Setup(service => service.DuplicateProfileAsync(7, copyName))
            .ReturnsAsync(CreateProfile(8, copyName));

        var viewModel = new WorkspaceProfileViewModel(_profileService.Object, german);
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = viewModel.Profiles[0];

        await viewModel.DuplicateProfileCommand.ExecuteAsync(null);

        copyName.Should().NotBe("Research (Copy)");
        _profileService.Verify(service => service.DuplicateProfileAsync(7, copyName), Times.Once);
        viewModel.StatusMessage.Should().Be(german.GetString("Workspace_ProfileDuplicated", copyName));
    }

    private static WorkspaceProfileEntity CreateProfile(
        long id,
        string name,
        string? description = null,
        string? activeModelId = null,
        string? activeCollectionIds = null,
        string? customSettings = null,
        bool isDefault = false)
    {
        var createdAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc);

        return new WorkspaceProfileEntity
        {
            Id = id,
            Name = name,
            Description = description,
            ActiveModelId = activeModelId,
            ActiveCollectionIds = activeCollectionIds,
            CustomSettings = customSettings,
            IsDefault = isDefault,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(id)
        };
    }
}
