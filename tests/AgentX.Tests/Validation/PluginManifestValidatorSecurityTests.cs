using AgentX.Core.Services.Plugins;
using AgentX.Core.Validation;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Validation;

/// <summary>
/// Security-focused tests for <see cref="PluginManifestValidator"/>: the plugin ID (used verbatim
/// as an install directory name) and the entry assembly (loaded as executable code) must reject
/// any path-injection so installation/activation cannot escape the plugin sandbox directory.
/// </summary>
public sealed class PluginManifestValidatorSecurityTests
{
    private readonly PluginManifestValidator _sut = new();

    private static PluginManifest ValidManifest() => new()
    {
        Id = "com.vendor.myplugin",
        Name = "My Plugin",
        Version = "1.0.0",
        Author = "Test Author",
        Description = "A test plugin.",
        EntryAssembly = "MyPlugin.dll",
    };

    [Theory]
    [InlineData("..")]
    [InlineData("../evil")]
    [InlineData("com/evil")]
    [InlineData(@"com\evil")]
    [InlineData("a..b")]
    [InlineData(".com.vendor")]
    [InlineData("com.vendor.")]
    [InlineData("com..vendor")]
    public void Validate_RejectsPathInjectionInId(string maliciousId)
    {
        var manifest = ValidManifest();
        manifest.Id = maliciousId;

        var result = _sut.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Id));
    }

    [Theory]
    [InlineData("con.foo")]
    [InlineData("nul.bar")]
    [InlineData("COM1.x")]
    [InlineData("LPT9.y")]
    public void Validate_RejectsReservedDeviceNameId(string reservedId)
    {
        var manifest = ValidManifest();
        manifest.Id = reservedId;

        var result = _sut.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.Id));
    }

    [Theory]
    [InlineData(@"..\Other.dll")]
    [InlineData("../Other.dll")]
    [InlineData("sub/Other.dll")]
    [InlineData(@"C:\Windows\evil.dll")]
    [InlineData("/evil.dll")]
    public void Validate_RejectsPathInjectionInEntryAssembly(string maliciousAssembly)
    {
        var manifest = ValidManifest();
        manifest.EntryAssembly = maliciousAssembly;

        var result = _sut.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.FieldName == nameof(PluginManifest.EntryAssembly));
    }

    [Theory]
    [InlineData("com.vendor.myplugin")]
    [InlineData("io.company.my.deeply.nested.plugin")]
    [InlineData("org.agent-x.plugin")]
    public void Validate_AcceptsWellFormedReverseDnsId(string id)
    {
        var manifest = ValidManifest();
        manifest.Id = id;

        var result = _sut.Validate(manifest);

        result.Errors.Should().NotContain(e => e.FieldName == nameof(PluginManifest.Id));
    }
}
