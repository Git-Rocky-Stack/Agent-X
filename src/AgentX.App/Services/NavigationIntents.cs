namespace AgentX.App.Services;

/// <summary>
/// Navigation parameters that ask the destination page to do something on arrival,
/// as opposed to the entity ids (a conversation, a document) that Jump-To passes.
/// Strings rather than an enum because Frame navigation parameters travel as
/// objects and the pages already pattern-match on the parameter's runtime type.
/// </summary>
/// <remarks>
/// These exist so a command that is labelled "New Conversation" starts one. Before
/// this, the palette's three non-theme actions were plain page navigations wearing
/// action names.
/// </remarks>
public static class NavigationIntents
{
    /// <summary>Open Chat and start a fresh conversation.</summary>
    public const string NewConversation = "intent:new-conversation";

    /// <summary>Open the Knowledge Vault and raise the import file picker.</summary>
    public const string ImportFiles = "intent:import-files";
}
