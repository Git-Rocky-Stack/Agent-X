namespace AgentX.Core.Validation;

/// <summary>
/// Defines a contract for validating instances of type <typeparamref name="T"/>.
/// Implementations encapsulate all business rules and constraints for a given type
/// and return a <see cref="ValidationResult"/> summarising any violations.
/// </summary>
/// <typeparam name="T">The type of object to validate.</typeparam>
public interface IValidator<in T>
{
    /// <summary>
    /// Validates the supplied <paramref name="instance"/> against all known constraints.
    /// </summary>
    /// <param name="instance">The object to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> whose <see cref="ValidationResult.IsValid"/> property
    /// is <see langword="true"/> when all checks pass, or <see langword="false"/> when one or
    /// more <see cref="ValidationError"/> entries have been recorded.
    /// </returns>
    ValidationResult Validate(T instance);
}
