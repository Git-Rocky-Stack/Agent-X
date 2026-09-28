using AgentX.Core.Constants;
using AgentX.Core.Services.Backup.Models;
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
///   <item>Numeric inference and chunking parameters must fall within their documented ranges;
///         the chunk overlap must be smaller than the chunk size, as ChunkingService requires.</item>
///   <item>Provider-specific endpoints must be valid URIs when their provider is active.</item>
///   <item>Provider-specific API keys must be non-empty when their provider is active.</item>
///   <item><see cref="AppSettings.StoragePath"/> must not be null or whitespace.</item>
///   <item>An enabled <see cref="AppSettings.BackupSchedule"/> must have an interval of 1 to
///         <see cref="BackupScheduleConfig.MaxIntervalHours"/> hours and a non-negative retention count.</item>
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

        // -- ActiveProviderId ---------------------------------------------
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

        // -- Numeric inference parameters ---------------------------------
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

        // -- Knowledge Vault chunking parameters --------------------------
        if (instance.ChunkSize < 64 || instance.ChunkSize > AppConstants.MaxChunkSize)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ChunkSize),
                $"ChunkSize must be between 64 and 8192. Got {instance.ChunkSize}."));
        }

        // The same rule ChunkingService enforces: an overlap as large as the chunk would leave no
        // room for new text, so the chunker rejects it and every document would fail to index.
        if (instance.ChunkOverlap < 0 || instance.ChunkOverlap >= instance.ChunkSize)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.ChunkOverlap),
                $"ChunkOverlap must be at least 0 and less than ChunkSize ({instance.ChunkSize}). Got {instance.ChunkOverlap}."));
        }

        if (instance.TopKResults < 1 || instance.TopKResults > 100)
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.TopKResults),
                $"TopKResults must be between 1 and 100. Got {instance.TopKResults}."));
        }

        // -- Provider-specific endpoint and API key validation ------------
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

        // -- Storage path -------------------------------------------------
        if (string.IsNullOrWhiteSpace(instance.StoragePath))
        {
            errors.Add(new ValidationError(
                nameof(AppSettings.StoragePath),
                "Storage path must not be null or whitespace."));
        }

        // Scheduled backups: checked only while enabled
        var schedule = instance.BackupSchedule;
        if (schedule is { Enabled: true })
        {
            if (schedule.IntervalHours < 1 || schedule.IntervalHours > BackupScheduleConfig.MaxIntervalHours)
            {
                errors.Add(new ValidationError(
                    $"{nameof(AppSettings.BackupSchedule)}.{nameof(BackupScheduleConfig.IntervalHours)}",
                    $"Backup interval must be between 1 and {BackupScheduleConfig.MaxIntervalHours} hours. Got {schedule.IntervalHours}."));
            }

            if (schedule.MaxBackupsToKeep < 0)
            {
                errors.Add(new ValidationError(
                    $"{nameof(AppSettings.BackupSchedule)}.{nameof(BackupScheduleConfig.MaxBackupsToKeep)}",
                    $"The number of scheduled backups to keep must not be negative. Got {schedule.MaxBackupsToKeep}."));
            }
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
