using AgentX.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Helpers;

/// <summary>
/// Tests for <see cref="PathHelper"/>'s path-containment guards, which form the security
/// boundary for extracting untrusted archive entries and loading plugin assemblies.
/// </summary>
public sealed class PathHelperContainmentTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "agentx-contain-base");

    [Theory]
    [InlineData("file.txt")]
    [InlineData("sub/file.txt")]
    [InlineData("a/b/c/deep.bin")]
    public void ResolveContainedPath_AllowsNestedEntries(string relative)
    {
        var resolved = PathHelper.ResolveContainedPath(Base, relative);

        resolved.Should().StartWith(Path.GetFullPath(Base));
        PathHelper.IsPathContained(Base, resolved).Should().BeTrue();
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("a/b/../../../escape.txt")]
    public void ResolveContainedPath_RejectsTraversal(string relative)
    {
        var act = () => PathHelper.ResolveContainedPath(Base, relative);

        act.Should().Throw<UnauthorizedAccessException>();
        PathHelper.TryResolveContainedPath(Base, relative, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Windows\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData(@"\\server\share\evil.txt")]
    public void ResolveContainedPath_RejectsRootedPaths(string relative)
    {
        var act = () => PathHelper.ResolveContainedPath(Base, relative);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("file.txt", true)]
    [InlineData("sub/file.txt", true)]
    [InlineData("../evil.txt", false)]
    [InlineData("sub/../../evil.txt", false)]
    [InlineData(@"C:\evil.txt", false)]
    [InlineData("/evil.txt", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSafeRelativeEntry_ClassifiesEntries(string relative, bool expectedSafe)
    {
        PathHelper.IsSafeRelativeEntry(relative).Should().Be(expectedSafe);
    }

    [Theory]
    [InlineData("MyPlugin.dll", true)]
    [InlineData("My.Complex.Plugin.dll", true)]
    [InlineData("plain-name", true)]
    [InlineData(@"..\evil.dll", false)]
    [InlineData("../evil.dll", false)]
    [InlineData("sub/evil.dll", false)]
    [InlineData(@"C:\evil.dll", false)]
    [InlineData("/evil.dll", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    public void IsBareFileName_ClassifiesNames(string name, bool expected)
        => PathHelper.IsBareFileName(name).Should().Be(expected);

    [Fact]
    public void IsPathContained_TrueForInside_FalseForOutside()
    {
        var inside = Path.Combine(Path.GetFullPath(Base), "sub", "file.txt");
        var outside = Path.Combine(Path.GetFullPath(Path.Combine(Base, "..")), "sibling", "file.txt");

        PathHelper.IsPathContained(Base, inside).Should().BeTrue();
        PathHelper.IsPathContained(Base, outside).Should().BeFalse();
    }
}
