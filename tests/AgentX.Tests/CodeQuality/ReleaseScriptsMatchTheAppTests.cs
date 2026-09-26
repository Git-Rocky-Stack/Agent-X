using System.Text.RegularExpressions;
using AgentX.Core.AI;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the release scripts against drifting from what the app and the installer actually use.
/// <para>
/// build-installers.ps1 signed <c>AgentX.exe</c> while publish produces <c>AgentX.App.exe</c> (the
/// name the Inno Setup script ships), so every signed build failed on a missing file.
/// download-model.ps1 fetched a different model build than the app downloads, under the same file
/// name, so the OFFLINE installer bundled a model the app itself never uses. Neither mistake is
/// visible to the compiler, and the scripts cannot run in CI without the signing certificate and a
/// 2 GB download, so the contract is checked structurally here.
/// </para>
/// </summary>
public sealed class ReleaseScriptsMatchTheAppTests
{
    [Fact]
    public void BuildInstallers_SignsTheExecutableTheInstallerShips()
    {
        var root = ResolveRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "AgentX-Setup.iss"));
        var script = File.ReadAllText(Path.Combine(root, "scripts", "build-installers.ps1"));

        var exeName = Regex.Match(iss, @"#define\s+MyAppExeName\s+""([^""]+)""").Groups[1].Value;

        exeName.Should().Be("AgentX.App.exe", "publish names the executable after the AgentX.App project");
        script.Should().Contain($"$appExe = Join-Path $publishDir \"{exeName}\"",
            "the script must sign the executable the installer ships");
    }

    [Fact]
    public void DownloadModel_ReadsTheModelTheAppDownloads()
    {
        var root = ResolveRepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "download-model.ps1"));
        var source = File.ReadAllText(Path.Combine(root, "src", "AgentX.Core", "AI", "BuiltInModelBootstrap.cs"));

        script.Should().Contain(@"src\AgentX.Core\AI\BuiltInModelBootstrap.cs",
            "the script reads its defaults from the app's constants");
        script.Should().NotContain("huggingface.co",
            "a URL of the script's own would let the bundled model drift from the downloaded one");

        // Same pattern as Get-BootstrapConstant in the script: if the constants are ever reshaped
        // (concatenation, interpolation), this fails before the script does.
        ReadConstant(source, "DefaultDownloadUrl").Should().Be(BuiltInModelBootstrap.DefaultDownloadUrl);
        ReadConstant(source, "DefaultModelFileName").Should().Be(BuiltInModelBootstrap.DefaultModelFileName);
    }

    [Fact]
    public void OfflineInstaller_BundlesTheModelFileTheAppLoads()
    {
        var root = ResolveRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "AgentX-Setup.iss"));

        Regex.Match(iss, @"#define\s+BuiltInModelFile\s+""([^""]+)""").Groups[1].Value
            .Should().Be(BuiltInModelBootstrap.DefaultModelFileName);
    }

    private static string ReadConstant(string source, string name)
    {
        var match = Regex.Match(source, $@"const\s+string\s+{name}\s*=\s*""([^""]+)""");
        match.Success.Should().BeTrue($"{name} must stay a single string literal the script can read");
        return match.Groups[1].Value;
    }

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "scripts")) &&
                Directory.Exists(Path.Combine(directory.FullName, "installer")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "AgentX.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
