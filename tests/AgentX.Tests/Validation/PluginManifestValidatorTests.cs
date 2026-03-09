using AgentX.Core.Services.Plugins;
using AgentX.Core.Validation;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="PluginManifestValidator"/>.
/// Validates all constraints that a <see cref="PluginManifest"/> must satisfy
/// before a plugin archive is installed or activated.
/// </summary>
public sealed class PluginManifestValidatorTests
{
    private readonly PluginManifestValidator _sut = new();

    /// <summary>
    /// Creates a valid <see cref="PluginManifest"/> that should pass all validation checks.
    /// </summary>
    private static PluginManifest CreateValidManifest() => new()
    {
        Id = "com.vendor.myplugin",
        Name = "My Plugin",
        Version = "1.0.0",
        Author = "Test Author",
        Description = "A test plugin for unit testing.",
        PluginType = "Custom",
        MinAppVersion = "1.0.0",
        EntryAssembly = "MyPlugin.dll",
        Dependencies = [],
        Permissions = []
    };

    // ══════════════════════════════════════════════════════════════════════
    //  Valid manifest
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_WithValidManifest_Passes()
    {
        // Arrange
        var manifest = CreateValidManifest();

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithPreReleaseVersion_Passes()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Version = "2.1.0-beta";

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithBuildMetadataVersion_Passes()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Version = "0.9.1-rc.1+build.42";

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Empty Id
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyId_Fails(string id)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Id = id;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Id));
    }

    [Fact]
    public void Validate_NullId_Fails()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Id = null!;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Id));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Id without dot (not reverse-DNS)
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("myplugin")]
    [InlineData("noDotHere")]
    [InlineData("singleword")]
    public void Validate_IdWithoutDot_Fails(string id)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Id = id;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(PluginManifest.Id) &&
            e.Message.Contains("dot", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("com.vendor")]
    [InlineData("org.agentx.plugin")]
    [InlineData("io.company.my.deeply.nested.plugin")]
    public void Validate_IdWithDot_PassesIdCheck(string id)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Id = id;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(PluginManifest.Id));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Empty Name
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyName_Fails(string name)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Name = name;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Name));
    }

    [Fact]
    public void Validate_NullName_Fails()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Name = null!;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Name));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Name exceeding 100 chars
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_NameExceeding100Characters_Fails()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Name = new string('A', 101);

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(PluginManifest.Name) &&
            e.Message.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NameExactly100Characters_Passes()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Name = new string('A', 100);

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(PluginManifest.Name));
    }

    [Fact]
    public void Validate_NameWith99Characters_Passes()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Name = new string('B', 99);

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(PluginManifest.Name));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Invalid Version format
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1.0")]            // only major.minor
    [InlineData("1")]              // only major
    [InlineData("abc")]            // non-numeric
    [InlineData("v1.0.0")]         // prefixed with 'v'
    [InlineData("1.0.0.0")]        // four-part version
    [InlineData("1.0.0-")]         // trailing hyphen
    [InlineData("1.0.0-$invalid")] // special chars in pre-release
    public void Validate_InvalidVersionFormat_Fails(string version)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Version = version;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Version));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyVersion_Fails(string version)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Version = version;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Version));
    }

    [Fact]
    public void Validate_NullVersion_Fails()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Version = null!;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Version));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.1.0")]
    [InlineData("10.20.30")]
    [InlineData("1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1")]
    [InlineData("1.0.0+build")]
    [InlineData("1.0.0-beta+build.123")]
    public void Validate_ValidVersionFormats_Pass(string version)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.Version = version;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(PluginManifest.Version));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  EntryAssembly not ending in .dll
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("MyPlugin.exe")]
    [InlineData("MyPlugin")]
    [InlineData("MyPlugin.DL")]
    [InlineData("plugin.so")]
    [InlineData("plugin.dylib")]
    public void Validate_EntryAssemblyNotEndingInDll_Fails(string assembly)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.EntryAssembly = assembly;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.FieldName == nameof(PluginManifest.EntryAssembly) &&
            e.Message.Contains(".dll", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("MyPlugin.dll")]
    [InlineData("SomeAssembly.DLL")]  // case-insensitive
    [InlineData("My.Complex.Plugin.dll")]
    public void Validate_EntryAssemblyEndingInDll_Passes(string assembly)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.EntryAssembly = assembly;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.Errors.Should().NotContain(e => e.FieldName == nameof(PluginManifest.EntryAssembly));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyEntryAssembly_Fails(string assembly)
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.EntryAssembly = assembly;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.EntryAssembly));
    }

    [Fact]
    public void Validate_NullEntryAssembly_Fails()
    {
        // Arrange
        var manifest = CreateValidManifest();
        manifest.EntryAssembly = null!;

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.EntryAssembly));
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
        var manifest = new PluginManifest
        {
            Id = "",                      // empty
            Name = "",                    // empty
            Version = "invalid",          // bad format
            EntryAssembly = "noext"       // no .dll
        };

        // Act
        var result = _sut.Validate(manifest);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().Be(4,
            "all four validated fields are invalid and each should report an error");
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Id));
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Name));
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Version));
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.EntryAssembly));
    }
}
