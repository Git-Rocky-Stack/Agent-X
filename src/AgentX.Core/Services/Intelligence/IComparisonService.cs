using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Provides AI-powered comparative analysis across two or more documents.
/// The service retrieves representative chunks from each document via semantic
/// search, feeds them to the active AI model with a structured JSON-output prompt,
/// and parses the response into a <see cref="ComparisonReport"/> that can be
/// rendered directly in the UI or exported as Markdown.
/// </summary>
public interface IComparisonService
{
    /// <summary>
    /// Compares two or more documents and returns a structured analysis report.
    /// </summary>
    /// <param name="documentIds">
    /// The primary keys of the documents to compare. Must contain at least two
    /// distinct IDs. Documents that cannot be found or have no indexed chunks are
    /// skipped with a warning; the operation proceeds with the remaining documents
    /// as long as at least two are resolvable.
    /// </param>
    /// <param name="options">
    /// Optional configuration controlling chunk count, focus topic, and detail level.
    /// When null, <see cref="ComparisonOptions"/> defaults are used.
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter that receives human-readable status messages as
    /// the operation advances through its stages (e.g. "Loading documents…",
    /// "Running AI analysis…"). Suitable for display in a progress dialog.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A fully populated <see cref="ComparisonReport"/> containing similarities,
    /// differences, contradictions, unique points per document, an executive
    /// summary, and telemetry (token count and duration).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="documentIds"/> contains fewer than two IDs.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when fewer than two of the supplied document IDs can be resolved to
    /// indexed documents, making a comparison impossible.
    /// </exception>
    Task<ComparisonReport> CompareDocumentsAsync(
        IReadOnlyList<long> documentIds,
        ComparisonOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Renders a <see cref="ComparisonReport"/> as a formatted Markdown document
    /// suitable for saving to disk, copying to the clipboard, or previewing in a
    /// Markdown viewer.
    /// </summary>
    /// <param name="report">The comparison report to render. Must not be null.</param>
    /// <returns>
    /// A Markdown string with headed sections for Summary, Similarities,
    /// Differences, Contradictions, and Unique Points per document.
    /// </returns>
    Task<string> ExportComparisonAsMarkdownAsync(ComparisonReport report);
}
