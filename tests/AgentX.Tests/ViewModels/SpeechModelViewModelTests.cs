using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Documents;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The Whisper model that audio import and voice input need could not be installed from anywhere
/// in the app: the service could download it, but nothing called it, so every audio import was
/// indexed as a placeholder and voice input sent users to a Settings page that does not exist.
/// The Model Manager's Speech-to-Text Model section shows whether the model is installed and its
/// size, downloads it (only when asked) with progress and Cancel, removes it, and after a download
/// queues the audio that was waiting for it and says how many files that was.
/// </summary>
public sealed class SpeechModelViewModelTests
{
    private const long ModelBytes = 148_000_000; // shown as 141.1 MB

    private readonly Mock<ITranscriptionService> _transcription = new();
    private readonly Mock<IDocumentService> _documents = new();
    private readonly Mock<ILocalizationService> _localization = new();
    private readonly Mock<INotificationService> _notifications = new();

    /// <summary>The size <see cref="ITranscriptionService.GetInstalledModelSizeAsync"/> reports.</summary>
    private long? _installedSize;

    public SpeechModelViewModelTests()
    {
        _transcription.Setup(t => t.GetInstalledModelSizeAsync(It.IsAny<string>()))
            .ReturnsAsync(() => _installedSize);

        // Resource lookups come back as their keys, followed by their arguments.
        _localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        _localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}: {string.Join(" ", args)}");
    }

    private SpeechModelViewModel CreateSut() =>
        new(_transcription.Object, _documents.Object, _localization.Object, _notifications.Object);

    // Status
    [Fact]
    public async Task LoadAsync_InstalledModel_ShowsItsSizeAndOffersRemove()
    {
        _installedSize = ModelBytes;
        var sut = CreateSut();

        await sut.LoadAsync();

        sut.IsInstalled.Should().BeTrue();
        sut.StatusText.Should().Be("ModelMgr_SttInstalled: 141.1 MB");
        sut.CanRemove.Should().BeTrue();
        sut.CanDownload.Should().BeFalse();
        sut.DownloadCommand.CanExecute(null).Should().BeFalse();
        sut.RemoveCommand.CanExecute(null).Should().BeTrue();
        _transcription.Verify(t => t.GetInstalledModelSizeAsync(SpeechModelViewModel.ModelSize), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_MissingModel_OffersDownload_AndNeverDownloadsOnItsOwn()
    {
        var sut = CreateSut();

        await sut.LoadAsync();

        sut.IsInstalled.Should().BeFalse();
        sut.StatusText.Should().Be("ModelMgr_SttNotInstalled");
        sut.CanDownload.Should().BeTrue();
        sut.CanRemove.Should().BeFalse();
        sut.DownloadCommand.CanExecute(null).Should().BeTrue();
        _transcription.Verify(
            t => t.DownloadModelAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_WhenTheCheckFails_SaysWhy()
    {
        _transcription.Setup(t => t.GetInstalledModelSizeAsync(It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedAccessException("access denied"));
        var sut = CreateSut();

        await sut.LoadAsync();

        sut.IsInstalled.Should().BeFalse();
        sut.StatusText.Should().Be("ModelMgr_SttStatusFailed: access denied");
    }

    // Download
    [Fact]
    public async Task Download_Success_InstallsQueuesTheWaitingAudioAndSaysHowMany()
    {
        SetupDownloadThatInstalls();
        _documents.Setup(d => d.RequeueAudioAwaitingSpeechModelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.DownloadCommand.ExecuteAsync(null);

        _transcription.Verify(t => t.DownloadModelAsync(
            SpeechModelViewModel.ModelSize, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(d => d.RequeueAudioAwaitingSpeechModelAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.IsDownloading.Should().BeFalse();
        sut.IsInstalled.Should().BeTrue();
        sut.StatusText.Should().Be("ModelMgr_SttInstalled: 141.1 MB");
        sut.ResultMessage.Should().Be("ModelMgr_SttQueued: 3");
        sut.ErrorMessage.Should().BeEmpty();
        sut.CanRemove.Should().BeTrue();
        _notifications.Verify(n => n.ShowSuccess("ModelMgr_SttReadyTitle", "ModelMgr_SttQueued: 3", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Download_Success_WithNothingWaiting_SaysTheModelIsReady()
    {
        SetupDownloadThatInstalls();
        _documents.Setup(d => d.RequeueAudioAwaitingSpeechModelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.DownloadCommand.ExecuteAsync(null);

        sut.ResultMessage.Should().Be("ModelMgr_SttReady");
        _notifications.Verify(n => n.ShowSuccess("ModelMgr_SttReadyTitle", "ModelMgr_SttReady", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Download_Failure_SaysWhy_InstallsNothing_AndQueuesNothing()
    {
        _transcription
            .Setup(t => t.DownloadModelAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 503"));
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.DownloadCommand.ExecuteAsync(null);

        sut.IsDownloading.Should().BeFalse();
        sut.IsInstalled.Should().BeFalse();
        sut.CanDownload.Should().BeTrue();
        sut.ResultMessage.Should().BeEmpty();
        sut.ErrorMessage.Should().Be(
            "ModelMgr_SttDownloadFailed: Response status code does not indicate success: 503");
        sut.StatusText.Should().Be("ModelMgr_SttNotInstalled");
        _documents.Verify(d => d.RequeueAudioAwaitingSpeechModelAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.ShowError("ModelMgr_SttDownloadFailedTitle", sut.ErrorMessage, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_StopsTheDownload_AndQueuesNothing()
    {
        CancellationToken downloadToken = default;
        _transcription
            .Setup(t => t.DownloadModelAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<double>? _, CancellationToken ct) =>
            {
                downloadToken = ct;
                await Task.Delay(Timeout.Infinite, ct);
            });
        var sut = CreateSut();
        await sut.LoadAsync();

        var download = sut.DownloadCommand.ExecuteAsync(null);

        sut.IsDownloading.Should().BeTrue();
        sut.CanDownload.Should().BeFalse();
        sut.CanRemove.Should().BeFalse();
        sut.CancelDownloadCommand.CanExecute(null).Should().BeTrue();

        sut.CancelDownloadCommand.Execute(null);
        await download.WaitAsync(TimeSpan.FromSeconds(10));

        downloadToken.IsCancellationRequested.Should().BeTrue();
        sut.IsDownloading.Should().BeFalse();
        sut.IsInstalled.Should().BeFalse();
        sut.ResultMessage.Should().Be("ModelMgr_SttDownloadCancelled");
        sut.ErrorMessage.Should().BeEmpty();
        sut.CancelDownloadCommand.CanExecute(null).Should().BeFalse();
        _documents.Verify(d => d.RequeueAudioAwaitingSpeechModelAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Download_ReportsProgressAsAPercentage()
    {
        var release = new TaskCompletionSource();
        _transcription
            .Setup(t => t.DownloadModelAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, IProgress<double>? progress, CancellationToken _) =>
            {
                progress!.Report(0.5);
                await release.Task;
            });
        var sut = CreateSut();
        await sut.LoadAsync();

        var download = sut.DownloadCommand.ExecuteAsync(null);

        // Progress<T> posts its callback to the current context, so wait without blocking it.
        (await WaitUntilAsync(() => sut.StatusText == "ModelMgr_SttDownloading: 50"))
            .Should().BeTrue("the status line follows the download");
        sut.DownloadProgress.Should().Be(50);

        release.SetResult();
        await download.WaitAsync(TimeSpan.FromSeconds(10));
        sut.IsDownloading.Should().BeFalse();
    }

    [Fact]
    public async Task Download_WhenQueueingTheWaitingAudioFails_KeepsTheModelAndSaysWhy()
    {
        SetupDownloadThatInstalls();
        _documents.Setup(d => d.RequeueAudioAwaitingSpeechModelAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.DownloadCommand.ExecuteAsync(null);

        sut.IsInstalled.Should().BeTrue();
        sut.ResultMessage.Should().BeEmpty();
        sut.ErrorMessage.Should().Be("ModelMgr_SttRequeueFailed: database is locked");
        _notifications.Verify(n => n.ShowError("ModelMgr_SttReadyTitle", sut.ErrorMessage, It.IsAny<int>()), Times.Once);
    }

    // Remove
    [Fact]
    public async Task Remove_DeletesTheModel_AndOffersDownloadAgain()
    {
        _installedSize = ModelBytes;
        _transcription.Setup(t => t.RemoveModelAsync(It.IsAny<string>()))
            .Callback(() => _installedSize = null)
            .Returns(Task.CompletedTask);
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.RemoveCommand.ExecuteAsync(null);

        _transcription.Verify(t => t.RemoveModelAsync(SpeechModelViewModel.ModelSize), Times.Once);
        sut.IsInstalled.Should().BeFalse();
        sut.StatusText.Should().Be("ModelMgr_SttNotInstalled");
        sut.ResultMessage.Should().Be("ModelMgr_SttRemoved");
        sut.CanDownload.Should().BeTrue();
        sut.CanRemove.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_Failure_SaysWhy_AndTheModelStaysInstalled()
    {
        _installedSize = ModelBytes;
        _transcription.Setup(t => t.RemoveModelAsync(It.IsAny<string>()))
            .ThrowsAsync(new IOException("The file is in use."));
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.RemoveCommand.ExecuteAsync(null);

        sut.IsInstalled.Should().BeTrue();
        sut.ResultMessage.Should().BeEmpty();
        sut.ErrorMessage.Should().Be("ModelMgr_SttRemoveFailed: The file is in use.");
        sut.IsRemoving.Should().BeFalse();
    }

    // Model Manager page
    [Fact]
    public async Task ModelManager_Initialize_FillsInTheSpeechToTextSection_EvenWithoutAnAiProvider()
    {
        _installedSize = ModelBytes;
        var modelManager = new Mock<IModelManager>();
        modelManager.Setup(m => m.GetInstalledModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());
        var aiService = new Mock<IAiService>();
        aiService.SetupGet(a => a.ActiveProvider).Throws(new InvalidOperationException("not initialized"));
        var page = new ModelManagerViewModel(
            modelManager.Object, aiService.Object, _transcription.Object, _documents.Object, _localization.Object);

        await page.InitializeAsync();

        page.SpeechModel.IsInstalled.Should().BeTrue();
        page.SpeechModel.StatusText.Should().Be("ModelMgr_SttInstalled: 141.1 MB");
    }

    // Helpers
    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(10);
        }

        return true;
    }

    /// <summary>A download that completes and leaves the model on disk.</summary>
    private void SetupDownloadThatInstalls()
    {
        _transcription
            .Setup(t => t.DownloadModelAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _installedSize = ModelBytes)
            .Returns(Task.CompletedTask);
    }
}
