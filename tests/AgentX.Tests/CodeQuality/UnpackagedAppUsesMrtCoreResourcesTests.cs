using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards SH21. AgentX.App ships unpackaged (WindowsPackageType=None), and the UWP
/// resource and globalization APIs find an app's resources and language override through
/// package identity. The code-side string loader used the UWP ResourceLoader, so it never
/// reached resources.pri and the command palette rendered raw keys while x:Uid markup
/// (resolved by WinUI through MRT Core) was localized. Code in the app must use the
/// Windows App SDK MRT Core types (Microsoft.Windows.ApplicationModel.Resources and
/// Microsoft.Windows.Globalization) instead.
/// </summary>
public sealed class UnpackagedAppUsesMrtCoreResourcesTests
{
    // "Windows.ApplicationModel.Resources" or "Windows.Globalization.ApplicationLanguages"
    // not preceded by "Microsoft." (the MRT Core namespaces share the suffix), or a
    // "using Windows.Globalization;" directive, which is how the old adapter reached the
    // UWP ApplicationLanguages by its bare name.
    private static readonly Regex UwpResourceApi = new(
        @"(?<!Microsoft\.)\bWindows\.(ApplicationModel\.Resources|Globalization\.ApplicationLanguages)\b" +
        @"|^\s*using\s+Windows\.Globalization\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void TheAppIsUnpackaged_SoThisGuardApplies()
    {
        var csproj = XDocument.Load(Path.Combine(AppRoot(), "AgentX.App.csproj"));
        var packageType = csproj.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "WindowsPackageType")?.Value;

        packageType.Should().Be("None",
            "this guard exists because the app has no package identity; if that changes, revisit it");
    }

    [Fact]
    public void AppCode_UsesMrtCoreForResourcesAndLanguage_NotTheUwpApis()
    {
        var appRoot = AppRoot();
        var offenders = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, number: index + 1)))
            .Where(entry =>
                !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                !entry.line.TrimStart().StartsWith("///", StringComparison.Ordinal) &&
                UwpResourceApi.IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(appRoot, entry.path)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "UWP resource APIs cannot see resources.pri in an unpackaged process, so every lookup " +
            "misses and the UI shows raw keys. Use Microsoft.Windows.ApplicationModel.Resources " +
            "and Microsoft.Windows.Globalization. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    private static string AppRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AgentX.App");
            if (File.Exists(Path.Combine(candidate, "AgentX.App.csproj")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/AgentX.App from " + AppContext.BaseDirectory);
    }
}
