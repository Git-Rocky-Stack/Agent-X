using AgentX.Core.Services.Security;
using FluentAssertions;
using System;
using Xunit;

namespace AgentX.Tests.Services.Security;

public class DpapiEncryptionServiceTests
{
    private readonly DpapiEncryptionService _sut = new();

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
    {
        // Arrange
        const string plaintext = "sk-test-api-key-12345";

        // Act
        string encrypted = _sut.Encrypt(plaintext);
        string decrypted = _sut.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_ReturnsDifferentValueThanInput()
    {
        // Arrange
        const string plaintext = "my-secret-key";

        // Act
        string encrypted = _sut.Encrypt(plaintext);

        // Assert
        encrypted.Should().NotBe(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesBase64String()
    {
        // Arrange
        const string plaintext = "api-key-value";

        // Act
        string encrypted = _sut.Encrypt(plaintext);

        // Assert
        encrypted.Should().StartWith("DPAPI:");
        string base64Part = encrypted["DPAPI:".Length..];

        // Valid base64 should not throw
        Action act = () => Convert.FromBase64String(base64Part);
        act.Should().NotThrow<FormatException>();
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentCiphertext()
    {
        // Arrange
        const string plaintext = "deterministic-test-value";

        // Act
        string first = _sut.Encrypt(plaintext);
        string second = _sut.Encrypt(plaintext);

        // Assert — DPAPI is non-deterministic; same input produces different output each time
        first.Should().NotBe(second, "because DPAPI encryption is non-deterministic");

        // Both should still decrypt to the same original value
        _sut.Decrypt(first).Should().Be(plaintext);
        _sut.Decrypt(second).Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_InvalidBase64_ThrowsFormatException()
    {
        // Arrange
        const string invalidCiphertext = "DPAPI:$$$not-valid-base64$$$";

        // Act
        Action act = () => _sut.Decrypt(invalidCiphertext);

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEncryptedEmptyString()
    {
        // Arrange
        const string plaintext = "";

        // Act
        string encrypted = _sut.Encrypt(plaintext);
        string decrypted = _sut.Decrypt(encrypted);

        // Assert
        encrypted.Should().StartWith("DPAPI:");
        decrypted.Should().BeEmpty();
    }

    [Fact]
    public void IsEncrypted_EncryptedValue_ReturnsTrue()
    {
        // Arrange
        var encrypted = _sut.Encrypt("test-key");

        // Act
        bool result = _sut.IsEncrypted(encrypted);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_PlaintextKey_ReturnsFalse()
    {
        // Arrange
        const string plaintext = "sk-proj-abc123";

        // Act
        bool result = _sut.IsEncrypted(plaintext);

        // Assert
        result.Should().BeFalse();
    }
}