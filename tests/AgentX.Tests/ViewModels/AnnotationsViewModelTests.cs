using AgentX.App.ViewModels;
using AgentX.Core.Services.Annotations;
using AgentX.Core.Services.Localization;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class AnnotationsViewModelTests
{
    [Fact]
    public async Task ExportAnnotationsCommand_SendsGeneratedMarkdownToSaveHandler()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations
            .Setup(service => service.ExportAnnotationsAsMarkdownAsync(It.IsAny<long?>()))
            .ReturnsAsync("# Agent-X Annotations");

        var vm = new AnnotationsViewModel(annotations.Object, EnglishResources.Create())
        {
            TotalCount = 3,
        };

        AnnotationMarkdownExportRequest? capturedRequest = null;
        vm.SaveMarkdownExportAsync = request =>
        {
            capturedRequest = request;
            return Task.FromResult(AnnotationMarkdownExportResult.Saved(@"C:\Exports\annotations.md"));
        };

        await vm.ExportAnnotationsCommand.ExecuteAsync(null);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Markdown.Should().Be("# Agent-X Annotations");
        capturedRequest.SuggestedFileName.Should().StartWith("agent-x-annotations-");
        capturedRequest.SuggestedFileName.Should().EndWith(".md");
        vm.StatusMessage.Should().Be("Exported 3 annotations to annotations.md");
    }

    [Fact]
    public async Task ExportAnnotationsCommand_DoesNotClaimSuccessWhenSaveIsCancelled()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations
            .Setup(service => service.ExportAnnotationsAsMarkdownAsync(It.IsAny<long?>()))
            .ReturnsAsync("# Agent-X Annotations");

        var vm = new AnnotationsViewModel(annotations.Object, EnglishResources.Create())
        {
            TotalCount = 2,
            SaveMarkdownExportAsync = _ => Task.FromResult(AnnotationMarkdownExportResult.Cancelled()),
        };

        await vm.ExportAnnotationsCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("Export cancelled");
    }

    [Fact]
    public async Task ExportAnnotationsCommand_CountsASingleAnnotation()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations
            .Setup(service => service.ExportAnnotationsAsMarkdownAsync(It.IsAny<long?>()))
            .ReturnsAsync("# Agent-X Annotations");
        var saved = Path.Combine(Path.GetTempPath(), "notes.md");
        var vm = new AnnotationsViewModel(annotations.Object, EnglishResources.Create())
        {
            TotalCount = 1,
            SaveMarkdownExportAsync = _ => Task.FromResult(AnnotationMarkdownExportResult.Saved(saved)),
        };

        await vm.ExportAnnotationsCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("Exported 1 annotation to notes.md");
    }

    [Fact]
    public async Task Status_messages_come_from_the_resources()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString("Annot_Deleted")).Returns("Annotation gelöscht");
        var annotations = new Mock<IAnnotationService>();
        annotations.Setup(service => service.GetColorDistributionAsync())
            .ReturnsAsync(new Dictionary<string, int>());
        var vm = new AnnotationsViewModel(annotations.Object, localization.Object);

        await vm.DeleteAnnotationCommand.ExecuteAsync(7L);

        vm.StatusMessage.Should().Be("Annotation gelöscht");
    }

    // ── Editing ──────────────────────────────────────────────────────────────
    // The edit flow existed in the view model with no control anywhere in the page.
    // The colour picker for editing must not offer the "All" filter sentinel as a colour.

    [Fact]
    public void EditColorOptions_OfferRealColoursOnly()
    {
        var vm = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), EnglishResources.Create());

        vm.EditColorOptions.Should().Equal("yellow", "green", "blue", "red", "purple");
        vm.EditColorOptions.Should().NotContain("All");
    }

    [Fact]
    public void EditAnnotationCommand_LoadsTheAnnotationIntoTheEditor()
    {
        var vm = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), EnglishResources.Create());
        var item = new AnnotationDisplayItem
        {
            Id = 7,
            NoteText = "Check this against Q3",
            Color = "blue",
        };

        vm.EditAnnotationCommand.Execute(item);

        vm.IsEditing.Should().BeTrue();
        vm.SelectedAnnotation.Should().BeSameAs(item);
        vm.EditNoteText.Should().Be("Check this against Q3");
        vm.EditColor.Should().Be("blue");
    }

    [Fact]
    public void CancelEditCommand_ClosesTheEditorWithoutSaving()
    {
        var annotations = new Mock<IAnnotationService>();
        var vm = new AnnotationsViewModel(annotations.Object, EnglishResources.Create());
        vm.EditAnnotationCommand.Execute(new AnnotationDisplayItem { Id = 7, NoteText = "note", Color = "red" });

        vm.CancelEditCommand.Execute(null);

        vm.IsEditing.Should().BeFalse();
        annotations.Verify(
            service => service.UpdateAnnotationAsync(
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }
}
