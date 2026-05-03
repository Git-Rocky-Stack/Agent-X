using AgentX.App.ViewModels;
using AgentX.Core.Services.Annotations;
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

        var vm = new AnnotationsViewModel(annotations.Object)
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

        var vm = new AnnotationsViewModel(annotations.Object)
        {
            TotalCount = 2,
            SaveMarkdownExportAsync = _ => Task.FromResult(AnnotationMarkdownExportResult.Cancelled()),
        };

        await vm.ExportAnnotationsCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("Export cancelled");
    }
}
