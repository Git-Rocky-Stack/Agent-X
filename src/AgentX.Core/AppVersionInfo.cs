using System.Reflection;

namespace AgentX.Core;

/// <summary>
/// Single source of truth for the user-facing application version (AX-QA-014).
///
/// Reads the running assembly's version — ultimately driven by the one
/// <c>&lt;Version&gt;</c> in <c>Directory.Build.props</c> — so the dashboard footer,
/// the Settings page, the backup manifest, and any other surface never drift from
/// the shipped build. Prefer this over hardcoding a version string anywhere.
/// </summary>
public static class AppVersionInfo
{
    /// <summary>
    /// Product version for display, e.g. <c>"2.1.2"</c> — without assembly-info build
    /// metadata (the <c>+bedrock</c>/commit suffix) or a trailing <c>.0</c> revision.
    /// </summary>
    public static string Display { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(AppVersionInfo).Assembly;

        // Preferred: InformationalVersion (e.g. "2.1.2+bedrock"). Take everything before
        // the first '+' so any build/commit metadata is dropped.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataStart = informational.IndexOf('+');
            return metadataStart >= 0 ? informational[..metadataStart] : informational;
        }

        // Fallback: AssemblyVersion ("2.1.2.0") -> "2.1.2".
        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
