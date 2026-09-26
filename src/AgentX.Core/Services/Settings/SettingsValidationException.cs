using AgentX.Core.Validation;

namespace AgentX.Core.Services.Settings;

/// <summary>
/// Thrown by <see cref="ISettingsService.SaveSettingsAsync"/> when the settings contain clearly
/// invalid values (for example a chunk overlap larger than the chunk size). Nothing is written,
/// and the in-memory settings fall back to the last saved state.
/// </summary>
public sealed class SettingsValidationException : ArgumentException
{
    public SettingsValidationException(IReadOnlyList<ValidationError> errors)
        : base("Settings were not saved because some values are invalid: "
               + string.Join(" ", (errors ?? throw new ArgumentNullException(nameof(errors))).Select(e => e.Message)))
    {
        Errors = errors;
    }

    /// <summary>The validation errors that blocked the save.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }
}
