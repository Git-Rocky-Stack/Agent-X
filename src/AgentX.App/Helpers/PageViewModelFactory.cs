using Microsoft.Extensions.DependencyInjection;

namespace AgentX.App.Helpers;

/// <summary>
/// Creates a page's view model without the DI container tracking it.
/// <para>
/// Page view models are registered as transient, and the root provider keeps a reference
/// to every transient IDisposable it creates until the host is disposed at shutdown.
/// Pages resolve their view model from that root provider, and the Frame caches at most
/// ten pages for twenty-nine rail entries, so each time a page is evicted and rebuilt its
/// previous view model stayed reachable from the container for the rest of the session,
/// together with its collections and, through the page's own PropertyChanged
/// subscriptions, the evicted page itself.
/// </para>
/// <para>
/// ActivatorUtilities builds the same object (same constructor, dependencies resolved
/// from the same provider, unregistered optional parameters left at their defaults) but
/// does not record it, so the view model lives exactly as long as the page that holds it.
/// Nothing disposes it, deliberately: the Dispose methods of these view models either
/// only log or cancel work (a model download, a workflow run, the auto-sync loop) that
/// should keep running when the page is merely navigated away from, and the singletons
/// that own that work are still disposed by the container at shutdown.
/// </para>
/// </summary>
public static partial class PageViewModelFactory
{
    /// <summary>
    /// Creates <typeparamref name="T"/> from <paramref name="services"/>, untracked.
    /// </summary>
    public static T Create<T>(IServiceProvider services) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        return ActivatorUtilities.CreateInstance<T>(services);
    }
}
