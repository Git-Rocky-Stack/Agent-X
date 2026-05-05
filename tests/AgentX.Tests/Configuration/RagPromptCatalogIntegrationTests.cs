using AgentX.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentX.Tests.Configuration;

/// <summary>
/// Wave 3c: end-to-end binding test for <see cref="RagPromptCatalog"/>.
/// Writes a real <c>RagPrompts.json</c> to a temporary directory, binds it
/// through a real <see cref="ConfigurationBuilder"/> + <see cref="IOptionsMonitor{T}"/>,
/// and verifies the catalog returns the on-disk content. Covers the JSON
/// binding pipeline that the unit-level <see cref="RagPromptCatalogTests"/>
/// stubs out.
/// </summary>
public sealed class RagPromptCatalogIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _promptsFile;

    public RagPromptCatalogIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentx-prompt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _promptsFile = Path.Combine(_tempDir, "RagPrompts.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; ignore IO failures during teardown.
        }
    }

    private IRagPromptCatalog BuildCatalogFromDisk(string json, bool reloadOnChange = false)
    {
        File.WriteAllText(_promptsFile, json);

        var config = new ConfigurationBuilder()
            .SetBasePath(_tempDir)
            .AddJsonFile("RagPrompts.json", optional: false, reloadOnChange: reloadOnChange)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<RagPromptOptions>(config.GetSection("RagPrompts"));
        services.AddSingleton<IRagPromptCatalog, RagPromptCatalog>();

        return services.BuildServiceProvider().GetRequiredService<IRagPromptCatalog>();
    }

    [Fact]
    public void RealJsonFile_BindsThroughOptionsAndReachesCatalog()
    {
        const string json = """
            {
              "RagPrompts": {
                "EvalSystem": [
                  "Custom eval line 1.",
                  "Custom eval line 2."
                ],
                "HydeSystem": [
                  "Custom hyde."
                ]
              }
            }
            """;

        var catalog = BuildCatalogFromDisk(json);

        catalog.EvalSystem.Should().Be("Custom eval line 1.\nCustom eval line 2.");
        catalog.HydeSystem.Should().Be("Custom hyde.");
        // Untouched prompts fall back to compile-time defaults.
        catalog.RagSystemPrefix.Should().Be(RagPromptDefaults.RagSystemPrefix);
        catalog.RerankerSystem.Should().Be(RagPromptDefaults.RerankerSystem);
        catalog.CompressorSystem.Should().Be(RagPromptDefaults.CompressorSystem);
        catalog.MultiQuerySystem.Should().Be(RagPromptDefaults.MultiQuerySystem);
    }

    [Fact]
    public void EmptyJsonObject_AllPromptsResolveToDefaults()
    {
        const string json = """{ "RagPrompts": {} }""";

        var catalog = BuildCatalogFromDisk(json);

        catalog.RagSystemPrefix.Should().Be(RagPromptDefaults.RagSystemPrefix);
        catalog.EvalSystem.Should().Be(RagPromptDefaults.EvalSystem);
        catalog.RerankerSystem.Should().Be(RagPromptDefaults.RerankerSystem);
        catalog.CompressorSystem.Should().Be(RagPromptDefaults.CompressorSystem);
        catalog.MultiQuerySystem.Should().Be(RagPromptDefaults.MultiQuerySystem);
        catalog.HydeSystem.Should().Be(RagPromptDefaults.HydeSystem);
    }

    [Fact]
    public void MissingRagPromptsSection_AllResolveToDefaults()
    {
        // Operator deploys a config file but forgets the wrapper section —
        // the catalog must still produce working prompts.
        const string json = """{ "Other": { "Setting": "value" } }""";

        var catalog = BuildCatalogFromDisk(json);

        catalog.EvalSystem.Should().Be(RagPromptDefaults.EvalSystem);
        catalog.HydeSystem.Should().Be(RagPromptDefaults.HydeSystem);
    }

    [Fact]
    public void MultiLineRagPrefix_PreservesLineBreaks()
    {
        // Verifies the array → \n-joined-string transformation produces the
        // exact bytes Anthropic prompt caching keys on. Any drift here
        // invalidates the cache silently.
        const string json = """
            {
              "RagPrompts": {
                "RagSystemPrefix": [
                  "Line 1.",
                  "",
                  "Line 3 after blank."
                ]
              }
            }
            """;

        var catalog = BuildCatalogFromDisk(json);

        catalog.RagSystemPrefix.Should().Be("Line 1.\n\nLine 3 after blank.");
    }
}
