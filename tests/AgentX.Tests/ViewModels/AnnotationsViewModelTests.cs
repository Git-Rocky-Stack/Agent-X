using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
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
        var vm = new AnnotationsViewModel(annotations.Object, localization.Object)
        {
            ConfirmDestructiveActionAsync = _ => Task.FromResult(true),
        };

        await vm.DeleteAnnotationCommand.ExecuteAsync(7L);

        vm.StatusMessage.Should().Be("Annotation gelöscht");
    }

    // --- Deleting ---
    // Delete removed the annotation on the first click, where every other delete in the app
    // asks first.

    [Fact]
    public async Task DeleteAnnotationCommand_AsksFirstNamingTheDocument_AndDeletesOnYes()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations.Setup(service => service.GetColorDistributionAsync()).ReturnsAsync(new Dictionary<string, int>());
        ConfirmationRequest? asked = null;
        var vm = new AnnotationsViewModel(annotations.Object, EnglishResources.Create())
        {
            ConfirmDestructiveActionAsync = request =>
            {
                asked = request;
                return Task.FromResult(true);
            },
        };
        vm.Annotations.Add(new AnnotationDisplayItem { Id = 7, DocumentName = "Q3 plan.md" });

        await vm.DeleteAnnotationCommand.ExecuteAsync(7L);

        asked.Should().Be(new ConfirmationRequest(
            "Delete annotation?",
            "The highlight and note on \"Q3 plan.md\" will be deleted. The document itself is not changed. This cannot be undone.",
            "Delete",
            "Cancel"));
        annotations.Verify(service => service.DeleteAnnotationAsync(7), Times.Once);
        vm.Annotations.Should().BeEmpty();
        vm.StatusMessage.Should().Be("Annotation deleted");
    }

    [Fact]
    public async Task DeleteAnnotationCommand_WhenTheUserSaysNo_KeepsTheAnnotation()
    {
        var annotations = new Mock<IAnnotationService>();
        var vm = new AnnotationsViewModel(annotations.Object, EnglishResources.Create())
        {
            ConfirmDestructiveActionAsync = _ => Task.FromResult(false),
        };
        vm.Annotations.Add(new AnnotationDisplayItem { Id = 7, DocumentName = "Q3 plan.md" });

        await vm.DeleteAnnotationCommand.ExecuteAsync(7L);

        annotations.Verify(service => service.DeleteAnnotationAsync(It.IsAny<long>()), Times.Never);
        vm.Annotations.Should().ContainSingle();
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAnnotationCommand_WithoutAnAnswer_DeletesNothing()
    {
        // No dialog attached, or one that cannot open, is not a yes.
        var annotations = new Mock<IAnnotationService>();
        var unattached = new AnnotationsViewModel(annotations.Object, EnglishResources.Create());
        var failing = new AnnotationsViewModel(annotations.Object, EnglishResources.Create())
        {
            ConfirmDestructiveActionAsync = _ => throw new InvalidOperationException("No XamlRoot"),
        };

        await unattached.DeleteAnnotationCommand.ExecuteAsync(7L);
        await failing.DeleteAnnotationCommand.ExecuteAsync(7L);

        annotations.Verify(service => service.DeleteAnnotationAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAnnotationCommand_AsksInTheUsersLanguage()
    {
        ConfirmationRequest? asked = null;
        var vm = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), ReswLocalization.For("de"))
        {
            ConfirmDestructiveActionAsync = request =>
            {
                asked = request;
                return Task.FromResult(false);
            },
        };
        vm.Annotations.Add(new AnnotationDisplayItem { Id = 7, DocumentName = "Q3-Plan.md" });

        await vm.DeleteAnnotationCommand.ExecuteAsync(7L);

        asked!.Title.Should().Be("Annotation löschen?");
        asked.Message.Should().StartWith("Die Hervorhebung und die Notiz zu \"Q3-Plan.md\" werden gelöscht.");
        asked.ConfirmText.Should().Be("Löschen");
        asked.CancelText.Should().Be("Abbrechen");
    }

    // ── Editing ──────────────────────────────────────────────────────────────
    // The edit flow existed in the view model with no control anywhere in the page.
    // The colour picker for editing must not offer the "All" filter sentinel as a colour.

    [Fact]
    public void EditColorOptions_OfferRealColoursOnly()
    {
        var vm = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), EnglishResources.Create());

        vm.EditColorOptions.Select(option => option.Value).Should().Equal("yellow", "green", "blue", "red", "purple");
        vm.EditColorOptions.Select(option => option.Value).Should().NotContain("All");
    }

    // --- Colour names ---
    // The pickers and the distribution showed the stored English colour words in every
    // language. The stored words stay: existing rows and the Markdown export use them.

    [Fact]
    public void ColorOptions_AreNamedInTheUsersLanguage_AndKeepTheStoredValues()
    {
        var english = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), EnglishResources.Create());
        var german = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), ReswLocalization.For("de"));

        english.ColorOptions.Select(option => option.Label)
            .Should().Equal("All", "Yellow", "Green", "Blue", "Red", "Purple");
        german.ColorOptions.Select(option => option.Label)
            .Should().Equal("Alle", "Gelb", "Grün", "Blau", "Rot", "Lila");
        german.ColorOptions.Select(option => option.Value)
            .Should().Equal("All", "yellow", "green", "blue", "red", "purple");
        german.EditColorOptions.Select(option => option.Label)
            .Should().Equal("Gelb", "Grün", "Blau", "Rot", "Lila");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("zh-CN")]
    public void Every_language_names_each_colour_and_no_two_alike(string locale)
    {
        var vm = new AnnotationsViewModel(Mock.Of<IAnnotationService>(), ReswLocalization.For(locale));

        var labels = vm.ColorOptions.Select(option => option.Label).ToList();

        labels.Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label) && !label.StartsWith("Annot_", StringComparison.Ordinal));
        labels.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task FilterByColorCommand_FiltersByTheStoredValueBehindTheName()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations.Setup(service => service.GetAnnotationsByColorAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<AnnotationEntity>());
        annotations.Setup(service => service.GetAllAnnotationsAsync(0, 200))
            .ReturnsAsync(Array.Empty<AnnotationEntity>());
        var vm = new AnnotationsViewModel(annotations.Object, ReswLocalization.For("de"));

        await vm.FilterByColorCommand.ExecuteAsync(vm.ColorOptions.Single(o => o.Label == "Grün").Value);
        vm.SelectedColorFilter.Should().Be("green");
        annotations.Verify(service => service.GetAnnotationsByColorAsync("green"), Times.Once);

        await vm.FilterByColorCommand.ExecuteAsync(vm.ColorOptions.Single(o => o.Label == "Alle").Value);
        vm.SelectedColorFilter.Should().Be("All");
        annotations.Verify(service => service.GetAllAnnotationsAsync(0, 200), Times.Once);
    }

    [Fact]
    public async Task EditColorOption_ShowsTheNameAndSavesTheStoredColour()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations.Setup(service => service.GetColorDistributionAsync()).ReturnsAsync(new Dictionary<string, int>());
        var vm = new AnnotationsViewModel(annotations.Object, ReswLocalization.For("fr"));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.EditAnnotationCommand.Execute(new AnnotationDisplayItem { Id = 7, NoteText = "note", Color = "blue" });

        vm.EditColorOption.Should().Be(new AnnotationColorOption("blue", "Bleu"));
        changed.Should().Contain(nameof(AnnotationsViewModel.EditColorOption));

        vm.EditColorOption = vm.EditColorOptions.Single(option => option.Label == "Violet");
        vm.EditColorOption = null;
        vm.EditColor.Should().Be("purple", "clearing the picker keeps the chosen colour");

        await vm.SaveAnnotationCommand.ExecuteAsync(null);
        annotations.Verify(service => service.UpdateAnnotationAsync(7, "note", "purple"), Times.Once);
    }

    [Fact]
    public async Task ColorStats_NameEachColourInTheUsersLanguage()
    {
        var annotations = new Mock<IAnnotationService>();
        annotations.Setup(service => service.GetAllAnnotationsAsync(0, 200))
            .ReturnsAsync(Array.Empty<AnnotationEntity>());
        annotations.Setup(service => service.GetColorDistributionAsync())
            .ReturnsAsync(new Dictionary<string, int> { ["yellow"] = 3, ["purple"] = 1 });
        var vm = new AnnotationsViewModel(annotations.Object, ReswLocalization.For("ja"));

        await vm.InitializeAsync();

        vm.ColorStats.Select(stat => (stat.Color, stat.ColorLabel, stat.Count))
            .Should().Equal(("yellow", "黄色", 3), ("purple", "紫", 1));
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
