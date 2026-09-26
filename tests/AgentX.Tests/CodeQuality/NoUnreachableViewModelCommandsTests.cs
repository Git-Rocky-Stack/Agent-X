using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards against view-model behaviour that no user can ever invoke.
/// <para>
/// A <c>[RelayCommand]</c> method compiles, unit-tests cleanly, and reports as covered
/// while being completely unreachable from the running application: nothing binds the
/// generated command and nothing calls the method. The feature looks finished in every
/// signal a developer normally reads, yet it does not exist for the user.
/// </para>
/// <para>
/// A command counts as reachable when a view that hosts its view model uses it: the
/// generated <c>XxxCommand</c> property is bound or invoked (<c>.XxxCommand</c>), or the
/// underlying method is called (<c>.Xxx(</c>), in a file outside the view models that names
/// the view model's type (the page, window, control or dialog holding it) or in the XAML of
/// such a code-behind. Commands invoked only from inside their own view model are not
/// reachable: that is an internal helper wearing a command attribute.
/// </para>
/// <para>
/// A mention anywhere else does not count. The earlier rule accepted any same-named
/// identifier in any file, so a coordinator method called <c>DeleteConversationAsync</c> and
/// another page's <c>ClearConversationCommand</c> made chat commands that nothing invoked
/// look reachable.
/// </para>
/// </summary>
public sealed class NoUnreachableViewModelCommandsTests
{
    /// <summary>
    /// Unreachable commands another workstream is fixing. Temporary: remove each entry with
    /// its fix. Comparison selection is in flight.
    /// </summary>
    private static readonly HashSet<string> KnownUnreachableCommands = new(StringComparer.Ordinal)
    {
        "ComparisonViewModel.ToggleDocumentSelectionCommand",
    };

    /// <summary>
    /// Locates <c>[RelayCommand]</c> methods and captures the method name, tolerating
    /// interleaved attributes and any return type.
    /// </summary>
    private static readonly Regex RelayCommandDeclaration = new(
        @"\[RelayCommand[^\]]*\]\s*(?:\[[^\]]*\]\s*)*(?:private|public|internal|protected)\s+(?:static\s+)?(?:async\s+)?[\w<>?,\[\]\. ]+?\s+(?<method>\w+)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void EveryViewModelCommand_IsReachableFromTheApplication()
    {
        var appRoot = Path.Combine(ResolveSourceRoot(), "AgentX.App");
        var sources = LoadAppSources(appRoot);

        var unreachable = new List<string>();

        foreach (var (viewModelPath, viewModelText) in sources.Where(s => IsViewModel(s.Key)))
        {
            var viewModelType = Path.GetFileNameWithoutExtension(viewModelPath);
            var hosts = FindHosts(sources, viewModelType);

            foreach (Match declaration in RelayCommandDeclaration.Matches(viewModelText))
            {
                var method = declaration.Groups["method"].Value;
                var command = ToCommandName(method);

                if (IsUsedByAHost(hosts, $@"\.{Regex.Escape(command)}\b") ||
                    IsUsedByAHost(hosts, $@"\.{Regex.Escape(method)}\s*\(") ||
                    IsDrivenByABoundProperty(hosts, viewModelText, command))
                {
                    continue;
                }

                var name = $"{viewModelType}.{command}";
                if (!KnownUnreachableCommands.Contains(name))
                {
                    unreachable.Add(name);
                }
            }
        }

        unreachable.Sort(StringComparer.Ordinal);

        unreachable.Should().BeEmpty(
            "every command must be invocable by a user; the commands below are implemented " +
            "but unreachable from any view or code path:\n  " + string.Join("\n  ", unreachable));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsViewModel(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}ViewModels{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CommunityToolkit.Mvvm strips a trailing "Async" and appends "Command" when it
    /// generates the property name for a <c>[RelayCommand]</c> method.
    /// </summary>
    private static string ToCommandName(string method)
    {
        var stem = method.EndsWith("Async", StringComparison.Ordinal)
            ? method[..^"Async".Length]
            : method;

        return stem + "Command";
    }

    /// <summary>
    /// Recognises the MVVM path where a control binds a property two-way and the
    /// generated <c>On&lt;Property&gt;Changed</c> hook runs the command. The command is
    /// only named inside its own view model there, but the user still reaches it through
    /// the binding, so it is not dead code.
    /// </summary>
    private static bool IsDrivenByABoundProperty(
        IReadOnlyDictionary<string, string> hosts,
        string viewModelText,
        string command)
    {
        foreach (Match hook in Regex.Matches(
            viewModelText,
            @"partial\s+void\s+On(?<property>\w+)Changed\s*\([^)]*\)\s*(?<body>\{(?:[^{}]|\{[^{}]*\})*\})",
            RegexOptions.Singleline))
        {
            if (!Regex.IsMatch(hook.Groups["body"].Value, $@"\b{Regex.Escape(command)}\b"))
            {
                continue;
            }

            var property = hook.Groups["property"].Value;
            var boundInXaml = hosts
                .Where(source => source.Key.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Any(source => Regex.IsMatch(source.Value, $@"ViewModel\.{Regex.Escape(property)}\b"));

            if (boundInXaml)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The files that can put a view model's commands in front of a user: every file outside
    /// the view models that names the view model's type (the page, window, control or dialog
    /// that holds it), plus the XAML of each such code-behind.
    /// </summary>
    private static Dictionary<string, string> FindHosts(
        IReadOnlyDictionary<string, string> sources,
        string viewModelType)
    {
        var namesType = new Regex($@"\b{Regex.Escape(viewModelType)}\b");
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, text) in sources)
        {
            if (IsViewModel(path) ||
                !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                !namesType.IsMatch(text))
            {
                continue;
            }

            hosts[path] = text;

            if (path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
            {
                var xamlPath = path[..^".cs".Length];
                if (sources.TryGetValue(xamlPath, out var xaml))
                {
                    hosts[xamlPath] = xaml;
                }
            }
        }

        return hosts;
    }

    private static bool IsUsedByAHost(IReadOnlyDictionary<string, string> hosts, string pattern) =>
        hosts.Values.Any(text => Regex.IsMatch(text, pattern));

    private static Dictionary<string, string> LoadAppSources(string appRoot) =>
        Directory
            .EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

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

        throw new DirectoryNotFoundException("Could not locate Agent-X source root from test output directory.");
    }
}
