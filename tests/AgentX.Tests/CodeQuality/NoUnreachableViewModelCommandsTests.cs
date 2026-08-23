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
/// A command counts as reachable when either the generated <c>XxxCommand</c> property is
/// referenced outside its declaring file (a XAML binding or a code-behind invocation) or
/// the underlying method is called outside its declaring file (a coordinator or a
/// code-behind handler driving it directly). Commands invoked only from inside their own
/// view model are not reachable: that is an internal helper wearing a command attribute.
/// </para>
/// </summary>
public sealed class NoUnreachableViewModelCommandsTests
{
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
            foreach (Match declaration in RelayCommandDeclaration.Matches(viewModelText))
            {
                var method = declaration.Groups["method"].Value;
                var command = ToCommandName(method);

                if (IsReferencedOutside(sources, viewModelPath, $@"\b{Regex.Escape(command)}\b") ||
                    IsReferencedOutside(sources, viewModelPath, $@"\b{Regex.Escape(method)}\s*\(") ||
                    IsDrivenByABoundProperty(sources, viewModelText, command))
                {
                    continue;
                }

                unreachable.Add($"{Path.GetFileNameWithoutExtension(viewModelPath)}.{command}");
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
        IReadOnlyDictionary<string, string> sources,
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
            var boundInXaml = sources
                .Where(source => source.Key.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Any(source => Regex.IsMatch(source.Value, $@"ViewModel\.{Regex.Escape(property)}\b"));

            if (boundInXaml)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReferencedOutside(
        IReadOnlyDictionary<string, string> sources,
        string declaringPath,
        string pattern)
    {
        foreach (var (path, text) in sources)
        {
            if (string.Equals(path, declaringPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(text, pattern))
            {
                return true;
            }
        }

        return false;
    }

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
