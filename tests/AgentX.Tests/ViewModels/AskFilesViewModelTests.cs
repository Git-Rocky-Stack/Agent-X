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

    // Streaming thread
    // The pipeline invokes onToken from a thread-pool thread (its streaming loop awaits
    // with ConfigureAwait(false)). Appending there raised PropertyChanged for a property
    // bound to the view off the UI thread, which WinUI rejects with RPC_E_WRONG_THREAD.

    [Fact]
    public async Task AskAsync_AppendsStreamedTokensOnTheThreadThatAskedTheQuestion()
    {
        _ragPipeline
            .Setup(pipeline => pipeline.AskAsync(
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, long? _, Action<string>? onToken, bool _, CancellationToken _) =>
            {
                await Task.Run(() =>
                {
                    onToken!("Revenue ");
                    onToken!("grew.");
                });
                return new RagResponse { AnswerText = "Revenue grew.", Citations = [] };
            });

        using var ui = new DedicatedThreadSynchronizationContext();
        var viewModel = CreateViewModel();
        var contentThreads = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var streamed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        viewModel.Messages.CollectionChanged += (_, e) =>
        {
            foreach (var message in e.NewItems?.OfType<AskFilesMessage>() ?? Enumerable.Empty<AskFilesMessage>())
            {
                message.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(AskFilesMessage.Content) && !message.IsUser)
                    {
                        contentThreads.Enqueue(Environment.CurrentManagedThreadId);
                        streamed.Enqueue(message.Content);
                    }
                };
            }
        };

        await ui.RunAsync(async () =>
        {
            viewModel.QuestionText = "How did revenue move?";
            await viewModel.AskCommand.ExecuteAsync(null);
        });

        contentThreads.Should().NotBeEmpty().And.OnlyContain(id => id == ui.ThreadId);
        streamed.Should().Contain("Revenue ").And.Contain("Revenue grew.");
        viewModel.Messages.Last().Content.Should().Be("Revenue grew.");
    }

    // Inline citation badges

    [Fact]
    public async Task AskAsync_AssigningCitationsNotifiesTheMessageOnScreen()
    {
        // The badges bound Citations one-time, and Citations did not notify, so citations
        // assigned after the message appeared never rendered.
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
                    new Citation { Number = 1, DocumentId = 5, FileName = "a.pdf", FilePath = "a.pdf", Excerpt = "x" }
                ],
            });
        var viewModel = CreateViewModel();
        var notified = new List<string?>();
        viewModel.Messages.CollectionChanged += (_, e) =>
        {
            foreach (var message in e.NewItems?.OfType<AskFilesMessage>() ?? Enumerable.Empty<AskFilesMessage>())
            {
                message.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
            }
        };
        viewModel.QuestionText = "Question";

        await viewModel.AskCommand.ExecuteAsync(null);

        notified.Should().Contain(nameof(AskFilesMessage.Citations));
        viewModel.Messages.Last().Citations.Should().ContainSingle().Which.FileName.Should().Be("a.pdf");
    }

    /// <summary>
    /// A single dedicated thread that runs posted callbacks in order, standing in for the
    /// WinUI dispatcher so the test can tell which thread touched a bound property.
    /// </summary>
    private sealed class DedicatedThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public DedicatedThreadSynchronizationContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            {
                IsBackground = true,
                Name = "Test UI thread"
            };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("The test context only supports Post.");

        public override SynchronizationContext CreateCopy() => this;

        /// <summary>Runs <paramref name="action"/> on the dedicated thread and waits for it.</summary>
        public Task RunAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);
            return completion.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }
    }

    private AskFilesViewModel CreateViewModel() =>
        new(
            _ragPipeline.Object,
            _documentService.Object,
            _collectionService.Object,
            _logger.Object);
}
