using AgentX.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentX.Tests.Configuration;

/// <summary>
/// P2-4: tests covering <see cref="RagPromptCatalog"/> resolution semantics —
/// fallback to <see cref="RagPromptDefaults"/> when no override is supplied,
/// override behavior when an array is set, and protection against silently
/// shipping all-blank arrays.
/// </summary>
public sealed class RagPromptCatalogTests
{
    private static RagPromptCatalog BuildCatalog(RagPromptOptions options)
    {
        var monitor = new TestOptionsMonitor<RagPromptOptions>(options);
        return new RagPromptCatalog(monitor);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Fallback semantics
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyOptions_ResolvesToCompileTimeDefaults()
    {
        var catalog = BuildCatalog(new RagPromptOptions());

        catalog.RagSystemPrefix.Should().Be(RagPromptDefaults.RagSystemPrefix);
        catalog.EvalSystem.Should().Be(RagPromptDefaults.EvalSystem);
        catalog.RerankerSystem.Should().Be(RagPromptDefaults.RerankerSystem);
        catalog.CompressorSystem.Should().Be(RagPromptDefaults.CompressorSystem);
        catalog.MultiQuerySystem.Should().Be(RagPromptDefaults.MultiQuerySystem);
        catalog.HydeSystem.Should().Be(RagPromptDefaults.HydeSystem);
    }

    [Fact]
    public void NullArrays_ResolveToCompileTimeDefaults()
    {
        var catalog = BuildCatalog(new RagPromptOptions
        {
            RagSystemPrefix = null,
            EvalSystem = null,
            RerankerSystem = null,
            CompressorSystem = null,
            MultiQuerySystem = null,
            HydeSystem = null
        });

        catalog.RagSystemPrefix.Should().Be(RagPromptDefaults.RagSystemPrefix);
        catalog.EvalSystem.Should().Be(RagPromptDefaults.EvalSystem);
    }

    [Fact]
    public void EmptyArrays_ResolveToCompileTimeDefaults()
    {
        var catalog = BuildCatalog(new RagPromptOptions
        {
            EvalSystem = Array.Empty<string>(),
            HydeSystem = Array.Empty<string>()
        });

        catalog.EvalSystem.Should().Be(RagPromptDefaults.EvalSystem);
        catalog.HydeSystem.Should().Be(RagPromptDefaults.HydeSystem);
    }

    [Fact]
    public void AllBlankArrays_ResolveToCompileTimeDefaults()
    {
        // Defends against an editor accidentally saving a prompt as ["", "", ""]
        // and silently breaking a downstream LLM call. The catalog must fall
        // back to the default rather than ship a blank prompt.
        var catalog = BuildCatalog(new RagPromptOptions
        {
            CompressorSystem = new[] { "", "   ", "\t", " " }
        });

        catalog.CompressorSystem.Should().Be(RagPromptDefaults.CompressorSystem);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Override semantics
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonEmptyArray_OverridesDefault()
    {
        var catalog = BuildCatalog(new RagPromptOptions
        {
            EvalSystem = new[] { "Custom eval prompt line 1.", "Custom line 2." }
        });

        catalog.EvalSystem.Should().Be("Custom eval prompt line 1.\nCustom line 2.");
        catalog.EvalSystem.Should().NotBe(RagPromptDefaults.EvalSystem);
    }

    [Fact]
    public void SingleLineArray_DoesNotAddTrailingNewline()
    {
        var catalog = BuildCatalog(new RagPromptOptions
        {
            HydeSystem = new[] { "Single line." }
        });

        catalog.HydeSystem.Should().Be("Single line.");
        catalog.HydeSystem.Should().NotEndWith("\n");
    }

    [Fact]
    public void OverrideOnePrompt_OthersStayDefault()
    {
        var catalog = BuildCatalog(new RagPromptOptions
        {
            EvalSystem = new[] { "Custom only here." }
        });

        catalog.EvalSystem.Should().Be("Custom only here.");
        catalog.RerankerSystem.Should().Be(RagPromptDefaults.RerankerSystem);
        catalog.CompressorSystem.Should().Be(RagPromptDefaults.CompressorSystem);
        catalog.MultiQuerySystem.Should().Be(RagPromptDefaults.MultiQuerySystem);
        catalog.HydeSystem.Should().Be(RagPromptDefaults.HydeSystem);
        catalog.RagSystemPrefix.Should().Be(RagPromptDefaults.RagSystemPrefix);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Hot-reload semantics
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void OptionsMonitorChange_ReflectsOnNextRead()
    {
        var monitor = new TestOptionsMonitor<RagPromptOptions>(new RagPromptOptions());
        var catalog = new RagPromptCatalog(monitor);

        catalog.EvalSystem.Should().Be(RagPromptDefaults.EvalSystem);

        // Simulate an operator editing RagPrompts.json — IOptionsMonitor swaps
        // CurrentValue. The catalog reads CurrentValue on every getter, so the
        // change should be visible immediately on the next access.
        monitor.UpdateValue(new RagPromptOptions
        {
            EvalSystem = new[] { "Hot-reloaded prompt." }
        });

        catalog.EvalSystem.Should().Be("Hot-reloaded prompt.");
    }

    [Fact]
    public void NullMonitor_ThrowsArgumentNullException()
    {
        Action act = () => new RagPromptCatalog(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal IOptionsMonitor stub that lets tests swap the current value
    /// on demand to exercise the catalog's hot-reload behavior. Doesn't fire
    /// change tokens (the catalog reads CurrentValue, not via OnChange).
    /// </summary>
    private sealed class TestOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
    {
        private T _current = initial;

        public T CurrentValue => _current;

        public T Get(string? name) => _current;

        public IDisposable? OnChange(Action<T, string?> listener) => null;

        public void UpdateValue(T value) => _current = value;
    }
}
