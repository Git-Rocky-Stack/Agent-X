using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards SH-DI. A view model that implements IDisposable and is resolved through
/// App.GetService (the root provider) is recorded by the container until shutdown, so
/// every time the Frame evicts and rebuilds its page the previous instance leaks, along
/// with anything it references. Pages create such view models with
/// PageViewModelFactory.Create, which builds the same object untracked.
/// </summary>
public sealed class PagesCreateDisposableViewModelsUntrackedTests
{
    /// <summary>
    /// Pages allowed to resolve their disposable view model through App.GetService for now.
    /// Every page has been switched to PageViewModelFactory.Create, so the set is empty; park a
    /// page here only while its switch is in progress. An entry is skipped, not required.
    /// </summary>
    private static readonly HashSet<string> PendingPages = new(StringComparer.Ordinal);

    private static readonly Regex DisposableViewModelDeclaration = new(
        @"\bclass\s+(?<name>\w+ViewModel)\b[^{;]*\bIDisposable\b",
        RegexOptions.Compiled);

    private static readonly Regex RootProviderResolution = new(
        @"App\.GetService<(?<name>\w+ViewModel)>\(\)|GetService\(\s*typeof\(\s*(?<name>\w+ViewModel)\s*\)\s*\)|GetRequiredService<(?<name>\w+ViewModel)>\(\)",
        RegexOptions.Compiled);

    [Fact]
    public void DisposableViewModels_AreNotResolvedFromTheRootProvider()
    {
        var appRoot = Path.Combine(ResolveSourceRoot(), "AgentX.App");
        var sources = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        var disposableViewModels = sources
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}ViewModels{Path.DirectorySeparatorChar}"))
            .SelectMany(File.ReadAllLines)
            .Select(line => DisposableViewModelDeclaration.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        disposableViewModels.Should().NotBeEmpty(
            "the scan must find the IDisposable view models, otherwise this guard silently passes");

        var offenders = sources
            .Where(path => !PendingPages.Contains(Path.GetFileName(path)))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, number: index + 1)))
            .Where(entry => !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(entry => RootProviderResolution.Matches(entry.line)
                .Select(match => (entry.path, entry.number, name: match.Groups["name"].Value)))
            .Where(hit => disposableViewModels.Contains(hit.name))
            .Select(hit => $"{Path.GetRelativePath(appRoot, hit.path)}:{hit.number} -> {hit.name}")
            .ToList();

        offenders.Should().BeEmpty(
            "a transient IDisposable resolved from the root provider is kept until shutdown, so each " +
            "page rebuild leaks the previous view model. Use PageViewModelFactory.Create<T>() instead. " +
            "Offenders:\n  " + string.Join("\n  ", offenders));
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

        throw new DirectoryNotFoundException("Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
