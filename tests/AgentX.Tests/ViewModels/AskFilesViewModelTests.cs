using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Collections;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class AskFilesViewModelTests
{
    private readonly Mock<IRagPipeline> _ragPipeline = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<ICollectionService> _collectionService = new();
    private readonly Mock<Serilog.ILogger> _logger = new();

    public AskFilesViewModelTests()
    {
        _logger.Setup(log => log.ForContext<It.IsAnyType>()).Returns(_logger.Object);
    }

    // ── Citation file paths ──────────────────────────────────────────────────
    // A citation that arrives without a file path is backfilled from the document store.
    // That lookup used to block on the async call with Task.Wait()/Task.Result inside the
    // answer pipeline, which can deadlock on the UI thread; it must stay asynchronous.

    [Fact]
    public async Task AskQuestionAsync_BackfillsCitationPathsThatTheAnswerDidNotCarry()
    {
        _ragPipeline
            .Setup(pipeline => pipeline.AskAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagResponse
            {
                AnswerText = "Revenue grew 12% quarter over quarter.",
                Citations =
                [
                    new Citation
                    {
                        Number = 1,
                        DocumentId = 77,
                        FileName = "Q3.pdf",
                        FilePath = string.Empty,
                        Excerpt = "Revenue grew 12%.",
                    }
                ],
            });

        _documentService
            .Setup(service => service.GetDocumentAsync(77))
            .ReturnsAsync(new DocumentEntity
            {
                Id = 77,
                FileName = "Q3.pdf",
                FilePath = @"C:\Vault\Q3.pdf",
            });

        var viewModel = CreateViewModel();
        viewModel.QuestionText = "How did revenue move?";

        await viewModel.AskCommand.ExecuteAsync(null);

        viewModel.ActiveCitations.Should().ContainSingle();
        viewModel.ActiveCitations[0].FilePath.Should().Be(@"C:\Vault\Q3.pdf");
    }

    [Fact]
    public async Task AskQuestionAsync_KeepsAPathTheAnswerAlreadyCarried()
    {
        _ragPipeline
            .Setup(pipeline => pipeline.AskAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagResponse
            {
                AnswerText = "Answer",
                Citations =
                [
                    new Citation
                    {
                        Number = 1,
                        DocumentId = 77,
                        FileName = "Q3.pdf",
                        FilePath = @"C:\Vault\Direct.pdf",
                        Excerpt = "Excerpt",
                    }
                ],
            });

        var viewModel = CreateViewModel();
        viewModel.QuestionText = "How did revenue move?";

        await viewModel.AskCommand.ExecuteAsync(null);

        viewModel.ActiveCitations[0].FilePath.Should().Be(@"C:\Vault\Direct.pdf");
        _documentService.Verify(
            service => service.GetDocumentAsync(It.IsAny<long>()),
            Times.Never);
    }

    private AskFilesViewModel CreateViewModel() =>
        new(
            _ragPipeline.Object,
            _documentService.Object,
            _collectionService.Object,
            _logger.Object);
}
