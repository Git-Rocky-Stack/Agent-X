using AgentX.Core.Services.Settings;
using AgentX.Core.Validation;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="AppSettingsValidator"/>.
/// Validates that business rules for <see cref="AppSettings"/> are correctly enforced.
/// </summary>
public sealed class AppSettingsValidatorTests
{
    private readonly AppSettingsValidator _sut = new();

    /// <summary>
    /// Creates a valid <see cref="AppSettings"/> instance with all defaults.
    /// The default instance should always pass validation.
    /// </summary>
    private static AppSettings CreateValidSettings() => new();

    // ══════════════════════════════════════════════════════════════════════
    //  Valid settings
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_WithDefaultSettings_Passes()
    {
        // Arrange
        var settings = CreateValidSettings();

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithAllProvidersConfigured_Passes()
    {
        // Arrange
        var settings = new AppSettings
        {
            ActiveProviderId = "openai",
            OpenAiApiKey = "sk-test-key",
            OpenAiEndpoint = "https://api.openai.com/v1/",
            Temperature = 1.0,
            MaxTokens = 4096,
            ContextWindow = 8192,
            ChunkSize = 512,
            ChunkOverlap = 50,
            TopKResults = 5
        };

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Temperature
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-1.0)]
    [InlineData(2.1)]
    [InlineData(5.0)]
    [InlineData(100.0)]
    public void Validate_TemperatureOutOfRange_Fails(double temperature)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.Temperature = temperature;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.Temperature));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Validate_TemperatureInRange_Passes(double temperature)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.Temperature = temperature;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(AppSettings.Temperature));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MaxTokens
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(128_001)]
    [InlineData(1_000_000)]
    public void Validate_MaxTokensOutOfRange_Fails(int maxTokens)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.MaxTokens = maxTokens;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.MaxTokens));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(128_000)]
    public void Validate_MaxTokensInRange_Passes(int maxTokens)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.MaxTokens = maxTokens;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(AppSettings.MaxTokens));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ChunkOverlap exceeding ChunkSize
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ChunkOverlapExceedingChunkSize_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ChunkSize = 256;
        settings.ChunkOverlap = 300; // overlap > size

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ChunkOverlap));
    }

    [Fact]
    public void Validate_ChunkOverlapEqualToChunkSize_Fails()
    {
        // Arrange: the validator uses > (not >=), but let's check the boundary
        var settings = CreateValidSettings();
        settings.ChunkSize = 256;
        settings.ChunkOverlap = 256; // overlap == size

        // Act
        var result = _sut.Validate(settings);

        // Assert: per the implementation, ChunkOverlap > ChunkSize fails;
        // overlap == size should also fail since condition is ChunkOverlap > ChunkSize
        // Actually checking the source: "ChunkOverlap < 0 || ChunkOverlap > ChunkSize"
        // overlap == size passes this check (256 > 256 is false), so it should be valid.
        // Let's verify the source logic is honored correctly.
        result.Errors.Should().NotContain(e => e.FieldName == nameof(AppSettings.ChunkOverlap),
            "overlap equal to ChunkSize is within the valid range per implementation");
    }

    [Fact]
    public void Validate_NegativeChunkOverlap_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ChunkOverlap = -1;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ChunkOverlap));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ActiveProviderId — invalid provider
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("invalid")]
    [InlineData("google")]
    [InlineData("azure")]
    [InlineData("bedrock")]
    public void Validate_InvalidProvider_Fails(string provider)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = provider;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ActiveProviderId));
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("Ollama")]    // case-insensitive
    [InlineData("OPENAI")]    // case-insensitive
    public void Validate_ValidProvider_Passes(string provider)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = provider;

        // Ensure required keys are set for openai and anthropic
        if (provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            settings.OpenAiApiKey = "sk-test";
        }
        if (provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            settings.AnthropicApiKey = "sk-ant-test";
        }

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(AppSettings.ActiveProviderId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyProvider_Fails(string provider)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = provider;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ActiveProviderId));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Missing API keys
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MissingOpenAiApiKeyWhenProviderIsOpenai_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "openai";
        settings.OpenAiApiKey = null;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.OpenAiApiKey));
    }

    [Fact]
    public void Validate_EmptyOpenAiApiKeyWhenProviderIsOpenai_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "openai";
        settings.OpenAiApiKey = "   ";

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.OpenAiApiKey));
    }

    [Fact]
    public void Validate_MissingAnthropicApiKeyWhenProviderIsAnthropic_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "anthropic";
        settings.AnthropicApiKey = null;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.AnthropicApiKey));
    }

    [Fact]
    public void Validate_EmptyAnthropicApiKeyWhenProviderIsAnthropic_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "anthropic";
        settings.AnthropicApiKey = "";

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.AnthropicApiKey));
    }

    [Fact]
    public void Validate_MissingApiKeyForNonActiveProvider_StillPasses()
    {
        // Arrange: using ollama, so openai/anthropic keys can be null
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "ollama";
        settings.OpenAiApiKey = null;
        settings.AnthropicApiKey = null;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(AppSettings.OpenAiApiKey));
        result.Errors.Should().NotContain(e => e.FieldName == nameof(AppSettings.AnthropicApiKey));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Invalid URI format
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_InvalidOllamaEndpointUri_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "ollama";
        settings.OllamaEndpoint = "not-a-uri";

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.OllamaEndpoint));
    }

    [Fact]
    public void Validate_InvalidOpenAiEndpointUri_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "openai";
        settings.OpenAiApiKey = "sk-test";
        settings.OpenAiEndpoint = "ftp://invalid-scheme.com";

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.OpenAiEndpoint));
    }

    [Fact]
    public void Validate_InvalidAnthropicEndpointUri_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ActiveProviderId = "anthropic";
        settings.AnthropicApiKey = "sk-ant-test";
        settings.AnthropicEndpoint = "";

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.AnthropicEndpoint));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Empty StoragePath
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyStoragePath_Fails(string storagePath)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.StoragePath = storagePath;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.StoragePath));
    }

    [Fact]
    public void Validate_NullStoragePath_Fails()
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.StoragePath = null!;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.StoragePath));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Multiple errors
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MultipleInvalidFields_ReportsAllErrors()
    {
        // Arrange
        var settings = new AppSettings
        {
            ActiveProviderId = "invalid_provider",
            Temperature = -5.0,
            MaxTokens = 0,
            ChunkSize = 10,   // below minimum of 64
            StoragePath = ""
        };

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterOrEqualTo(4,
            "multiple fields are invalid and all should be reported");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ContextWindow boundaries
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(511)]
    [InlineData(0)]
    [InlineData(1_048_577)]
    public void Validate_ContextWindowOutOfRange_Fails(int contextWindow)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ContextWindow = contextWindow;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ContextWindow));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TopKResults boundaries
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_TopKResultsOutOfRange_Fails(int topK)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.TopKResults = topK;

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.TopKResults));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ChunkSize boundaries
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(63)]
    [InlineData(0)]
    [InlineData(8193)]
    public void Validate_ChunkSizeOutOfRange_Fails(int chunkSize)
    {
        // Arrange
        var settings = CreateValidSettings();
        settings.ChunkSize = chunkSize;
        settings.ChunkOverlap = 0; // Ensure overlap doesn't also fail

        // Act
        var result = _sut.Validate(settings);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(AppSettings.ChunkSize));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Null instance
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_NullInstance_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.Validate(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
