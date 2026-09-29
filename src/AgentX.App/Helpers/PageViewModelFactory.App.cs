namespace AgentX.App.Helpers;

public static partial class PageViewModelFactory
{
    /// <summary>
    /// Creates <typeparamref name="T"/> from the application's service provider, untracked.
    /// Pages call this instead of App.GetService for their view model. Kept out of the
    /// file AgentX.Tests links, because it reaches the real App host.
    /// </summary>
    public static T Create<T>() where T : class => Create<T>(App.Host.Services);
}
