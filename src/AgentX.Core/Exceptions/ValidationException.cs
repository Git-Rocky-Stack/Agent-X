namespace AgentX.Core.Exceptions;

/// <summary>
/// Thrown when one or more input validation rules fail.
/// Contains a structured list of <see cref="ValidationError"/> records
/// that identify the offending fields and their error messages.
/// </summary>
public class ValidationException : AgentXException
{
    private const string Code = "VALIDATION_FAILED";

    /// <summary>
    /// The collection of validation errors that caused this exception.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationException"/> with multiple validation errors.
    /// </summary>
    /// <param name="errors">The validation errors that describe which fields failed and why.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public ValidationException(IEnumerable<ValidationError> errors)
        : this(ToReadOnlyList(errors ?? throw new ArgumentNullException(nameof(errors))))
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationException"/> for a single field error.
    /// This is a convenience constructor for the common single-field validation case.
    /// </summary>
    /// <param name="fieldName">The name of the field that failed validation.</param>
    /// <param name="errorMessage">A description of why validation failed for this field.</param>
    public ValidationException(string fieldName, string errorMessage)
        : base(
            message: $"Validation failed for '{fieldName}': {errorMessage}",
            errorCode: Code,
            userFriendlyMessage: $"{fieldName}: {errorMessage}",
            inner: null)
    {
        Errors = new List<ValidationError> { new(fieldName, errorMessage) }.AsReadOnly();
    }

    /// <summary>
    /// Private constructor that accepts an already-materialized list to avoid double enumeration.
    /// </summary>
    private ValidationException(IReadOnlyList<ValidationError> materializedErrors)
        : base(
            message: "One or more validation errors occurred.",
            errorCode: Code,
            userFriendlyMessage: BuildUserFriendlyMessage(materializedErrors),
            inner: null)
    {
        Errors = materializedErrors;
    }

    /// <summary>
    /// Materializes the enumerable into a read-only list, validating it is non-empty.
    /// </summary>
    private static IReadOnlyList<ValidationError> ToReadOnlyList(IEnumerable<ValidationError> errors)
    {
        var list = errors.ToList().AsReadOnly();

        if (list.Count == 0)
            throw new ArgumentException("At least one validation error is required.", nameof(errors));

        return list;
    }

    /// <summary>
    /// Builds a user-friendly summary string from the list of validation errors.
    /// </summary>
    private static string BuildUserFriendlyMessage(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count == 1)
            return $"{errors[0].FieldName}: {errors[0].Message}";

        var lines = errors.Select(e => $"- {e.FieldName}: {e.Message}");
        return $"Please correct the following errors:\n{string.Join("\n", lines)}";
    }

    /// <summary>
    /// Represents a single field-level validation error.
    /// </summary>
    /// <param name="FieldName">The name of the field that failed validation.</param>
    /// <param name="Message">A description of why validation failed.</param>
    public record ValidationError(string FieldName, string Message);
}
