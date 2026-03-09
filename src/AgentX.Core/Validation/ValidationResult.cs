namespace AgentX.Core.Validation;

/// <summary>
/// Represents the outcome of a validation operation.
/// Contains a collection of <see cref="ValidationError"/> instances describing any
/// constraint violations found during validation. An empty error list indicates
/// the validated instance is considered valid.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validated instance passed all checks.
    /// Returns <see langword="true"/> when no errors were recorded.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the read-only list of validation errors discovered during the check.
    /// An empty list indicates a successful validation.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    private ValidationResult(IReadOnlyList<ValidationError> errors) => Errors = errors;

    /// <summary>
    /// Creates a <see cref="ValidationResult"/> representing a successful validation
    /// with no errors.
    /// </summary>
    /// <returns>A valid <see cref="ValidationResult"/> with an empty error list.</returns>
    public static ValidationResult Success() => new(Array.Empty<ValidationError>());

    /// <summary>
    /// Creates a <see cref="ValidationResult"/> representing a failed validation
    /// containing the supplied errors.
    /// </summary>
    /// <param name="errors">One or more validation errors to include in the result.</param>
    /// <returns>An invalid <see cref="ValidationResult"/> populated with <paramref name="errors"/>.</returns>
    public static ValidationResult Failure(IEnumerable<ValidationError> errors) => new(errors.ToList());

    /// <summary>
    /// Convenience overload that creates a <see cref="ValidationResult"/> representing
    /// a single-field failure.
    /// </summary>
    /// <param name="field">The name of the field that failed validation.</param>
    /// <param name="message">A human-readable message describing the validation failure.</param>
    /// <returns>An invalid <see cref="ValidationResult"/> with a single error entry.</returns>
    public static ValidationResult Failure(string field, string message) =>
        new(new[] { new ValidationError(field, message) });
}

/// <summary>
/// Describes a single validation failure, associating a field name with a
/// human-readable error message.
/// </summary>
/// <param name="FieldName">The name of the field or property that failed validation.</param>
/// <param name="Message">A human-readable description of the validation violation.</param>
public sealed record ValidationError(string FieldName, string Message);
