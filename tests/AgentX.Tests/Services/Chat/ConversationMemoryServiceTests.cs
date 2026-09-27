using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Chat;

/// <summary>
/// Memories could be counted but not listed or removed anywhere in the app, and the only removal
/// the service offered was a dismissal that keeps the text in the database. These pin the deletes
/// the chat's memory list uses, on a real SQLite context built from the model (which carries the
/// foreign key on LinkedMemoryId that older installs have).
/// </summary>
public sealed class ConversationMemoryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();

    public ConversationMemoryServiceTests()
    {
        _db = _factory.CreateContext();
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
        _logger.Dispose();
    }

    private ConversationMemoryService CreateSut() => new(_db, Mock.Of<IAiService>(), _logger);

    private static MemoryEntity Memory(string content, bool active = true, double importance = 0.5) => new()
    {
        Content = content,
        Category = "fact",
        Importance = importance,
        IsActive = active,
    };

    [Fact]
    public async Task DeleteMemoryAsync_removes_the_row_and_its_text()
    {
        var keep = Memory("Works on Agent-X");
        var delete = Memory("Prefers dark mode");
        _db.Memories.AddRange(keep, delete);
        await _db.SaveChangesAsync();

        var deleted = await CreateSut().DeleteMemoryAsync(delete.Id);

        deleted.Should().BeTrue();
        using var check = _factory.CreateContext();
        (await check.Memories.Select(m => m.Content).ToListAsync()).Should().Equal("Works on Agent-X");
    }

    [Fact]
    public async Task DeleteMemoryAsync_clears_links_to_the_deleted_memory()
    {
        var target = Memory("Uses SQLite");
        _db.Memories.Add(target);
        await _db.SaveChangesAsync();
        var linking = Memory("Uses SQLCipher");
        linking.LinkedMemoryId = target.Id;
        _db.Memories.Add(linking);
        await _db.SaveChangesAsync();

        var deleted = await CreateSut().DeleteMemoryAsync(target.Id);

        deleted.Should().BeTrue();
        using var check = _factory.CreateContext();
        var remaining = await check.Memories.SingleAsync();
        remaining.Content.Should().Be("Uses SQLCipher");
        remaining.LinkedMemoryId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteMemoryAsync_for_an_unknown_memory_reports_that_nothing_was_deleted()
    {
        (await CreateSut().DeleteMemoryAsync(99999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAllMemoriesAsync_deletes_every_memory_including_dismissed_ones()
    {
        var first = Memory("First");
        _db.Memories.Add(first);
        await _db.SaveChangesAsync();
        var linked = Memory("Second");
        linked.LinkedMemoryId = first.Id;
        _db.Memories.AddRange(linked, Memory("Dismissed", active: false));
        await _db.SaveChangesAsync();

        var deleted = await CreateSut().DeleteAllMemoriesAsync();

        deleted.Should().Be(3);
        using var check = _factory.CreateContext();
        (await check.Memories.CountAsync()).Should().Be(0);
        (await CreateSut().GetMemoryCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetAllMemoriesAsync_lists_active_memories_without_tracking_them()
    {
        _db.Memories.AddRange(
            Memory("Low", importance: 0.2),
            Memory("High", importance: 0.9),
            Memory("Dismissed", active: false, importance: 1.0));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var memories = await CreateSut().GetAllMemoriesAsync();

        memories.Select(m => m.Content).Should().Equal("High", "Low");
        _db.ChangeTracker.Entries<MemoryEntity>().Should().BeEmpty(
            "the shared context's change tracker is also used by the background extraction");
    }
}
