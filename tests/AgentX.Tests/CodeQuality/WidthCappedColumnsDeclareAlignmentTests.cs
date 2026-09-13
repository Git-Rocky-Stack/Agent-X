using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// A content column that caps its width must say where it sits.
/// <para>
/// Rocky's report, 2026-09-06: "weekly digest and model manager screens render too far
/// to the right". Both pages wrap their content in a 1200px MaxWidth grid inside a
/// ScrollViewer, exactly as Dashboard does, except Dashboard's grid declares
/// <c>HorizontalAlignment="Center"</c> and theirs did not. Sixteen such columns across
/// twelve pages had no alignment; UI Automation captures measured six of them 230 to
/// 540px right of center, one running off the window. Every column that did declare an
/// alignment was placed correctly.
/// </para>
/// <para>
/// The rule: a Grid, StackPanel or Border with a MaxWidth of 600 or more (a page-level
/// content column, not a chat bubble) declares HorizontalAlignment. Center is the
/// design intent for a content column; the guard only insists the choice is written
/// down, because a column with no declared alignment is placed by whatever its parent
/// measured last.
/// </para>
/// <para>
/// The scan covers every XAML file under AgentX.App. It used to read Views at the top
/// level only, which left Views/Dialogs, Controls and MainWindow.xaml unguarded: a
/// 900px column with no alignment could be added to a dialog and this guard would pass.
/// Styles and Themes hold no MaxWidth at all today, so including them costs nothing and
/// means no new folder arrives outside the scan.
/// </para>
/// </summary>
public sealed class WidthCappedColumnsDeclareAlignmentTests
{
    private const int PageColumnMinimumWidth = 600;

    private static readonly Regex WidthCappedElement = new(
        @"<(?<el>Grid|StackPanel|Border)\b(?<a>[^>]*?)MaxWidth=""(?<w>\d{3,4})""(?<b>[^>]*)>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void EveryWidthCappedContentColumn_DeclaresItsHorizontalAlignment()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");
        var offenders = new List<string>();
        var files = 0;
        var scanned = 0;

        foreach (var path in EnumerateXamlFiles(appRoot))
        {
            files++;
            var text = File.ReadAllText(path);
            foreach (Match match in WidthCappedElement.Matches(text))
            {
                if (int.Parse(match.Groups["w"].Value) < PageColumnMinimumWidth) continue;
                scanned++;

                var attributes = match.Groups["a"].Value + match.Groups["b"].Value;
                if (!attributes.Contains("HorizontalAlignment=\"", StringComparison.Ordinal))
                {
                    var line = text[..match.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(sourceRoot, path)}:{line} <{match.Groups["el"].Value} MaxWidth=\"{match.Groups["w"].Value}\">");
                }
            }
        }

        files.Should().BeGreaterThan(40,
            "the scan must reach the app's XAML, otherwise this guard passes forever");
        scanned.Should().BeGreaterThan(20,
            "the scan must find the page columns, otherwise this guard passes forever");

        offenders.Should().BeEmpty(
            "a width-capped content column with no declared alignment is placed by whatever " +
            "its parent measured last; Weekly Digest and Model Manager rendered 275 and 540px " +
            "right of center that way. Declare HorizontalAlignment (Center for a content " +
            "column). Offenders:\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> EnumerateXamlFiles(string root) =>
        Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

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
