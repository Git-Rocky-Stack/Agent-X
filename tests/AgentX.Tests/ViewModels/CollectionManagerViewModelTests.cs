using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Collections;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class CollectionManagerViewModelTests
{
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<IDocumentService> _documentService = new();

    // ── Rename ───────────────────────────────────────────────────────────────
    // Rename logged "rename requested" and returned without calling the service, so a
    // collection could never actually be renamed.

    [Fact]
    public void BeginRenameCollectionCommand_OpensTheEditorOnTheChosenCollection()
    {
        var viewModel = CreateViewModel();
        var item = new CollectionDisplayItem { Id = 4, Name = "Reserach", Description = "typo" };

        viewModel.BeginRenameCollectionCommand.Execute(item);

        viewModel.IsRenaming.Should().BeTrue();
        viewModel.RenameTarget.Should().BeSameAs(item);
        viewModel.RenameName.Should().Be("Reserach");
    }

    [Fact]
    public async Task RenameCollectionCommand_PersistsTheNewNameAndUpdatesTheList()
    {
        var viewModel = CreateViewModel();
        var item = new CollectionDisplayItem { Id = 4, Name = "Reserach", Description = "typo" };
        viewModel.Collections.Add(item);
        viewModel.BeginRenameCollectionCommand.Execute(item);
        viewModel.RenameName = "Research";

        await viewModel.RenameCollectionCommand.ExecuteAsync(null);

        _collectionService.Verify(
            service => service.UpdateCollectionAsync(4, "Research", (string?)"typo"),
            Times.Once);
        item.Name.Should().Be("Research");
        viewModel.IsRenaming.Should().BeFalse();
    }

    [Fact]
    public async Task RenameCollectionCommand_WithABlankName_DoesNotTouchTheService()
    {
        var viewModel = CreateViewModel();
        var item = new CollectionDisplayItem { Id = 4, Name = "Research" };
        viewModel.BeginRenameCollectionCommand.Execute(item);
        viewModel.RenameName = "   ";

        await viewModel.RenameCollectionCommand.ExecuteAsync(null);

        _collectionService.Verify(
            service => service.UpdateCollectionAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>()!),
            Times.Never);
        item.Name.Should().Be("Research");
    }

    [Fact]
    public void CancelRenameCommand_ClosesTheEditorWithoutRenaming()
    {
        var viewModel = CreateViewModel();
        var item = new CollectionDisplayItem { Id = 4, Name = "Research" };
        viewModel.BeginRenameCollectionCommand.Execute(item);

        viewModel.CancelRenameCommand.Execute(null);

        viewModel.IsRenaming.Should().BeFalse();
        viewModel.RenameTarget.Should().BeNull();
    }

    // ── Multi-select ─────────────────────────────────────────────────────────
    // Selection state has to live on the item so a checkbox can bind to it; the id list
    // alone cannot drive a per-row control.

    [Fact]
    public void ToggleCollectionSelectionCommand_MarksTheItemSelected()
    {
        var viewModel = CreateViewModel();
        var item = new CollectionDisplayItem { Id = 9, Name = "Finance" };
        viewModel.Collections.Add(item);

        viewModel.ToggleCollectionSelectionCommand.Execute(9L);

        item.IsSelected.Should().BeTrue();
        viewModel.SelectedCount.Should().Be(1);

        viewModel.ToggleCollectionSelectionCommand.Execute(9L);

        item.IsSelected.Should().BeFalse();
        viewModel.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void SelectAllCollectionsCommand_MarksEveryItemSelected()
    {
        var viewModel = CreateViewModel();
        viewModel.Collections.Add(new CollectionDisplayItem { Id = 1, Name = "A" });
        viewModel.Collections.Add(new CollectionDisplayItem { Id = 2, Name = "B" });

        viewModel.SelectAllCollectionsCommand.Execute(null);

        viewModel.Collections.Should().OnlyContain(item => item.IsSelected);
        viewModel.SelectedCount.Should().Be(2);
    }

    [Fact]
    public void ToggleMultiSelectCommand_WhenSwitchedOff_ClearsEveryItemSelection()
    {
        var viewModel = CreateViewModel();
        var item = new CollectionDisplayItem { Id = 1, Name = "A" };
        viewModel.Collections.Add(item);
        viewModel.ToggleMultiSelectCommand.Execute(null);
        viewModel.SelectAllCollectionsCommand.Execute(null);

        viewModel.ToggleMultiSelectCommand.Execute(null);

        viewModel.IsMultiSelectMode.Should().BeFalse();
        item.IsSelected.Should().BeFalse();
        viewModel.SelectedCount.Should().Be(0);
    }

    [Fact]
    public async Task BulkDeleteCollectionsCommand_DeletesEverySelectedCollection()
    {
        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());
        _collectionService.Setup(service => service.GetCollectionCountAsync()).ReturnsAsync(0);

        var viewModel = CreateViewModel();
        viewModel.Collections.Add(new CollectionDisplayItem { Id = 1, Name = "A" });
        viewModel.Collections.Add(new CollectionDisplayItem { Id = 2, Name = "B" });
        viewModel.SelectAllCollectionsCommand.Execute(null);

        await viewModel.BulkDeleteCollectionsCommand.ExecuteAsync(null);

        _collectionService.Verify(service => service.DeleteCollectionAsync(1, false), Times.Once);
        _collectionService.Verify(service => service.DeleteCollectionAsync(2, false), Times.Once);
        viewModel.SelectedCount.Should().Be(0);
    }

    private CollectionManagerViewModel CreateViewModel() =>
        new(_collectionService.Object, _documentService.Object);
}
