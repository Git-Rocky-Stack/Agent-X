using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Plugins;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class PluginManagerViewModelTests
{
    private readonly Mock<IPluginService> _pluginService = new();
    private readonly Mock<IOperationsDrillInService> _operationsDrillInService = new();

    [Fact]
    public async Task InitializeAsync_loads_plugins_and_sets_status_message()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(1, "Calendar Connector", enabled: true),
                CreatePlugin(2, "Inbox Helper", enabled: false),
            ]);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.PluginCount.Should().Be(2);
        viewModel.Plugins.Should().HaveCount(2);
        viewModel.StatusMessage.Should().Be("2 plugins installed");
        viewModel.Plugins[0].Name.Should().Be("Calendar Connector");
        viewModel.Plugins[1].StatusLabel.Should().Be("DISABLED");
    }

    [Fact]
    public async Task BulkEnableAsync_enables_selected_plugins_refreshes_and_clears_selection()
    {
        _pluginService.SetupSequence(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: false),
            ])
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: true),
                CreatePlugin(12, "Email Connector", enabled: true),
            ]);

        _pluginService.Setup(service => service.EnablePluginAsync(It.IsAny<long>()))
            .Returns(Task.CompletedTask);

        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.ToggleMultiSelectCommand.Execute(null);
        viewModel.TogglePluginSelectionCommand.Execute(11L);
        viewModel.TogglePluginSelectionCommand.Execute(12L);

        await viewModel.BulkEnableCommand.ExecuteAsync(null);

        _pluginService.Verify(service => service.EnablePluginAsync(11L), Times.Once);
        _pluginService.Verify(service => service.EnablePluginAsync(12L), Times.Once);

        viewModel.SelectedCount.Should().Be(0);
        viewModel.SelectedPluginIds.Should().BeEmpty();
        viewModel.Plugins.Should().OnlyContain(plugin => plugin.IsEnabled);
        viewModel.StatusMessage.Should().Be("Successfully enabled 2 plugins");
        viewModel.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_consumes_pending_operations_plugin_request_and_focuses_plugin()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: true),
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingPluginRequest())
            .Returns(new OperationsPluginDrillInRequest(12, "Opened connector \"Email Connector\" from Operations"));

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.FocusedPluginId.Should().Be(12);
        viewModel.FocusedPluginSourceLabel.Should().Contain("Email Connector");
        viewModel.StatusMessage.Should().Contain("Email Connector");
        viewModel.Plugins[0].Id.Should().Be(12);
        viewModel.Plugins[0].IsFocused.Should().BeTrue();
        viewModel.Plugins[1].IsFocused.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshPluginsCommand_preserves_focused_plugin_until_dismissed()
    {
        _pluginService.SetupSequence(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: true),
            ])
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: true),
            ]);
        _operationsDrillInService.SetupSequence(service => service.ConsumePendingPluginRequest())
            .Returns(new OperationsPluginDrillInRequest(12, "Opened connector \"Email Connector\" from Operations"))
            .Returns((OperationsPluginDrillInRequest?)null);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();
        await viewModel.RefreshPluginsCommand.ExecuteAsync(null);

        viewModel.FocusedPluginId.Should().Be(12);
        viewModel.FocusedPluginSourceLabel.Should().Contain("Email Connector");
        viewModel.StatusMessage.Should().Contain("Email Connector");
        viewModel.Plugins[0].Id.Should().Be(12);
        viewModel.Plugins[0].IsFocused.Should().BeTrue();
        viewModel.Plugins[1].IsFocused.Should().BeFalse();
    }

    [Fact]
    public async Task DismissFocusedPluginLandingCommand_clears_focus_and_restores_default_status()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: true),
            ]);
        _operationsDrillInService.Setup(service => service.ConsumePendingPluginRequest())
            .Returns(new OperationsPluginDrillInRequest(12, "Opened connector \"Email Connector\" from Operations"));

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();
        viewModel.DismissFocusedPluginLandingCommand.Execute(null);

        viewModel.FocusedPluginId.Should().Be(0);
        viewModel.FocusedPluginSourceLabel.Should().BeEmpty();
        viewModel.StatusMessage.Should().Be("2 plugins installed");
        viewModel.Plugins.Should().OnlyContain(plugin => !plugin.IsFocused);
    }

    [Fact]
    public async Task EnablePluginCommand_resolves_focused_connector_after_successful_enable()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: false),
            ]);
        _pluginService.Setup(service => service.EnablePluginAsync(12))
            .Returns(Task.CompletedTask);
        _operationsDrillInService.Setup(service => service.ConsumePendingPluginRequest())
            .Returns(new OperationsPluginDrillInRequest(12, "Opened connector \"Email Connector\" from Operations"));

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();
        await viewModel.EnablePluginCommand.ExecuteAsync(12L);

        _pluginService.Verify(service => service.EnablePluginAsync(12), Times.Once);
        viewModel.FocusedPluginId.Should().Be(0);
        viewModel.FocusedPluginSourceLabel.Should().BeEmpty();
        viewModel.StatusMessage.Should().Be("Resolved \"Email Connector\" by enabling it.");
        viewModel.Plugins.Should().OnlyContain(plugin => !plugin.IsFocused);
        viewModel.Plugins.Single(plugin => plugin.Id == 12).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task BulkEnableAsync_resolves_focused_connector_when_selection_includes_it()
    {
        _pluginService.SetupSequence(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: false),
            ])
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: true),
                CreatePlugin(12, "Email Connector", enabled: true),
            ]);
        _pluginService.Setup(service => service.EnablePluginAsync(It.IsAny<long>()))
            .Returns(Task.CompletedTask);
        _operationsDrillInService.SetupSequence(service => service.ConsumePendingPluginRequest())
            .Returns(new OperationsPluginDrillInRequest(12, "Opened connector \"Email Connector\" from Operations"))
            .Returns((OperationsPluginDrillInRequest?)null);

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();
        viewModel.ToggleMultiSelectCommand.Execute(null);
        viewModel.TogglePluginSelectionCommand.Execute(11L);
        viewModel.TogglePluginSelectionCommand.Execute(12L);

        await viewModel.BulkEnableCommand.ExecuteAsync(null);

        _pluginService.Verify(service => service.EnablePluginAsync(11L), Times.Once);
        _pluginService.Verify(service => service.EnablePluginAsync(12L), Times.Once);
        viewModel.FocusedPluginId.Should().Be(0);
        viewModel.FocusedPluginSourceLabel.Should().BeEmpty();
        viewModel.StatusMessage.Should().Be("Resolved \"Email Connector\" by enabling it.");
        viewModel.SelectedPluginIds.Should().BeEmpty();
        viewModel.SelectedCount.Should().Be(0);
        viewModel.Plugins.Should().OnlyContain(plugin => !plugin.IsFocused && plugin.IsEnabled);
    }

    // -- Selection state ------------------------------------------------------
    // The bulk commands tracked selection in an id list only, which no per-row control
    // can bind to. The flag has to live on the item for a checkbox to reflect it.

    [Fact]
    public async Task TogglePluginSelectionCommand_MarksTheItemSelected()
    {
        SetupTwoPlugins();
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.TogglePluginSelectionCommand.Execute(11L);

        viewModel.Plugins.Single(plugin => plugin.Id == 11).IsSelected.Should().BeTrue();
        viewModel.Plugins.Single(plugin => plugin.Id == 12).IsSelected.Should().BeFalse();

        viewModel.TogglePluginSelectionCommand.Execute(11L);

        viewModel.Plugins.Single(plugin => plugin.Id == 11).IsSelected.Should().BeFalse();
    }

    [Fact]
    public async Task SelectAllPluginsCommand_MarksEveryItemSelected()
    {
        SetupTwoPlugins();
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.SelectAllPluginsCommand.Execute(null);

        viewModel.Plugins.Should().OnlyContain(plugin => plugin.IsSelected);
        viewModel.SelectedCount.Should().Be(2);
    }

    [Fact]
    public async Task ToggleMultiSelectCommand_WhenSwitchedOff_ClearsEveryItemSelection()
    {
        SetupTwoPlugins();
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.ToggleMultiSelectCommand.Execute(null);
        viewModel.SelectAllPluginsCommand.Execute(null);

        viewModel.ToggleMultiSelectCommand.Execute(null);

        viewModel.IsMultiSelectMode.Should().BeFalse();
        viewModel.Plugins.Should().OnlyContain(plugin => !plugin.IsSelected);
        viewModel.SelectedCount.Should().Be(0);
    }

    // -- Uninstall asks first --
    // Uninstalling one plugin deleted its folder on a single click, while uninstalling several
    // asked first. Both now ask through the same page-supplied confirmation.

    [Fact]
    public async Task UninstallPluginCommand_AsksWithThePluginsName_AndUninstallsOnlyOnConfirm()
    {
        SetupTwoPlugins();
        _pluginService.Setup(service => service.UninstallPluginAsync(11))
            .ReturnsAsync(new PluginUninstallResult(Found: true, LeftoverDirectory: null));
        var viewModel = CreateViewModel(KeyEchoingLocalization().Object);
        await viewModel.InitializeAsync();
        var requests = new List<ConfirmationRequest>();
        viewModel.ConfirmDestructiveActionAsync = request =>
        {
            requests.Add(request);
            return Task.FromResult(true);
        };

        await viewModel.UninstallPluginCommand.ExecuteAsync(11L);

        requests.Should().ContainSingle().Which.Should().Be(new ConfirmationRequest(
            "[Plugin_UninstallConfirmTitle]",
            "[Plugin_UninstallConfirmMessage] Calendar Connector",
            "[Plugin_UninstallConfirmButton]",
            "[Plugin_ConfirmCancelButton]"));
        _pluginService.Verify(service => service.UninstallPluginAsync(11), Times.Once);
        viewModel.Plugins.Select(plugin => plugin.Id).Should().Equal(12L);
        viewModel.PluginCount.Should().Be(1);
    }

    [Fact]
    public async Task UninstallPluginCommand_WhenCancelled_RemovesNothing()
    {
        SetupTwoPlugins();
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.ConfirmDestructiveActionAsync = _ => Task.FromResult(false);

        await viewModel.UninstallPluginCommand.ExecuteAsync(11L);

        _pluginService.Verify(service => service.UninstallPluginAsync(It.IsAny<long>()), Times.Never);
        viewModel.Plugins.Should().HaveCount(2);
        viewModel.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task UninstallPluginCommand_WithoutAConfirmationHandler_RemovesNothing()
    {
        SetupTwoPlugins();
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.ConfirmDestructiveActionAsync = null;

        await viewModel.UninstallPluginCommand.ExecuteAsync(11L);

        _pluginService.Verify(service => service.UninstallPluginAsync(It.IsAny<long>()), Times.Never);
        viewModel.Plugins.Should().HaveCount(2);
    }

    [Fact]
    public async Task BulkUninstallCommand_AsksWithTheCount_AndKeepsTheSelectionWhenCancelled()
    {
        SetupTwoPlugins();
        var viewModel = CreateViewModel(KeyEchoingLocalization().Object);
        await viewModel.InitializeAsync();
        viewModel.ToggleMultiSelectCommand.Execute(null);
        viewModel.SelectAllPluginsCommand.Execute(null);
        ConfirmationRequest? asked = null;
        viewModel.ConfirmDestructiveActionAsync = request =>
        {
            asked = request;
            return Task.FromResult(false);
        };

        await viewModel.BulkUninstallCommand.ExecuteAsync(null);

        asked.Should().NotBeNull();
        asked!.Title.Should().Be("[Plugin_BulkUninstallConfirmTitle]");
        asked.Message.Should().Be("[Plugin_BulkUninstallConfirmMessage] 2");
        asked.ConfirmText.Should().Be("[Plugin_UninstallConfirmButton]");
        _pluginService.Verify(service => service.UninstallPluginAsync(It.IsAny<long>()), Times.Never);
        viewModel.SelectedCount.Should().Be(2, "a cancelled uninstall keeps the selection to adjust");
        viewModel.IsMultiSelectMode.Should().BeTrue();
    }

    [Fact]
    public async Task BulkUninstallCommand_WhenConfirmed_UninstallsEverySelectedPlugin()
    {
        SetupTwoPlugins();
        _pluginService.Setup(service => service.UninstallPluginAsync(It.IsAny<long>()))
            .ReturnsAsync(new PluginUninstallResult(Found: true, LeftoverDirectory: null));
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.ToggleMultiSelectCommand.Execute(null);
        viewModel.SelectAllPluginsCommand.Execute(null);

        await viewModel.BulkUninstallCommand.ExecuteAsync(null);

        _pluginService.Verify(service => service.UninstallPluginAsync(11), Times.Once);
        _pluginService.Verify(service => service.UninstallPluginAsync(12), Times.Once);
        viewModel.SelectedCount.Should().Be(0);
        viewModel.IsMultiSelectMode.Should().BeFalse();
    }

    // -- Texts in the user's language --
    // The status line, errors, "Never" and the type fallback were English literals.

    [Fact]
    public async Task InitializeAsync_ShowsTheStatusAndNeverActivatedInTheUsersLanguage()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: false),
                CreatePlugin(12, "Email Connector", enabled: false),
            ]);
        var viewModel = CreateViewModel(ReswLocalization.For("de"));

        await viewModel.InitializeAsync();

        viewModel.StatusMessage.Should().Be("2 Plugins installiert");
        viewModel.Plugins.Should().OnlyContain(plugin => plugin.LastActivatedAtFormatted == "Nie");
    }

    [Fact]
    public async Task InitializeAsync_WithOnePlugin_SaysPluginInTheSingular()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync([CreatePlugin(11, "Calendar Connector", enabled: true)]);
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync();

        viewModel.StatusMessage.Should().Be("1 plugin installed");
    }

    [Fact]
    public async Task InitializeAsync_NamesAPluginWithoutATypeAnExtension()
    {
        var untyped = CreatePlugin(11, "Calendar Connector", enabled: true);
        untyped.PluginType = string.Empty;
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync([untyped, CreatePlugin(12, "Email Connector", enabled: true)]);
        var viewModel = CreateViewModel(ReswLocalization.For("fr"));

        await viewModel.InitializeAsync();

        viewModel.Plugins.Single(plugin => plugin.Id == 11).TypeLabel.Should().Be("Extension");
        viewModel.Plugins.Single(plugin => plugin.Id == 12).TypeLabel.Should().Be("Connector");
    }

    [Fact]
    public async Task InstallPluginCommand_WhenInstallFails_ExplainsInTheUsersLanguage()
    {
        SetupTwoPlugins();
        _pluginService.Setup(service => service.InstallPluginAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("manifest.json is missing"));
        var viewModel = CreateViewModel(ReswLocalization.For("es"));
        viewModel.FilePickerRequested += () => Task.FromResult<string?>(@"C:\Downloads\broken.zip");
        await viewModel.InitializeAsync();

        await viewModel.InstallPluginCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Be("Error en la instalación: manifest.json is missing");
        viewModel.StatusMessage.Should().Be("Error en la instalación");
    }

    // -- Activation time --
    // Enabling a plugin stamped the row with the local time, which TimeAgo reads as UTC: west
    // of UTC a plugin enabled a moment ago read "5h ago".

    [Fact]
    public async Task EnablePluginCommand_StampsTheActivationInUtc()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync([CreatePlugin(11, "Calendar Connector", enabled: false)]);
        _pluginService.Setup(service => service.EnablePluginAsync(11)).Returns(Task.CompletedTask);
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        await viewModel.EnablePluginCommand.ExecuteAsync(11L);

        var plugin = viewModel.Plugins.Single();
        plugin.LastActivatedAt.Should().NotBeNull();
        plugin.LastActivatedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        plugin.LastActivatedAt.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        plugin.LastActivatedAtFormatted.Should().Be(FormatHelper.TimeAgoWithMonths(plugin.LastActivatedAt.Value));
        viewModel.StatusMessage.Should().Be("Enabled Calendar Connector");
    }

    // -- A change that fails --
    // The detail switch reported success before the command finished and set the status lamp
    // from the switch position, so a failed enable read "Active". The page now waits for the
    // command and shows the plugin's state, which a failure leaves as it was.

    [Fact]
    public async Task EnablePluginCommand_WhenTheServiceFails_LeavesThePluginDisabled()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync([CreatePlugin(11, "Calendar Connector", enabled: false)]);
        _pluginService.Setup(service => service.EnablePluginAsync(11))
            .ThrowsAsync(new InvalidOperationException("entry assembly missing"));
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        await viewModel.EnablePluginCommand.ExecuteAsync(11L);

        var plugin = viewModel.Plugins.Single();
        plugin.IsEnabled.Should().BeFalse();
        plugin.LastActivatedAt.Should().BeNull();
        viewModel.ErrorMessage.Should().Contain("entry assembly missing");
    }

    [Fact]
    public async Task DisablePluginCommand_WhenTheServiceFails_LeavesThePluginEnabled()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync([CreatePlugin(11, "Calendar Connector", enabled: true)]);
        _pluginService.Setup(service => service.DisablePluginAsync(11))
            .ThrowsAsync(new InvalidOperationException("still in use"));
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        await viewModel.DisablePluginCommand.ExecuteAsync(11L);

        viewModel.Plugins.Single().IsEnabled.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("still in use");
    }

    private void SetupTwoPlugins() =>
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(11, "Calendar Connector", enabled: true),
                CreatePlugin(12, "Email Connector", enabled: true),
            ]);

    /// <summary>
    /// A view model whose page confirms every uninstall, as a user who clicks Uninstall would.
    /// Tests of the confirmation itself replace the handler. Without a localization service it
    /// reads the en-US resources the app ships.
    /// </summary>
    private PluginManagerViewModel CreateViewModel(ILocalizationService? localization = null) =>
        new(_pluginService.Object, localization ?? EnglishResources.Create(), _operationsDrillInService.Object)
        {
            ConfirmDestructiveActionAsync = _ => Task.FromResult(true)
        };

    /// <summary>Returns "[key]" for a text and "[key] arg1, arg2" for a formatted one.</summary>
    private static Mock<ILocalizationService> KeyEchoingLocalization()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => $"[{key}]");
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"[{key}] {string.Join(", ", args)}");
        return localization;
    }

    private static PluginEntity CreatePlugin(long id, string name, bool enabled)
    {
        return new PluginEntity
        {
            Id = id,
            PluginId = $"com.agentx.{name.Replace(" ", string.Empty).ToLowerInvariant()}",
            Name = name,
            Version = "1.0.0",
            Author = "AgentX",
            Description = $"{name} description",
            PluginType = "connector",
            InstallPath = $@"C:\Plugins\{id}",
            IsEnabled = enabled,
            InstalledAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc)
        };
    }
}
