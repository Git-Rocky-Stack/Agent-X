using System.Text.RegularExpressions;
using AgentX.App.ViewModels;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Processors;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Web;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the import file picker against offering a format nothing can read, and against leaving
/// out one a processor can.
/// <para>
/// The Knowledge Vault picker used to advertise a fixed extension list. Every entry on it is a
/// promise: the user is shown a file, allowed to select it, and expects it in their vault. If no
/// <c>IDocumentProcessor</c> claims that extension the import falls through to "unsupported
/// format" after the user has already chosen the file. The fixed list also failed the other way:
/// a format an active plugin adds could only be imported by drag and drop.
/// </para>
/// <para>
/// The picker now offers <see cref="KnowledgeVaultViewModel.GetImportFileTypes"/>: the extensions
/// <see cref="IDocumentService.GetSupportedExtensions"/> reports for the built-in and active plugin
/// processors, in the form the picker accepts. These tests keep a fixed list from coming back and
/// check that every built-in format survives the picker's rules.
/// </para>
/// </summary>
public sealed class ImportPickerOffersOnlyProcessableTypesTests
{
    [Fact]
    public void TheImportPicker_OffersTheProcessorsFormats_NotAFixedList()
    {
        var source = File.ReadAllText(Path.Combine(
            ResolveSourceRoot(), "AgentX.App", "Views", "KnowledgeVaultPage.xaml.cs"));
        var picker = ExtractMethod(source, "private async void OnImportFilesClick(");

        picker.Should().Contain("ViewModel.GetImportFileTypes()");
        Regex.IsMatch(picker, @"FileTypeFilter\.Add\(""").Should().BeFalse(
            "a literal extension drifts from the processors: either nothing reads it, or a format "
            + "a processor reads is missing from the picker");
    }

    [Fact]
    public void EveryBuiltInFormat_SurvivesThePickerRules()
    {
        var builtIn = BuiltInProcessors()
            .SelectMany(processor => processor.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        builtIn.Should().NotBeEmpty("the processor scan must find extensions, or this guard is vacuous");

        KnowledgeVaultViewModel.ToPickerFileTypes(builtIn).Should().BeEquivalentTo(
            builtIn.Select(extension => extension.ToLowerInvariant()),
            "the picker must keep offering every format the built-in processors read");
    }

    // Helpers

    /// <summary>Every built-in document processor, as the app registers them.</summary>
    private static IEnumerable<IDocumentProcessor> BuiltInProcessors() =>
    [
        new PdfProcessor(),
        new DocxProcessor(),
        new TextProcessor(),
        new MarkdownProcessor(),
        new CodeFileProcessor(),
        new ImageProcessor(),
        new AudioProcessor(Mock.Of<ITranscriptionService>()),
        new WebProcessor(Mock.Of<IWebScraperService>()),
    ];

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
