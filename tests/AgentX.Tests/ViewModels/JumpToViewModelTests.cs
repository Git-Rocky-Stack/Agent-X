using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentX.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class JumpToViewModelTests
{
    [Fact]
    public async Task Load_populates_results_from_loader()
    {
        var candidates = new List<JumpToItem>
        {
            new("p.docs", "Documents", null, JumpToItemKind.Page, _ => Task.CompletedTask),
            new("d.1", "Annual Report 2026.pdf", null, JumpToItemKind.Document, _ => Task.CompletedTask),
        };
        var sut = new JumpToViewModel(_ => Task.FromResult((IReadOnlyList<JumpToItem>)candidates));

        await sut.LoadAsync();

        sut.Results.Select(r => r.Id).Should().BeEquivalentTo("p.docs", "d.1");
    }

    [Fact]
    public async Task Query_fuzzy_filters_candidates()
    {
        var candidates = new List<JumpToItem>
        {
            new("d.1", "Annual Report 2026.pdf", null, JumpToItemKind.Document, _ => Task.CompletedTask),
            new("d.2", "Meeting Notes.md", null, JumpToItemKind.Document, _ => Task.CompletedTask),
        };
        var sut = new JumpToViewModel(_ => Task.FromResult((IReadOnlyList<JumpToItem>)candidates));
        await sut.LoadAsync();

        sut.Query = "annual";

        sut.Results.Select(r => r.Id).Should().ContainSingle().Which.Should().Be("d.1");
    }

    [Fact]
    public async Task Execute_invokes_item_open_action()
    {
        var opened = false;
        var item = new JumpToItem(
            "c.1",
            "Planning Chat",
            "Conversation",
            JumpToItemKind.Conversation,
            _ =>
            {
                opened = true;
                return Task.CompletedTask;
            });
        var sut = new JumpToViewModel(_ => Task.FromResult((IReadOnlyList<JumpToItem>)new[] { item }));

        await sut.ExecuteAsync(item);

        opened.Should().BeTrue();
    }
}
