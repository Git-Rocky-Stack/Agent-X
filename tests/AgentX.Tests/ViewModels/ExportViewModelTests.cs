using AgentX.App.ViewModels;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The export dialog tells a failed export from a finished one by
/// <see cref="ExportViewModel.LastExportSucceeded"/>. It used to look for an English
/// "Export failed" prefix in the message, which a translated message does not have.
/// </summary>
public sealed class ExportViewModelTests
{
    private readonly Mock<IExportService> _exportService = new();

    [Fact]
    public async Task ExportConversation_reports_a_written_file_as_succeeded()
    {
        _exportService
            .Setup(service => service.ExportConversationAsync(7, It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExportResult.Ok(Path.Combine(Path.GetTempPath(), "chat.md"), 12));
        var viewModel = CreateViewModel();

        await viewModel.ExportConversationCommand.ExecuteAsync(new ExportConversationRequest(7));

        viewModel.LastExportSucceeded.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("Exported to chat.md");
    }

    [Fact]
    public async Task ExportConversation_reports_a_failed_export_as_failed()
    {
        _exportService
            .Setup(service => service.ExportConversationAsync(7, It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExportResult.Fail("disk full"));
        var viewModel = CreateViewModel();

        await viewModel.ExportConversationCommand.ExecuteAsync(new ExportConversationRequest(7));

        viewModel.LastExportSucceeded.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("Export failed: disk full");
    }

    [Fact]
    public async Task ExportConversation_reports_a_thrown_export_as_failed()
    {
        _exportService
            .Setup(service => service.ExportConversationAsync(7, It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("locked"));
        var viewModel = CreateViewModel();

        await viewModel.ExportConversationCommand.ExecuteAsync(new ExportConversationRequest(7));

        viewModel.LastExportSucceeded.Should().BeFalse();
        viewModel.StatusMessage.Should().Be("Export failed: locked");
    }

    [Theory]
    [InlineData(1, "Exported 1 conversation")]
    [InlineData(3, "Exported 3 conversations")]
    public async Task ExportConversations_names_the_count_with_one_and_many_wording(int count, string expected)
    {
        var ids = Enumerable.Range(1, count).Select(id => (long)id).ToList();
        _exportService
            .Setup(service => service.ExportConversationsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExportResult.Ok(Path.Combine(Path.GetTempPath(), "all.md"), 12));
        var viewModel = CreateViewModel();

        await viewModel.ExportConversationsCommand.ExecuteAsync(new ExportBatchRequest(ids));

        viewModel.LastExportSucceeded.Should().BeTrue();
        viewModel.StatusMessage.Should().Be(expected);
    }

    [Fact]
    public async Task ExportCollection_names_the_file_it_wrote()
    {
        _exportService
            .Setup(service => service.ExportCollectionAsync(4, It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExportResult.Ok(Path.Combine(Path.GetTempPath(), "research.md"), 12));
        var viewModel = CreateViewModel();

        await viewModel.ExportCollectionCommand.ExecuteAsync(new ExportCollectionRequest(4));

        viewModel.LastExportSucceeded.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("Collection exported to research.md");
    }

    private ExportViewModel CreateViewModel() => new(_exportService.Object, EnglishResources.Create());
}
