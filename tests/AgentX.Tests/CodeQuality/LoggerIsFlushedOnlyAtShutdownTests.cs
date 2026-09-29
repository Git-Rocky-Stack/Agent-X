using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the "one failure disables logging for the session" defect (SH15).
/// <para>
/// <c>Log.CloseAndFlush()</c> swaps the static logger for a silent one and disposes the
/// real one, which DI also captured at startup (<c>services.AddSingleton&lt;ILogger&gt;(_ =&gt;
/// Log.Logger)</c>). MainWindow used to call it from the navigation-failure handler and
/// the startup-navigation catch, then keep running, so a single failed page load left
/// every later log call writing nowhere. The only legitimate callers are the process
/// ending paths in App.xaml.cs (host shutdown and the AppDomain unhandled-exception hook).
/// </para>
/// </summary>
public sealed class LoggerIsFlushedOnlyAtShutdownTests
{
    private static readonly string[] AllowedFiles = { "App.xaml.cs" };

    [Fact]
    public void CloseAndFlush_IsCalledOnlyFromTheAppShutdownPaths()
    {
        var appRoot = Path.Combine(ResolveSourceRoot(), "AgentX.App");

        var offenders = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !AllowedFiles.Contains(Path.GetFileName(path)))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, number: index + 1)))
            .Where(entry =>
                !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                entry.line.Contains("CloseAndFlush(", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(appRoot, entry.path)}:{entry.number}")
            .ToList();

        offenders.Should().BeEmpty(
            "Log.CloseAndFlush() disposes the process-wide logger; calling it on a path that " +
            "keeps the app running silences all logging for the rest of the session. Log the " +
            "failure and let App.xaml.cs flush once at shutdown. Offenders:\n  " +
            string.Join("\n  ", offenders));
    }

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
