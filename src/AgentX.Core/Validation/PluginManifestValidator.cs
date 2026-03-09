using System.Text.RegularExpressions;
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

    /// <inheritdoc />
    public ValidationResult Validate(PluginManifest instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var errors = new List<ValidationError>();

        // ── Id ───────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.Id))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Id),
                "Plugin ID must not be empty."));
        }
        else if (!instance.Id.Contains('.'))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.Id),
                $"Plugin ID must follow a reverse-DNS pattern and contain at least one dot (e.g., 'com.vendor.myplugin'). Got '{instance.Id}'."));
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
        if (string.IsNullOrWhiteSpace(instance.EntryAssembly))
        {
            errors.Add(new ValidationError(
                nameof(PluginManifest.EntryAssembly),
                "Entry assembly must not be empty."));
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
