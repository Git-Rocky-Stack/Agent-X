using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the "Test connection" buttons for Ollama in onboarding and Settings.
/// <para>
/// Onboarding used to save the typed endpoint into the settings and then test
/// <c>IAiService.ActiveProvider</c>. The active provider is normally the built-in model, so the
/// button reported "Connected to Ollama" whenever the built-in model file existed (and failed
/// when Ollama ran but the model was missing), and the endpoint was saved before the user
/// finished the wizard. The model list on the next step came from the same wrong provider.
/// </para>
/// <para>
/// Both view models live in the WinUI project, which this test project cannot compile, so the
/// rule is checked on their source: probe a temporary Ollama provider built from the validated
/// endpoint, and do not save settings or consult the active provider while testing.
/// </para>
/// </summary>
public sealed class OllamaConnectionTestsProbeOllamaTests
{
    [Fact]
    public void Onboarding_connection_test_probes_the_typed_Ollama_endpoint_without_saving()
    {
        var body = ExtractMethod(ReadViewModel("OnboardingViewModel.cs"), "private async Task TestConnectionAsync()");

        body.Should().Contain("TryParseHttpEndpoint(OllamaEndpoint");
        body.Should().Contain("new AgentX.Core.AI.Providers.OllamaProvider(endpoint");
        body.Should().NotContain("SaveSettingsAsync", "nothing is saved until the wizard completes");
        body.Should().NotContain("ActiveProvider", "the active provider is usually the built-in model, not Ollama");
    }

    [Fact]
    public void Onboarding_model_list_comes_from_the_tested_Ollama_endpoint()
    {
        var body = ExtractMethod(ReadViewModel("OnboardingViewModel.cs"), "private async Task LoadModelsAsync()");

        body.Should().Contain("new AgentX.Core.AI.Providers.OllamaProvider(ollamaEndpoint");
        body.Should().NotContain("ActiveProvider");
    }

    [Fact]
    public void Settings_connection_test_validates_the_endpoint_first()
    {
        var body = ExtractMethod(ReadViewModel("SettingsViewModel.cs"), "private async Task TestOllamaConnectionAsync()");

        body.Should().Contain("TryParseHttpEndpoint(OllamaEndpoint");
        body.Should().NotContain("new Uri(", "a malformed endpoint gets a clear message instead of a bogus URI");
    }

    private static string ReadViewModel(string fileName) =>
        File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "ViewModels", fileName));

    /// <summary>Returns the method starting at <paramref name="signature"/>, up to its closing brace.</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the source declares {signature}");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after {signature}.");
    }

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the source root from {AppContext.BaseDirectory}.");
    }
}
