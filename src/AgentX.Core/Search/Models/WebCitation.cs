namespace AgentX.Core.Search.Models;

/// <summary>
/// Indicates whether a citation originates from the local Knowledge Vault
/// or from an external web source discovered during Deep Research Mode.
/// </summary>
public enum WebCitationSource
{
    /// <summary>Citation points to a document in the local Knowledge Vault.</summary>
    Vault,

    /// <summary>Citation points to an external web page discovered via web search.</summary>
    Web
}

/// <summary>
/// Represents a citation that can reference either a local vault document
/// or an external web source. Used in Deep Research Mode to blend
/// vault-sourced and web-sourced context into a single, unified citation list.
/// </summary>
public sealed class WebCitation
{
    /// <summary>Display title of the cited source.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>URL of the cited source (web URL for Web citations; file path for Vault citations).</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Short excerpt/snippet from the cited source.</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>Whether this citation originates from the local vault or the web.</summary>
    public WebCitationSource Source { get; init; }

    /// <summary>
    /// For Vault citations: the original document file name.
    /// Null for Web citations.
    /// </summary>
    public string? DocumentName { get; init; }
}