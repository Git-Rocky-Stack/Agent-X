using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Validation;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="SyncConfigurationValidator"/>.
/// Validates that all business rules for <see cref="SyncConfiguration"/> are enforced.
/// </summary>
public sealed class SyncConfigurationValidatorTests
{
    private readonly SyncConfigurationValidator _sut = new();

    /// <summary>
    /// Creates a valid <see cref="SyncConfiguration"/> that should pass all validation checks.
    /// </summary>
    private static SyncConfiguration CreateValidConfig() => new()
    {
        SyncFolderPath = @"C:\Users\TestUser\OneDrive\AgentXSync",
        EncryptionKey = "MySecureKey123!",
        AutoSyncEnabled = true,
        SyncIntervalMinutes = 30,
        SyncScope = SyncScope.All,
        SelectedCollectionIds = null
    };

    // ══════════════════════════════════════════════════════════════════════
    //  Valid configuration
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_WithValidConfiguration_Passes()
    {
        // Arrange
        var config = CreateValidConfig();

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithSelectedCollectionsScopeAndValidIds_Passes()
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncScope = SyncScope.SelectedCollections;
        config.SelectedCollectionIds = "1,2,3";

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithUnixStyleRootedPath_Passes()
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncFolderPath = "/home/user/sync";

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(SyncConfiguration.SyncFolderPath));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Empty SyncFolderPath
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptySyncFolderPath_Fails(string path)
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncFolderPath = path;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.SyncFolderPath));
    }

    [Fact]
    public void Validate_NullSyncFolderPath_Fails()
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncFolderPath = null!;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.SyncFolderPath));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Non-rooted SyncFolderPath
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("relative/path")]
    [InlineData("sync")]
    [InlineData("./local/sync")]
    public void Validate_NonRootedSyncFolderPath_Fails(string path)
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncFolderPath = path;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.SyncFolderPath));
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(SyncConfiguration.SyncFolderPath) &&
            e.Message.Contains("rooted", StringComparison.OrdinalIgnoreCase));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Short EncryptionKey
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("short")]
    [InlineData("1234567")]
    [InlineData("a")]
    public void Validate_ShortEncryptionKey_Fails(string key)
    {
        // Arrange
        var config = CreateValidConfig();
        config.EncryptionKey = key;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.EncryptionKey));
    }

    [Fact]
    public void Validate_ExactlyMinLengthEncryptionKey_Passes()
    {
        // Arrange: minimum is 8 characters
        var config = CreateValidConfig();
        config.EncryptionKey = "12345678";

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(SyncConfiguration.EncryptionKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyEncryptionKey_Fails(string key)
    {
        // Arrange
        var config = CreateValidConfig();
        config.EncryptionKey = key;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.EncryptionKey));
    }

    [Fact]
    public void Validate_NullEncryptionKey_Fails()
    {
        // Arrange
        var config = CreateValidConfig();
        config.EncryptionKey = null!;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.EncryptionKey));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SyncIntervalMinutes out of range
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(1441)]
    [InlineData(10000)]
    public void Validate_IntervalOutOfRange_Fails(int interval)
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncIntervalMinutes = interval;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(SyncConfiguration.SyncIntervalMinutes));
    }

    [Theory]
    [InlineData(1)]      // minimum
    [InlineData(30)]     // default
    [InlineData(720)]    // 12 hours
    [InlineData(1440)]   // maximum (24 hours)
    public void Validate_IntervalInRange_Passes(int interval)
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncIntervalMinutes = interval;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(SyncConfiguration.SyncIntervalMinutes));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SelectedCollections scope with empty collection IDs
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_SelectedCollectionsScopeWithNullIds_Fails()
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncScope = SyncScope.SelectedCollections;
        config.SelectedCollectionIds = null;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(SyncConfiguration.SelectedCollectionIds));
    }

    [Fact]
    public void Validate_SelectedCollectionsScopeWithEmptyIds_Fails()
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncScope = SyncScope.SelectedCollections;
        config.SelectedCollectionIds = "";

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(SyncConfiguration.SelectedCollectionIds));
    }

    [Fact]
    public void Validate_SelectedCollectionsScopeWithWhitespaceIds_Fails()
    {
        // Arrange
        var config = CreateValidConfig();
        config.SyncScope = SyncScope.SelectedCollections;
        config.SelectedCollectionIds = "   ";

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(SyncConfiguration.SelectedCollectionIds));
    }

    [Fact]
    public void Validate_AllScopeWithNullIds_Passes()
    {
        // Arrange: when scope is All, SelectedCollectionIds can be null
        var config = CreateValidConfig();
        config.SyncScope = SyncScope.All;
        config.SelectedCollectionIds = null;

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.Errors.Should().NotContain(e =>
            e.FieldName == nameof(SyncConfiguration.SelectedCollectionIds));
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

    // ══════════════════════════════════════════════════════════════════════
    //  Multiple errors
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_MultipleInvalidFields_ReportsAllErrors()
    {
        // Arrange
        var config = new SyncConfiguration
        {
            SyncFolderPath = "",
            EncryptionKey = "abc",      // too short
            SyncIntervalMinutes = 0,    // out of range
            SyncScope = SyncScope.SelectedCollections,
            SelectedCollectionIds = ""  // empty when scope requires IDs
        };

        // Act
        var result = _sut.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterOrEqualTo(4,
            "all four fields should report validation errors");
    }
}
