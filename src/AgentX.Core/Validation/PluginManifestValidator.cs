using System.Text.RegularExpressions;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Plugins;

namespace AgentX.Core.Validation;

/// <summary>
/// Validates a <see cref="PluginManifest"/> instance against all known constraints
/// that must hold before a plugin archive is installed or activated.
/// </summary>
/// <remarks>
/// <para>
/// This validator enforces the following constraints:
/// </para>
/// <list type="bullet">
///   <item><see cref="PluginManifest.Id"/> must not be empty and must follow a
///         reverse-DNS pattern (i.e., contain at least one dot).</item>
///   <item><see cref="PluginManifest.Name"/> must not be empty and must not exceed
///         100 characters.</item>
///   <item><see cref="PluginManifest.Version"/> must not be empty and must be parseable
///         as a semantic version (major.minor.patch, with optional pre-release suffix).</item>
///   <item><see cref="PluginManifest.EntryAssembly"/> must not be empty and must end
///         with <c>.dll</c> (case-insensitive).</item>
/// </list>
/// </remarks>
public sealed partial class PluginManifestValidator : IValidator<PluginManifest>
{
    /// <summary>
    /// Maximum allowed length for the <see cref="PluginManifest.Name"/> field.
    /// </summary>
    private const int MaxNameLength = 100;

    /// <summary>
    /// Regex pattern for validating semantic version strings.
    /// Accepts versions like <c>1.0.0</c>, <c>2.1.3-beta</c>, <c>0.9.1-rc.1+build.42</c>.
    /// </summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*)?(\+[a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*)?$")]
    private static partial Regex SemVerRegex();

    /// <summary>
    /// Strict reverse-DNS plugin ID pattern: two or more dot-separated alphanumeric segments
    /// (hyphens allowed internally). Because the ID becomes a single on-disk directory name, this
    /// pattern intentionally rejects path separators, <c>..</c>, empty segments, leading/trailing
    /// dots, and control characters — closing the install-path injection surface.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+$")]
    private static partial Regex PluginIdRegex();

    /// <summary>
    /// Windows reserved device names. Rejected as the first ID segment because a directory whose
    /// base name (before the first dot) matches one of these is reserved by the OS.
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <inheritdoc />
    public ValidationResult Validate(PluginManifest instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var errors = new List<ValidationError>();

        // ── Id ───────────────────────────────────────────────────────────
        // The ID is used verbatim as the plugin's install directory name, so it must be a
        // strict reverse-DNS identifier with no path-injection potential.
        if (string.IsNullOrWhiteSpace(instance.Id))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Id),
                "Plugin ID must not be empty."));
        }
        else if (!PluginIdRegex().IsMatch(instance.Id))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Id),
                $"Plugin ID must be a valid reverse-DNS identifier — two or more dot-separated alphanumeric segments (e.g., 'com.vendor.myplugin'), with no path separators or '..'. Got '{instance.Id}'."));
        }
        else if (ReservedDeviceNames.Contains(instance.Id.Split('.', 2)[0]))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Id),
                $"Plugin ID must not begin with a reserved device name. Got '{instance.Id}'."));
        }

        // ── Name ─────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Name),
                "Plugin name must not be empty."));
        }
        else if (instance.Name.Length > MaxNameLength)
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Name),
                $"Plugin name must not exceed {MaxNameLength} characters. Got {instance.Name.Length} characters."));
        }

        // ── Version ──────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.Version))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Version),
                "Plugin version must not be empty."));
        }
        else if (!SemVerRegex().IsMatch(instance.Version))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Version),
                $"Plugin version must be a valid semantic version (e.g., '1.0.0'). Got '{instance.Version}'."));
        }

        // ── EntryAssembly ────────────────────────────────────────────────
        // Must be a bare file name (no directory component, not rooted) so activation cannot be
        // redirected to load an assembly outside the plugin's install directory.
        if (string.IsNullOrWhiteSpace(instance.EntryAssembly))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.EntryAssembly),
                "Entry assembly must not be empty."));
        }
        else if (!PathHelper.IsBareFileName(instance.EntryAssembly))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.EntryAssembly),
                $"Entry assembly must be a bare file name with no path separators, '..', or drive/rooted path. Got '{instance.EntryAssembly}'."));
        }
        else if (!instance.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.EntryAssembly),
                $"Entry assembly must end with '.dll'. Got '{instance.EntryAssembly}'."));
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
