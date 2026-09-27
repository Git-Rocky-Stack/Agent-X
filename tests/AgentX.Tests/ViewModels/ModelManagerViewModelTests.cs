using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Documents;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Localization;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The Model Manager's connection line, download status and error banner were English whatever
/// the UI language. They are read from the resources, and the connection line is worded like the
/// status strip's.
/// </summary>
public sealed class ModelManagerViewModelTests
{
    private readonly Mock<IModelManager> _modelManager = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IAiProvider> _provider = new();
    private readonly Mock<ITranscriptionService> _transcription = new();
    private readonly Mock<IDocumentService> _documents = new();

    public ModelManagerViewModelTests()
    {
        _provider.SetupGet(p => p.DisplayName).Returns("Ollama");
        _aiService.SetupGet(s => s.ActiveProvider).Returns(_provider.Object);
        _aiService.SetupGet(s => s.ActiveModelId).Returns("llama3.2");
        _modelManager.Setup(m => m.GetInstalledModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AiModel>());
    }

    private ModelManagerViewModel CreateSut(ILocalizationService? localization = null) =>
        new(_modelManager.Object, _aiService.Object, _transcription.Object, _documents.Object,
            localization ?? EnglishResources.Create());

    [Fact]
    public void A_new_page_says_it_is_checking_the_connection()
    {
        CreateSut().ConnectionStatus.Should().Be("Checking...");
    }

    [Theory]
    [InlineData(true, "Connected to Ollama")]
    [InlineData(false, "Ollama not available")]
    public async Task InitializeAsync_names_the_active_provider(bool reachable, string expected)
    {
        _provider.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(reachable);
        var sut = CreateSut();

        await sut.InitializeAsync();

        sut.ConnectionStatus.Should().Be(expected);
        sut.IsConnected.Should().Be(reachable);
    }

    [Fact]
    public async Task A_failed_download_says_why_and_what_to_check()
    {
        _modelManager.Setup(m => m.PullModelAsync("llama3", It.IsAny<IProgress<ModelDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("manifest not found"));
        var sut = CreateSut();
        sut.DownloadModelName = "llama3";

        await sut.PullModelCommand.ExecuteAsync(null);

        sut.DownloadStatus.Should().Be("Download failed: manifest not found");
        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().Be(
            "Failed to download llama3. Ensure Ollama is available and the model name is correct.");
    }

    [Fact]
    public async Task Messages_come_from_the_resources()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}({string.Join(",", args)})");
        _modelManager.Setup(m => m.DeleteModelAsync("llama3", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("in use"));
        var sut = CreateSut(localization.Object);

        sut.ConnectionStatus.Should().Be("ModelMgr_CheckingConnection");

        await sut.DeleteModelCommand.ExecuteAsync("llama3");

        sut.ErrorMessage.Should().Be("ModelMgr_DeleteFailed(in use)");
    }
}
