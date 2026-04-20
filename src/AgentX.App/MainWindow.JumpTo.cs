using System.Linq;
using Microsoft.UI.Xaml;
using Serilog;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.App.Views.Dialogs;
using AgentX.Core.Documents;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Settings;

namespace AgentX.App;

public sealed partial class MainWindow
{
    private async Task<IReadOnlyList<JumpToItem>> LoadJumpToCandidatesAsync(CancellationToken ct)
    {
        var items = new List<JumpToItem>();
        foreach (var page in PageMap.OrderBy(p => p.Key))
        {
            items.Add(new JumpToItem(
                $"page.{page.Key}",
                ToDisplayName(page.Key),
                "Page",
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
                    "Document",
                    JumpToItemKind.Document,
                    _ =>
                    {
                        _navigationService.NavigateToPage("KnowledgeVault");
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
                    string.IsNullOrWhiteSpace(conversation.Title) ? "Untitled Conversation" : conversation.Title,
                    "Conversation",
                    JumpToItemKind.Conversation,
                    _ =>
                    {
                        _navigationService.NavigateToPage("Chat");
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
