using System.Linq;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.Views.Dialogs;
using AgentX.Core.Documents;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using Microsoft.UI.Xaml;
using Serilog;

namespace AgentX.App;

public sealed partial class MainWindow
{
    private async Task<IReadOnlyList<JumpToItem>> LoadJumpToCandidatesAsync(CancellationToken ct)
    {
        var localization = App.GetService<ILocalizationService>();
        var items = new List<JumpToItem>();
        foreach (var page in PageMap.OrderBy(p => p.Key))
        {
            items.Add(new JumpToItem(
                $"page.{page.Key}",
                PageDisplayName(page.Key, localization),
                localization.GetString("Main_JumpToKindPage"),
                JumpToItemKind.Page,
                _ =>
                {
                    _navigationService.NavigateToPage(page.Key);
                    return Task.CompletedTask;
                }));
        }

        try
        {
            var docs = await App.GetService<IDocumentService>().GetAllDocumentsAsync(ct: ct);
            foreach (var document in docs.Take(50))
            {
                var label = string.IsNullOrWhiteSpace(document.ExtractedTitle)
                    ? document.FileName
                    : document.ExtractedTitle;
                items.Add(new JumpToItem(
                    $"document.{document.Id}",
                    label,
                    localization.GetString("Main_JumpToKindDocument"),
                    JumpToItemKind.Document,
                    _ =>
                    {
                        // Carry the document the user actually picked; navigating to the
                        // vault without it drops them on an unfiltered list.
                        _navigationService.NavigateToPage("KnowledgeVault", document.Id);
                        return Task.CompletedTask;
                    }));
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to load documents for Jump-To candidates");
        }

        try
        {
            var conversations = await App.GetService<IConversationService>().GetAllConversationsAsync();
            foreach (var conversation in conversations.Take(50))
            {
                items.Add(new JumpToItem(
                    $"conversation.{conversation.Id}",
                    string.IsNullOrWhiteSpace(conversation.Title)
                        ? localization.GetString("Main_JumpToUntitledConversation")
                        : conversation.Title,
                    localization.GetString("Main_JumpToKindConversation"),
                    JumpToItemKind.Conversation,
                    _ =>
                    {
                        // Carry the conversation the user picked so Chat opens that thread
                        // instead of whatever happened to be active.
                        _navigationService.NavigateToPage("Chat", conversation.Id);
                        return Task.CompletedTask;
                    }));
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to load conversations for Jump-To candidates");
        }

        return items;
    }

    /// <summary>
    /// A page is named as the navigation rail names it, which its x:Uid already shows in the
    /// user's language. Onboarding has no rail entry, so it has a resource of its own.
    /// </summary>
    private string PageDisplayName(string pageKey, ILocalizationService localization)
    {
        if (_navItemMap.TryGetValue(pageKey, out var navItem) &&
            navItem.Content is string railLabel &&
            !string.IsNullOrWhiteSpace(railLabel))
        {
            return railLabel;
        }

        return string.Equals(pageKey, "Onboarding", StringComparison.Ordinal)
            ? localization.GetString("Main_JumpToOnboarding")
            : ToDisplayName(pageKey);
    }

    private static string ToDisplayName(string value) =>
        string.IsNullOrWhiteSpace(value) ? value
            : string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(value[i - 1]) ? " " + c : c.ToString()));

    private ElementTheme GetDialogTheme()
    {
        try
        {
            return App.GetService<IThemeService>().CurrentTheme;
        }
        catch
        {
            return ElementTheme.Dark;
        }
    }
}
