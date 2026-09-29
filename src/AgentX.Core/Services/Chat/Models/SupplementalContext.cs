using AgentX.Core.Search.Models;

namespace AgentX.Core.Services.Chat.Models;

/// <summary>
/// Context added to one reply only, such as Research Mode's web results: the text the model
/// sees with the conversation, and the sources that text cites, which are saved with the answer.
/// </summary>
/// <param name="PromptContext">The context block added to the reply's prompt. It is not saved.</param>
/// <param name="Citations">The sources the block cites, numbered in its order ([1] is the first).</param>
public sealed record SupplementalContext(string PromptContext, IReadOnlyList<WebCitation> Citations);
