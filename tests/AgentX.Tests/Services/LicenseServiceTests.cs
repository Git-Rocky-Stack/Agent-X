using AgentX.Core.Services.License;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LicenseService"/>.
/// Uses an in-memory SQLite database via <see cref="TestDbContextFactory"/>
/// for all database-dependent operations.
/// </summary>
public sealed class LicenseServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public LicenseServiceTests()
    {
        _factory = new TestDbContextFactory();
    }

    public void Dispose() => _factory.Dispose();

    private LicenseService CreateService()
    {
        var db = _factory.CreateContext();
        return new LicenseService(db);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetCurrentLicenseAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCurrentLicenseAsync_WhenNoLicenseActivated_ReturnsTrialTier()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var license = await sut.GetCurrentLicenseAsync();

        // Assert
        license.Should().NotBeNull();
        license.Tier.Should().Be(LicenseTier.Trial);
        license.IsActivated.Should().BeFalse();
        license.CustomerName.Should().BeNull();
        license.CustomerEmail.Should().BeNull();
        license.ActivatedAt.Should().BeNull();
        license.ExpiresAt.Should().BeNull();
        license.MaxDocuments.Should().Be(50, "Trial tier allows 50 documents");
    }

    [Fact]
    public async Task GetCurrentLicenseAsync_ReturnsTrialWithCorrectFeatureGates()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var license = await sut.GetCurrentLicenseAsync();

        // Assert
        license.CanUseAdvancedModels.Should().BeFalse("Trial tier does not unlock advanced models");
        license.CanUseIntelligenceFeatures.Should().BeFalse("Trial tier does not unlock intelligence");
        license.CanUseUnlimitedDocuments.Should().BeFalse("Trial tier has document limits");
        license.CanUsePrioritySupport.Should().BeFalse("Trial tier does not have priority support");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ActivateLicenseAsync — format validation
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID-KEY")]
    [InlineData("AX-X-SHORT-ABCD")]
    [InlineData("XX-S-ABCDEFGHIJKLMNOP-ABCD")]      // Wrong prefix
    [InlineData("AX-Z-ABCDEFGHIJKLMNOP-ABCD")]      // Invalid tier char
    [InlineData("AX-S-abcdefghijklmnop-ABCD")]       // Lowercase payload (not Base32)
    [InlineData("AX-S-ABCDEFGHIJKLMNOP-abcd")]       // Lowercase checksum
    [InlineData("AX-S-ABCDEFGH-ABCD")]               // Payload too short
    public async Task ActivateLicenseAsync_WithInvalidFormat_ReturnsFormatError(string licenseKey)
    {
        // Arrange
        var sut = CreateService();

        // Act
        var result = await sut.ActivateLicenseAsync(licenseKey);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be(LicenseActivationError.InvalidFormat);
        result.Message.Should().Contain("format");
    }

    [Fact]
    public async Task ActivateLicenseAsync_WithNullKey_ThrowsNullReferenceException()
    {
        // Arrange
        var sut = CreateService();

        // Act & Assert
        // NOTE: The production code accesses licenseKey.Length for logging before the
        // null guard on line 101. This means null input throws NullReferenceException
        // rather than returning an InvalidFormat result. This test documents the
        // current behavior. If the production code is fixed to guard against null
        // earlier, this test should be updated to expect a format error result.
        var act = () => sut.ActivateLicenseAsync(null!);

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ActivateLicenseAsync — checksum validation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActivateLicenseAsync_WithValidFormatButBadChecksum_ReturnsChecksumError()
    {
        // Arrange: valid format (passes regex) but checksum won't match HMAC
        // AX-S-AAAAAAAAAAAAAAAA-ZZZZ has correct format but wrong checksum
        var sut = CreateService();
        var badChecksumKey = "AX-S-AAAAAAAAAAAAAAAA-ZZZZ";

        // Act
        var result = await sut.ActivateLicenseAsync(badChecksumKey);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be(LicenseActivationError.InvalidChecksum);
        result.Message.Should().Contain("checksum");
    }

    [Fact]
    public async Task ActivateLicenseAsync_WithAnotherBadChecksum_ReturnsChecksumError()
    {
        // Arrange: another key with valid format but incorrect checksum
        var sut = CreateService();
        var key = "AX-P-BBBBBBBBBBBBBBBB-AAAA";

        // Act
        var result = await sut.ActivateLicenseAsync(key);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be(LicenseActivationError.InvalidChecksum);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DeactivateLicenseAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeactivateLicenseAsync_WhenNoLicenseActive_RevertsToTrialAndReturnsTrue()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var result = await sut.DeactivateLicenseAsync();

        // Assert
        result.Should().BeTrue();

        var license = await sut.GetCurrentLicenseAsync();
        license.Tier.Should().Be(LicenseTier.Trial);
        license.IsActivated.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateLicenseAsync_ClearsCachedLicenseAndRevertsToTrial()
    {
        // Arrange: manually insert an activated license to simulate prior activation
        var db = _factory.CreateContext();
        db.Licenses.Add(new AgentX.Core.Data.Entities.LicenseEntity
        {
            LicenseKey = "AX-S-TESTKEYTESTKEYS-ABCD",
            Tier = "starter",
            IsActivated = true,
            ActivatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = CreateService();

        // Pre-condition: verify there is a license
        var beforeDeactivation = await sut.GetCurrentLicenseAsync();
        beforeDeactivation.Tier.Should().Be(LicenseTier.Starter);
        beforeDeactivation.IsActivated.Should().BeTrue();

        // Act
        var result = await sut.DeactivateLicenseAsync();

        // Assert
        result.Should().BeTrue();

        var afterDeactivation = await sut.GetCurrentLicenseAsync();
        afterDeactivation.Tier.Should().Be(LicenseTier.Trial);
        afterDeactivation.IsActivated.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GetMachineFingerprint
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMachineFingerprint_ReturnsDeterministicValue()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var fp1 = sut.GetMachineFingerprint();
        var fp2 = sut.GetMachineFingerprint();

        // Assert
        fp1.Should().Be(fp2, "fingerprint should be deterministic across calls");
    }

    [Fact]
    public void GetMachineFingerprint_IsNotEmpty()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var fingerprint = sut.GetMachineFingerprint();

        // Assert
        fingerprint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetMachineFingerprint_IsLowercaseHex()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var fingerprint = sut.GetMachineFingerprint();

        // Assert: SHA-256 produces 64 hex characters
        fingerprint.Should().HaveLength(64, "SHA-256 hex string is 64 characters");
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$",
            "fingerprint should be lowercase hexadecimal");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  LicenseInfo feature gates (static / unit-level)
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(LicenseTier.Trial, 50)]
    [InlineData(LicenseTier.Starter, 500)]
    [InlineData(LicenseTier.Professional, int.MaxValue)]
    [InlineData(LicenseTier.Ultimate, int.MaxValue)]
    public void GetDocumentLimit_ReturnsCorrectLimitForTier(LicenseTier tier, int expected)
    {
        // Act
        var limit = LicenseInfo.GetDocumentLimit(tier);

        // Assert
        limit.Should().Be(expected);
    }
}
