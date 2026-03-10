using AgentX.Core.Constants;
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Validation;

/// <summary>
/// Validates an <see cref="AppSettings"/> instance against all known business rules.
/// </summary>
/// <remarks>
/// <para>
/// This validator enforces the following constraints:
/// </para>
/// <list type="bullet">
///   <item><see cref="AppSettings.ActiveProviderId"/> must be one of
///         <c>"local"</c>, <c>"ollama"</c>, <c>"openai"</c>, or <c>"anthropic"</c>.</item>
///   <item>Numeric inference and chunking parameters must fall within their documented ranges.</item>
///   <item>Provider-specific endpoints must be valid URIs when their provider is active.</item>
///   <item>Provider-specific API keys must be non-empty when their provider is active.</item>
///   <item><see cref="AppSettings.StoragePath"/> must not be null or whitespace.</item>
/// </list>
/// </remarks>
public sealed class AppSettingsValidator : IValidator<AppSettings>
{
    /// <summary>
    /// The set of recognised AI provider identifiers.
    /// </summary>
    private static readonly HashSet<string> ValidProviderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "local",
        "ollama",
        "openai",
        "anthropic",
    };

    /// <inheritdoc />
    public ValidationResult Validate(AppSettings instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var errors = new List<ValidationError>();

        // ── ActiveProviderId ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.ActiveProviderId))
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ActiveProviderId),
                "Active provider ID must not be empty."));
        }
        else if (!ValidProviderIds.Contains(instance.ActiveProviderId))
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ActiveProviderId),
                $"Active provider ID must be one of: {string.Join(", ", ValidProviderIds)}. Got '{instance.ActiveProviderId}'."));
        }

        // ── Numeric inference parameters ─────────────────────────────────
        if (instance.Temperature < 0.0 || instance.Temperature > 2.0)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.Temperature),
                $"Temperature must be between 0.0 and 2.0. Got {instance.Temperature}."));
        }

        if (instance.MaxTokens < 1 || instance.MaxTokens > AppConstants.MaxTokensLimit)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.MaxTokens),
                $"MaxTokens must be between 1 and 128000. Got {instance.MaxTokens}."));
        }

        if (instance.ContextWindow < 512 || instance.ContextWindow > AppConstants.MaxContextWindowLimit)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ContextWindow),
                $"ContextWindow must be between 512 and 1048576. Got {instance.ContextWindow}."));
        }

        // ── Knowledge Vault chunking parameters ──────────────────────────
        if (instance.ChunkSize < 64 || instance.ChunkSize > AppConstants.MaxChunkSize)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ChunkSize),
                $"ChunkSize must be between 64 and 8192. Got {instance.ChunkSize}."));
        }

        if (instance.ChunkOverlap < 0 || instance.ChunkOverlap > instance.ChunkSize)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ChunkOverlap),
                $"ChunkOverlap must be between 0 and ChunkSize ({instance.ChunkSize}). Got {instance.ChunkOverlap}."));
        }

        if (instance.TopKResults < 1 || instance.TopKResults > 100)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.TopKResults),
                $"TopKResults must be between 1 and 100. Got {instance.TopKResults}."));
        }

        // ── Provider-specific endpoint and API key validation ────────────
        string providerId = instance.ActiveProviderId?.ToLowerInvariant() ?? string.Empty;

        if (providerId == "ollama")
        {
            ValidateUri(errors, nameof(AppSettings.OllamaEndpoint), instance.OllamaEndpoint,
                "Ollama endpoint must be a valid URI when the active provider is 'ollama'.");
        }

        if (providerId == "openai")
        {
            ValidateUri(errors, nameof(AppSettings.OpenAiEndpoint), instance.OpenAiEndpoint,
                "OpenAI endpoint must be a valid URI when the active provider is 'openai'.");

            if (string.IsNullOrWhiteSpace(instance.OpenAiApiKey))
            {
                errors.Add(new ValidationError(
                    nameof(AppSettings.OpenAiApiKey),
                    "OpenAI API key must not be empty when the active provider is 'openai'."));
            }
        }

        if (providerId == "anthropic")
        {
            ValidateUri(errors, nameof(AppSettings.AnthropicEndpoint), instance.AnthropicEndpoint,
                "Anthropic endpoint must be a valid URI when the active provider is 'anthropic'.");

            if (string.IsNullOrWhiteSpace(instance.AnthropicApiKey))
            {
                errors.Add(new ValidationError(
                    nameof(AppSettings.AnthropicApiKey),
                    "Anthropic API key must not be empty when the active provider is 'anthropic'."));
            }
        }

        // ── Storage path ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.StoragePath))
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.StoragePath),
                "Storage path must not be null or whitespace."));
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    /// <summary>
    /// Validates that the supplied <paramref name="value"/> is a well-formed absolute URI.
    /// </summary>
    private static void ValidateUri(List<ValidationError> errors, string fieldName, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(new ValidationError(fieldName, message));
        }
    }
}
