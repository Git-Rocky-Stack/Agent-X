using System.Text;
using AgentX.Core.Services.Sync.Codec;
using AgentX.Core.Services.Sync.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Sync.Codec;

/// <summary>
/// Unit tests for <see cref="SyncPackageCodec"/>.
/// Verifies serialisation, encryption round-trip, and header validation.
/// </summary>
public sealed class SyncPackageCodecTests
{
    private readonly SyncPackageCodec _sut;

    public SyncPackageCodecTests()
    {
        _sut = new SyncPackageCodec(Log.Logger);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SyncPackageCodec(null!));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Serialise / Deserialise round-trip
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Serialise_Deserialise_RoundTrips()
    {
        var changeSet = CreateChangeSet();

        var bytes = _sut.Serialise(changeSet);
        var result = _sut.Deserialise(bytes);

        result.DeviceId.Should().Be(changeSet.DeviceId);
        result.ExportedAt.Should().Be(changeSet.ExportedAt);
        result.Version.Should().Be(changeSet.Version);
        result.Changes.Should().HaveCount(changeSet.Changes.Count);
        result.Changes[0].EntityType.Should().Be("DocumentEntity");
        result.Changes[0].EntityId.Should().Be(42);
    }

    [Fact]
    public void Serialise_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Serialise(null!));
    }

    [Fact]
    public void Deserialise_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Deserialise(null!));
    }

    [Fact]
    public void Deserialise_InvalidJson_Throws()
    {
        var invalidJson = Encoding.UTF8.GetBytes("not valid json at all");
        Assert.Throws<System.Text.Json.JsonException>(() => _sut.Deserialise(invalidJson));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Encrypt / Decrypt round-trip
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var plaintext = Encoding.UTF8.GetBytes("{\"test\":true,\"value\":42}");
        var passphrase = "test-encryption-key";

        var encrypted = _sut.Encrypt(plaintext, passphrase);
        var decrypted = _sut.Decrypt(encrypted, passphrase);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesHeaderWithMagic()
    {
        var plaintext = Encoding.UTF8.GetBytes("test data");
        var encrypted = _sut.Encrypt(plaintext, "passphrase");

        encrypted.Should().NotBeEmpty();
        // First 8 bytes should be the magic "AXSYNC\0\0"
        encrypted[..8].Should().Equal("AXSYNC\0\0"u8.ToArray());
    }

    [Fact]
    public void Encrypt_ProducesOutputLargerThanPlaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("short");
        var encrypted = _sut.Encrypt(plaintext, "passphrase");

        // Header is 54 bytes + ciphertext (same length as plaintext)
        encrypted.Length.Should().BeGreaterThan(plaintext.Length);
        encrypted.Length.Should().Be(54 + plaintext.Length);
    }

    [Fact]
    public void Encrypt_DifferentPassphrases_ProduceDifferentOutput()
    {
        var plaintext = Encoding.UTF8.GetBytes("same data");

        var enc1 = _sut.Encrypt(plaintext, "password1");
        var enc2 = _sut.Encrypt(plaintext, "password2");

        enc1.Should().NotEqual(enc2);
    }

    [Fact]
    public void Encrypt_SamePassphraseDifferentCalls_ProduceDifferentOutput()
    {
        // Due to fresh random salt/nonce per call
        var plaintext = Encoding.UTF8.GetBytes("same data");

        var enc1 = _sut.Encrypt(plaintext, "password");
        var enc2 = _sut.Encrypt(plaintext, "password");

        enc1.Should().NotEqual(enc2); // different salt/nonce each time
    }

    [Fact]
    public void Decrypt_WrongPassphrase_Throws()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret data");
        var encrypted = _sut.Encrypt(plaintext, "correct-password");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => _sut.Decrypt(encrypted, "wrong-password"));
    }

    [Fact]
    public void Decrypt_TooShortData_Throws()
    {
        var shortData = new byte[10];

        Assert.Throws<InvalidOperationException>(
            () => _sut.Decrypt(shortData, "password"));
    }

    [Fact]
    public void Encrypt_NullPlaintext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Encrypt(null!, "password"));
    }

    [Fact]
    public void Encrypt_EmptyPassphrase_Throws()
    {
        Assert.Throws<ArgumentException>(() => _sut.Encrypt(new byte[1], ""));
    }

    [Fact]
    public void Decrypt_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Decrypt(null!, "password"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  IsValidHeader
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsValidHeader_ValidEncryptedData_ReturnsTrue()
    {
        var plaintext = Encoding.UTF8.GetBytes("test");
        var encrypted = _sut.Encrypt(plaintext, "password");

        _sut.IsValidHeader(encrypted).Should().BeTrue();
    }

    [Fact]
    public void IsValidHeader_RandomData_ReturnsFalse()
    {
        var randomData = new byte[100];
        Random.Shared.NextBytes(randomData);

        _sut.IsValidHeader(randomData).Should().BeFalse();
    }

    [Fact]
    public void IsValidHeader_TooShort_ReturnsFalse()
    {
        var shortData = new byte[10];

        _sut.IsValidHeader(shortData).Should().BeFalse();
    }

    [Fact]
    public void IsValidHeader_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.IsValidHeader(null!));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Full pipeline: Serialise → Encrypt → Decrypt → Deserialise
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FullPipeline_RoundTripsCorrectly()
    {
        var original = CreateChangeSet();
        var passphrase = "integration-test-key";

        var serialised = _sut.Serialise(original);
        var encrypted = _sut.Encrypt(serialised, passphrase);

        _sut.IsValidHeader(encrypted).Should().BeTrue();

        var decrypted = _sut.Decrypt(encrypted, passphrase);
        var result = _sut.Deserialise(decrypted);

        result.DeviceId.Should().Be(original.DeviceId);
        result.Version.Should().Be(original.Version);
        result.Changes.Should().HaveCount(original.Changes.Count);
        result.Changes[0].EntityType.Should().Be(original.Changes[0].EntityType);
        result.Changes[0].EntityId.Should().Be(original.Changes[0].EntityId);
        result.Changes[0].ChangeType.Should().Be(original.Changes[0].ChangeType);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SyncChangeSet CreateChangeSet()
    {
        return new SyncChangeSet
        {
            DeviceId = "test-device-001",
            ExportedAt = DateTime.UtcNow,
            Version = 1,
            Changes =
            [
                new SyncChange
                {
                    EntityType     = "DocumentEntity",
                    EntityId       = 42,
                    ChangeType     = SyncChangeType.Updated,
                    Timestamp      = DateTime.UtcNow,
                    SerializedData = "{\"title\":\"Test Document\"}",
                },
                new SyncChange
                {
                    EntityType     = "CollectionEntity",
                    EntityId       = 99,
                    ChangeType     = SyncChangeType.Created,
                    Timestamp      = DateTime.UtcNow,
                    SerializedData = "{\"name\":\"My Collection\"}",
                },
            ],
        };
    }
}
