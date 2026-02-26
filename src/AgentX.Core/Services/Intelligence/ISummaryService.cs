namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Provides AI-powered document summarization, key-point extraction,
/// and text translation capabilities.
/// </summary>
public interface ISummaryService
{
    /// <summary>
    /// Generates a concise summary of a document by its ID.
    /// Loads the document and its chunks from the database, concatenates chunk text
    /// (up to 8000 characters), and uses the AI service to produce a summary.
    /// </summary>
    /// <param name="documentId">The primary key of the document to summarize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A concise AI-generated summary of the document content.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the document is not found or has no indexed chunks.
    /// </exception>
    Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Extracts key points (bullet list) from a document by its ID.
    /// Each key point is a concise, single-sentence summary of an important finding or topic.
    /// </summary>
    /// <param name="documentId">The primary key of the document to analyze.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An ordered list of key points extracted from the document.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the document is not found or has no indexed chunks.
    /// </exception>
    Task<IReadOnlyList<string>> ExtractKeyPointsAsync(long documentId, CancellationToken ct = default);

    /// <summary>
    /// Translates the given text to the specified target language.
    /// Input text is capped at 4000 characters to fit within context limits.
    /// </summary>
    /// <param name="text">The source text to translate.</param>
    /// <param name="targetLanguage">The target language (e.g., "Spanish", "French", "Japanese").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The translated text in the target language.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="text"/> or <paramref name="targetLanguage"/> is null or empty.
    /// </exception>
    Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default);
}
