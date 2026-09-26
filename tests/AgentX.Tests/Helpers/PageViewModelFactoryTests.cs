using AgentX.App.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentX.Tests.Helpers;

/// <summary>
/// SH-DI: page view models are transient IDisposables. Resolved from the root provider,
/// each one is recorded by the container until shutdown, so every page the Frame evicts
/// and rebuilds leaves its old view model (and, through event subscriptions, the old
/// page) reachable for the rest of the session. The container's tracking is observable
/// deterministically: it disposes what it tracked when it is disposed itself.
/// </summary>
public sealed class PageViewModelFactoryTests
{
    [Fact]
    public void RootProvider_TracksTransientDisposables_WhichIsTheLeak()
    {
        var provider = BuildProvider();
        var viewModel = provider.GetRequiredService<DisposableViewModel>();

        provider.Dispose();

        viewModel.IsDisposed.Should().BeTrue(
            "the root provider holds every transient IDisposable it creates until it is disposed, " +
            "which for the app's root provider means until shutdown");
    }

    [Fact]
    public void Create_ReturnsAnInstanceTheContainerDoesNotTrack()
    {
        var provider = BuildProvider();
        var viewModel = PageViewModelFactory.Create<DisposableViewModel>(provider);

        provider.Dispose();

        viewModel.IsDisposed.Should().BeFalse(
            "an untracked view model is referenced only by its page, so it is collected with it");
    }

    [Fact]
    public void Create_BuildsTheSameObjectTheContainerWould()
    {
        var provider = BuildProvider();

        var viewModel = PageViewModelFactory.Create<DisposableViewModel>(provider);

        viewModel.Dependency.Should().BeSameAs(provider.GetRequiredService<SingletonDependency>());
        viewModel.RegisteredOptional.Should().BeSameAs(provider.GetRequiredService<RegisteredOptionalService>());
        viewModel.UnregisteredOptional.Should().BeNull("an unregistered optional parameter keeps its default");
    }

    [Fact]
    public void Create_RequiresAProvider()
    {
        var act = () => PageViewModelFactory.Create<DisposableViewModel>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SingletonDependency>();
        services.AddSingleton<RegisteredOptionalService>();
        services.AddTransient<DisposableViewModel>();
        return services.BuildServiceProvider();
    }

    private sealed class SingletonDependency
    {
    }

    private sealed class RegisteredOptionalService
    {
    }

    private sealed class UnregisteredOptionalService
    {
    }

    /// <summary>Shaped like the app's page view models: one public constructor, optional tail.</summary>
    private sealed class DisposableViewModel : IDisposable
    {
        public DisposableViewModel(
            SingletonDependency dependency,
            RegisteredOptionalService? registeredOptional = null,
            UnregisteredOptionalService? unregisteredOptional = null)
        {
            Dependency = dependency;
            RegisteredOptional = registeredOptional;
            UnregisteredOptional = unregisteredOptional;
        }

        public SingletonDependency Dependency { get; }
        public RegisteredOptionalService? RegisteredOptional { get; }
        public UnregisteredOptionalService? UnregisteredOptional { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
