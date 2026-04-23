namespace AgentX.App;

public static class App
{
    public static object MainWindow { get; } = new();

    public static T GetService<T>() where T : class
    {
        throw new NotSupportedException("App.GetService<T>() is not available in AgentX.Tests.");
    }
}
