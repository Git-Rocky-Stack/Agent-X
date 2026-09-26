using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Workspace;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentX.Tests.Services.Workspace;

/// <summary>
/// WS14: the service runs on the application's long-lived shared context, so entities stay
/// tracked between calls. These tests use one context for the service (like the app) and a
/// second context to read what was actually stored.
/// </summary>
public sealed class WorkspaceProfileServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly WorkspaceProfileService _sut;

    public WorkspaceProfileServiceTests()
    {
        _db = _factory.CreateContext();
        _sut = new WorkspaceProfileService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task<Dictionary<string, bool>> StoredDefaultFlagsAsync()
    {
        using var verify = _factory.CreateContext();
        return await verify.WorkspaceProfiles.AsNoTracking().ToDictionaryAsync(p => p.Name, p => p.IsDefault);
    }

    [Fact]
    public async Task UpdateProfileAsync_saves_the_same_profile_twice_from_detached_copies()
    {
        var created = await _sut.CreateProfileAsync("Research");

        // The view model's flow: fetch a detached copy, edit it, save it; then again.
        var first = (await _sut.GetProfileAsync(created.Id))!;
        first.ActiveModelId = "llama3.2";
        await _sut.UpdateProfileAsync(first);

        var second = (await _sut.GetProfileAsync(created.Id))!;
        second.ActiveModelId = "mistral:latest";
        second.Name = "Research Focus";
        var act = () => _sut.UpdateProfileAsync(second);

        // Previously Update(detached) threw "another instance with the same key value is
        // already being tracked" here.
        await act.Should().NotThrowAsync();
        using var verify = _factory.CreateContext();
        var stored = await verify.WorkspaceProfiles.AsNoTracking().SingleAsync();
        stored.Name.Should().Be("Research Focus");
        stored.ActiveModelId.Should().Be("mistral:latest");
        second.UpdatedAt.Should().Be(stored.UpdatedAt, "the caller's instance carries the saved timestamp");
    }

    [Fact]
    public async Task SetDefaultProfileAsync_switching_A_then_B_then_A_leaves_A_as_the_only_default()
    {
        var a = await _sut.CreateProfileAsync("A");
        var b = await _sut.CreateProfileAsync("B");

        await _sut.SetDefaultProfileAsync(a.Id);
        await _sut.SetDefaultProfileAsync(b.Id);
        await _sut.SetDefaultProfileAsync(a.Id);

        // Previously the third call read IsDefault = true from the stale tracked instance of A
        // and returned without promoting it, leaving B as the stored default.
        (await StoredDefaultFlagsAsync()).Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["A"] = true,
            ["B"] = false,
        });
        (await _sut.GetDefaultProfileAsync())!.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task SetDefaultProfileAsync_refreshes_instances_the_context_still_tracks()
    {
        var a = await _sut.CreateProfileAsync("A");
        var b = await _sut.CreateProfileAsync("B");

        await _sut.SetDefaultProfileAsync(a.Id);
        await _sut.SetDefaultProfileAsync(b.Id);

        // CreateProfileAsync returned the tracked instances.
        a.IsDefault.Should().BeFalse();
        b.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SetDefaultProfileAsync_for_the_current_default_keeps_it()
    {
        var a = await _sut.CreateProfileAsync("A");
        await _sut.SetDefaultProfileAsync(a.Id);

        await _sut.SetDefaultProfileAsync(a.Id);

        (await StoredDefaultFlagsAsync())["A"].Should().BeTrue();
    }

    [Fact]
    public async Task ClearDefaultProfileAsync_leaves_no_default_and_the_profile_can_be_promoted_again()
    {
        var a = await _sut.CreateProfileAsync("A");
        await _sut.SetDefaultProfileAsync(a.Id);

        await _sut.ClearDefaultProfileAsync(a.Id);

        (await _sut.GetDefaultProfileAsync()).Should().BeNull();
        (await StoredDefaultFlagsAsync())["A"].Should().BeFalse();

        await _sut.SetDefaultProfileAsync(a.Id);
        (await StoredDefaultFlagsAsync())["A"].Should().BeTrue();
    }

    [Fact]
    public async Task ClearDefaultProfileAsync_on_a_profile_that_is_not_the_default_keeps_the_real_default()
    {
        var a = await _sut.CreateProfileAsync("A");
        var b = await _sut.CreateProfileAsync("B");
        await _sut.SetDefaultProfileAsync(a.Id);

        await _sut.ClearDefaultProfileAsync(b.Id);

        (await _sut.GetDefaultProfileAsync())!.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task ClearDefaultProfileAsync_for_a_missing_profile_throws()
    {
        var act = () => _sut.ClearDefaultProfileAsync(404);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*404*");
    }

    [Fact]
    public async Task UpdateProfileAsync_does_not_change_the_default_flag()
    {
        var a = await _sut.CreateProfileAsync("A");
        var b = await _sut.CreateProfileAsync("B");
        await _sut.SetDefaultProfileAsync(a.Id);

        // A caller holding a copy with a wrong IsDefault must not create a second default.
        var copy = (await _sut.GetProfileAsync(b.Id))!;
        copy.IsDefault = true;
        copy.Description = "edited";
        await _sut.UpdateProfileAsync(copy);

        (await StoredDefaultFlagsAsync()).Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["A"] = true,
            ["B"] = false,
        });
        (await _sut.GetProfileAsync(b.Id))!.Description.Should().Be("edited");
    }

    [Fact]
    public async Task UpdateProfileAsync_for_a_deleted_profile_throws_a_clear_error()
    {
        var a = await _sut.CreateProfileAsync("A");
        await _sut.DeleteProfileAsync(a.Id);

        var act = () => _sut.UpdateProfileAsync(new WorkspaceProfileEntity { Id = a.Id, Name = "A" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
    }
}
