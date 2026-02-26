using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Analyzes uncategorized documents and provides AI-powered suggestions for
/// organizing them into collections with appropriate tags.
/// </summary>
public interface IOrganizationSuggestionService
{
    /// <summary>
    /// Analyzes documents that have no collection associations and suggests
    /// appropriate collections and tags for each one based on their content.
    /// </summary>
    /// <param name="maxDocuments">
    /// The maximum number of uncategorized documents to analyze in a single batch.
    /// Defaults to 20 to balance thoroughness with response time.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A list of organization suggestions, one per analyzed document.
    /// Each suggestion includes a recommended collection, tags, reasoning, and confidence score.
    /// Returns an empty list if all documents are already categorized.
    /// </returns>
    Task<IReadOnlyList<OrganizationSuggestion>> SuggestOrganizationAsync(
        int maxDocuments = 20, CancellationToken ct = default);
}
