namespace AgentX.Core.Services.Privacy;

/// <summary>Why a message sent from chat leaves this computer.</summary>
public enum PromptRecipientKind
{
    /// <summary>A hosted AI provider answers it. <see cref="PromptRecipient.Name"/> is the provider.</summary>
    CloudAiProvider,

    /// <summary>An Ollama server on another machine answers it. The name is the server's host.</summary>
    RemoteOllama,

    /// <summary>Smart model routing may hand it to a configured cloud AI provider. No name.</summary>
    ModelRouting,

    /// <summary>Research Mode sends it to a hosted web search provider. The name is the provider.</summary>
    WebSearch,

    /// <summary>
    /// Research Mode sends it to a SearXNG instance, which forwards it to public search engines.
    /// The name is the instance's host.
    /// </summary>
    SearXng,
}

/// <summary>
/// One place a message sent from chat goes off this computer. Structured rather than a sentence,
/// so the chat can say it in the user's language.
/// </summary>
/// <param name="Kind">What receives the message.</param>
/// <param name="Name">The provider or host that receives it, or null when there is no single one.</param>
public sealed record PromptRecipient(PromptRecipientKind Kind, string? Name);
