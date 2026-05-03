using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

public sealed class NoNotImplementedStubsTests
{
    [Fact]
    public void SourceFiles_DoNotShipNotImplementedExceptionStubs()
    {
        var sourceRoot = ResolveSourceRoot();
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(path => new
            {
                Path = Path.GetRelativePath(sourceRoot, path),
                Text = File.ReadAllText(path),
            })
            .Where(file => file.Text.Contains("NotImplementedException", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToList();

        offenders.Should().BeEmpty();
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

        throw new DirectoryNotFoundException("Could not locate Agent-X source root from test output directory.");
    }
}
