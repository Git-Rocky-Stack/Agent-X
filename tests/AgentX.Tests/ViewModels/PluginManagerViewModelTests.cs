using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Plugins;
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

    private PluginManagerViewModel CreateViewModel() =>
        new(_pluginService.Object, _operationsDrillInService.Object);

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
