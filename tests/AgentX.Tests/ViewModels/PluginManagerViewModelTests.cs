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

    [Fact]
    public async Task InitializeAsync_loads_plugins_and_sets_status_message()
    {
        _pluginService.Setup(service => service.GetInstalledPluginsAsync())
            .ReturnsAsync(
            [
                CreatePlugin(1, "Calendar Connector", enabled: true),
                CreatePlugin(2, "Inbox Helper", enabled: false),
            ]);

        var viewModel = new PluginManagerViewModel(_pluginService.Object);

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

        var viewModel = new PluginManagerViewModel(_pluginService.Object);
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
