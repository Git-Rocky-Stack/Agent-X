using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Annotations;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The vault preview's annotation panel. Nothing in the app called CreateAnnotationAsync, so the
/// Annotations page was always empty and highlights never reached Temporal Identity.
/// </summary>
public sealed class DocumentNotesViewModelTests
{
    private const string PassageText = "Latency budgets matter. Budgets beat heroics.";

    private readonly Mock<IAnnotationService> _annotations = new();

    public DocumentNotesViewModelTests()
    {
        // A document has no annotations unless a test says otherwise.
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(It.IsAny<long>()))
            .ReturnsAsync(Array.Empty<AnnotationEntity>());
    }

    [Fact]
    public async Task ShowDocumentAsync_ShowsTheFirstPassageAndTheAnnotationsNewestFirst()
    {
        SetupPassages(7, PassageText, "Second passage.");
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(7))
            .ReturnsAsync(
            [
                Annotation(1, "Latency", createdMinutesAgo: 30),
                Annotation(2, "heroics", createdMinutesAgo: 5, note: "Keep this"),
            ]);
        var viewModel = CreateViewModel();

        await viewModel.ShowDocumentAsync(7);

        viewModel.HasPassage.Should().BeTrue();
        viewModel.PassageText.Should().Be(PassageText);
        viewModel.PassageNumber.Should().Be(1);
        viewModel.PassageCount.Should().Be(2);
        viewModel.ShowSelectionHint.Should().BeTrue();
        viewModel.ShowNoTextHint.Should().BeFalse();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.Annotations.Select(a => a.Id).Should().Equal(2L, 1L);
        viewModel.Annotations[0].NoteText.Should().Be("Keep this");
        viewModel.HasAnnotations.Should().BeTrue();
    }

    [Fact]
    public async Task ShowDocumentAsync_ForADocumentWithoutIndexedText_SaysThereIsNothingToAnnotate()
    {
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(7))
            .ReturnsAsync(Array.Empty<AnnotationEntity>());
        var viewModel = CreateViewModel();

        await viewModel.ShowDocumentAsync(7);

        viewModel.HasPassage.Should().BeFalse();
        viewModel.ShowNoTextHint.Should().BeTrue();
        viewModel.ShowSelectionHint.Should().BeFalse();
        viewModel.NextPassageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SelectingTextAndSaving_CreatesTheAnnotationOnThatPassage()
    {
        SetupPassages(7, PassageText);
        AnnotationEntity? created = null;
        _annotations
            .Setup(service => service.CreateAnnotationAsync(
                It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((long documentId, long? chunkId, int start, int end, string text, string color, string? note) =>
            {
                created = new AnnotationEntity
                {
                    Id = 40,
                    DocumentId = documentId,
                    ChunkId = chunkId,
                    StartOffset = start,
                    EndOffset = end,
                    HighlightedText = text,
                    Color = color,
                    NoteText = note,
                    CreatedAt = DateTime.UtcNow,
                };
                return created;
            });
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        viewModel.CaptureSelection("Budgets beat heroics.", positionHint: 24);
        viewModel.HasDraft.Should().BeTrue();
        viewModel.DraftHighlight.Should().Be("Budgets beat heroics.");
        viewModel.SaveAnnotationCommand.CanExecute(null).Should().BeTrue();

        viewModel.DraftNote = "  The team's motto  ";
        await viewModel.SaveAnnotationCommand.ExecuteAsync(null);

        _annotations.Verify(service => service.CreateAnnotationAsync(
            7, 700, 24, 45, "Budgets beat heroics.", "yellow", "The team's motto"), Times.Once);
        created.Should().NotBeNull();
        viewModel.Annotations.Should().ContainSingle().Which.Id.Should().Be(40);
        viewModel.HasAnnotations.Should().BeTrue();
        viewModel.HasDraft.Should().BeFalse();
        viewModel.DraftHighlight.Should().BeEmpty();
        viewModel.DraftNote.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingWithABlankNote_SavesNoNote()
    {
        SetupPassages(7, PassageText);
        SetupCreateReturnsId(41);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        viewModel.CaptureSelection("Latency", positionHint: -1);
        viewModel.DraftNote = "   ";
        await viewModel.SaveAnnotationCommand.ExecuteAsync(null);

        _annotations.Verify(service => service.CreateAnnotationAsync(
            7, 700, 0, 7, "Latency", "yellow", null), Times.Once);
    }

    [Fact]
    public async Task AnEmptySelection_KeepsTheDraft()
    {
        // The passage loses its selection when focus moves to the note box or the Save button.
        SetupPassages(7, PassageText);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        viewModel.CaptureSelection("heroics", positionHint: 37);
        viewModel.CaptureSelection(string.Empty, positionHint: 0);
        viewModel.CaptureSelection(null, positionHint: -1);

        viewModel.HasDraft.Should().BeTrue();
        viewModel.DraftHighlight.Should().Be("heroics");
    }

    [Fact]
    public async Task ASelectionThatIsNotInThePassage_IsIgnored()
    {
        SetupPassages(7, PassageText);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        viewModel.CaptureSelection("text from somewhere else", positionHint: 0);

        viewModel.HasDraft.Should().BeFalse();
        viewModel.SaveAnnotationCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ARepeatedWord_IsSavedWhereItWasSelected()
    {
        SetupPassages(7, PassageText);
        SetupCreateReturnsId(42);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        // "udgets" occurs at 9 and at 25; the viewer reported the second one.
        viewModel.CaptureSelection("udgets", positionHint: 26);
        await viewModel.SaveAnnotationCommand.ExecuteAsync(null);

        _annotations.Verify(service => service.CreateAnnotationAsync(
            7, 700, 25, 31, "udgets", "yellow", null), Times.Once);
    }

    [Fact]
    public async Task WhenSavingFails_TheDraftStaysAndThePanelSaysSo()
    {
        SetupPassages(7, PassageText);
        _annotations
            .Setup(service => service.CreateAnnotationAsync(
                It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);
        viewModel.CaptureSelection("Latency", positionHint: 0);
        viewModel.DraftNote = "check";

        await viewModel.SaveAnnotationCommand.ExecuteAsync(null);

        viewModel.HasDraft.Should().BeTrue();
        viewModel.DraftNote.Should().Be("check");
        viewModel.Annotations.Should().BeEmpty();
        viewModel.HasStatusMessage.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("The annotation could not be saved.");
    }

    [Fact]
    public async Task CancelAnnotation_DropsTheDraft()
    {
        SetupPassages(7, PassageText);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);
        viewModel.CaptureSelection("Latency", positionHint: 0);
        viewModel.DraftNote = "draft";

        viewModel.CancelAnnotationCommand.Execute(null);

        viewModel.HasDraft.Should().BeFalse();
        viewModel.DraftNote.Should().BeEmpty();
        _annotations.Verify(
            service => service.CreateAnnotationAsync(
                It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task NextAndPreviousPassage_PageThroughTheDocument_AndDropTheDraft()
    {
        SetupPassages(7, "First passage.", "Second passage.", "Third passage.");
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);
        viewModel.PreviousPassageCommand.CanExecute(null).Should().BeFalse();
        viewModel.CaptureSelection("First", positionHint: 0);

        await viewModel.NextPassageCommand.ExecuteAsync(null);

        viewModel.PassageText.Should().Be("Second passage.");
        viewModel.PassageNumber.Should().Be(2);
        viewModel.HasDraft.Should().BeFalse("a draft belongs to the passage it was selected in");
        viewModel.PreviousPassageCommand.CanExecute(null).Should().BeTrue();

        await viewModel.NextPassageCommand.ExecuteAsync(null);
        viewModel.PassageText.Should().Be("Third passage.");
        viewModel.NextPassageCommand.CanExecute(null).Should().BeFalse();

        await viewModel.PreviousPassageCommand.ExecuteAsync(null);
        viewModel.PassageNumber.Should().Be(2);

        // A selection in the second passage is saved against the second chunk.
        SetupCreateReturnsId(43);
        viewModel.CaptureSelection("Second", positionHint: 0);
        await viewModel.SaveAnnotationCommand.ExecuteAsync(null);
        _annotations.Verify(service => service.CreateAnnotationAsync(
            7, 701, 0, 6, "Second", "yellow", null), Times.Once);
    }

    [Fact]
    public async Task DeleteAnnotation_RemovesItFromTheService_AndTheList()
    {
        SetupPassages(7, PassageText);
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(7))
            .ReturnsAsync([Annotation(1, "Latency", createdMinutesAgo: 3), Annotation(2, "heroics", createdMinutesAgo: 1)]);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        await viewModel.DeleteAnnotationCommand.ExecuteAsync(2L);

        _annotations.Verify(service => service.DeleteAnnotationAsync(2), Times.Once);
        viewModel.Annotations.Select(a => a.Id).Should().Equal(1L);

        await viewModel.DeleteAnnotationCommand.ExecuteAsync(1L);
        viewModel.HasAnnotations.Should().BeFalse();
    }

    [Fact]
    public async Task WhenDeletingFails_TheAnnotationStaysListed()
    {
        SetupPassages(7, PassageText);
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(7))
            .ReturnsAsync([Annotation(1, "Latency", createdMinutesAgo: 3)]);
        _annotations.Setup(service => service.DeleteAnnotationAsync(1))
            .ThrowsAsync(new InvalidOperationException("database is locked"));
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);

        await viewModel.DeleteAnnotationCommand.ExecuteAsync(1L);

        viewModel.Annotations.Should().ContainSingle();
        viewModel.StatusMessage.Should().Be("The annotation could not be deleted.");
    }

    [Fact]
    public async Task ASlowLoadForADocumentLeftBehind_DoesNotOverwriteTheOneShownNow()
    {
        var slowPassage = new TaskCompletionSource<AnnotationPassage?>();
        _annotations.Setup(service => service.GetPassageAsync(1, 0, It.IsAny<CancellationToken>()))
            .Returns(slowPassage.Task);
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(1))
            .ReturnsAsync([Annotation(9, "old", createdMinutesAgo: 1)]);
        SetupPassages(2, "The document shown now.");
        var viewModel = CreateViewModel();

        var first = viewModel.ShowDocumentAsync(1);
        await viewModel.ShowDocumentAsync(2);
        slowPassage.SetResult(new AnnotationPassage(100, 0, 1, null, "The document left behind."));
        await first;

        viewModel.PassageText.Should().Be("The document shown now.");
        viewModel.Annotations.Should().BeEmpty();
        viewModel.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task ShowDocumentAsync_WithNoDocument_ClearsThePanel()
    {
        SetupPassages(7, PassageText);
        _annotations.Setup(service => service.GetAnnotationsForDocumentAsync(7))
            .ReturnsAsync([Annotation(1, "Latency", createdMinutesAgo: 3)]);
        var viewModel = CreateViewModel();
        await viewModel.ShowDocumentAsync(7);
        viewModel.CaptureSelection("Latency", positionHint: 0);

        await viewModel.ShowDocumentAsync(null);

        viewModel.HasPassage.Should().BeFalse();
        viewModel.PassageText.Should().BeEmpty();
        viewModel.HasDraft.Should().BeFalse();
        viewModel.Annotations.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenLoadingFails_ThePanelSaysSo()
    {
        _annotations.Setup(service => service.GetPassageAsync(7, 0, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));
        var viewModel = CreateViewModel();

        await viewModel.ShowDocumentAsync(7);

        viewModel.IsLoading.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("The text and annotations of this document could not be loaded.");
        viewModel.ShowNoTextHint.Should().BeFalse("the text may exist; it only could not be read");
    }

    [Fact]
    public async Task WithoutTheAnnotationService_ThePanelIsUnavailable()
    {
        var viewModel = new DocumentNotesViewModel(annotations: null);

        await viewModel.ShowDocumentAsync(7);
        viewModel.CaptureSelection("anything", positionHint: 0);

        viewModel.IsAvailable.Should().BeFalse();
        viewModel.HasPassage.Should().BeFalse();
        viewModel.HasDraft.Should().BeFalse();
    }

    private DocumentNotesViewModel CreateViewModel() => new(_annotations.Object);

    /// <summary>
    /// Serves <paramref name="texts"/> as the passages of <paramref name="documentId"/>, with
    /// chunk ids 700, 701, ... (or 100x for other documents) in order.
    /// </summary>
    private void SetupPassages(long documentId, params string[] texts)
    {
        var firstChunkId = documentId == 7 ? 700 : documentId * 1000;
        _annotations.Setup(service => service.GetPassageAsync(documentId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, int position, CancellationToken _) =>
            {
                var clamped = Math.Clamp(position, 0, texts.Length - 1);
                return new AnnotationPassage(firstChunkId + clamped, clamped, texts.Length, null, texts[clamped]);
            });
    }

    private void SetupCreateReturnsId(long id) =>
        _annotations
            .Setup(service => service.CreateAnnotationAsync(
                It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((long documentId, long? _, int _, int _, string text, string color, string? note) =>
                new AnnotationEntity
                {
                    Id = id,
                    DocumentId = documentId,
                    HighlightedText = text,
                    Color = color,
                    NoteText = note,
                    CreatedAt = DateTime.UtcNow,
                });

    private static AnnotationEntity Annotation(long id, string text, int createdMinutesAgo, string? note = null) => new()
    {
        Id = id,
        DocumentId = 7,
        HighlightedText = text,
        NoteText = note,
        Color = "yellow",
        CreatedAt = DateTime.UtcNow.AddMinutes(-createdMinutesAgo),
    };
}
