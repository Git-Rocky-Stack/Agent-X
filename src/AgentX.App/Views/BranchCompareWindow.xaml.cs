using AgentX.App.Helpers;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Localization;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AgentX.App.Views;

/// <summary>
/// Side-by-side branch comparison window. Built programmatically (no XAML)
/// to avoid the WinUI 3 XAML compiler crash that occurs when a secondary
/// Window class has its own XAML file. Without XAML there is no x:Uid, so
/// every text it shows comes from the string resources here, and every color
/// from the theme tokens (DESIGN.md): the chassis behind, inset content cards,
/// hairline dividers and secondary text, which HighContrast binds to the
/// system colors.
/// </summary>
public sealed class BranchCompareWindow : Window
{
    private readonly ILocalizationService _localization;

    // The operator's shift. ThemeService sets it on the main window's root only, so this
    // window takes the same one, and its own text and the brushes below match.
    private readonly ElementTheme _shift;

    public BranchCompareWindow(
        ConversationBranchTree mainBranch,
        ConversationBranchTree compareBranch,
        string mainTitle,
        string compareTitle)
    {
        _localization = App.GetService<ILocalizationService>();
        _shift = App.MainWindow.Content is FrameworkElement mainRoot
            ? mainRoot.ActualTheme
            : ElementTheme.Dark;
        Title = _localization.GetString("BranchCompare_WindowTitle");

        // Size and position the window
        var appWindow = this.AppWindow;
        appWindow.Resize(new SizeInt32(1000, 600));

        // Build the root content: the chassis fills the window, the content sits 16 in.
        var root = new Grid
        {
            ColumnSpacing = 0,
            Padding = new Thickness(16),
            RowSpacing = 8,
            RequestedTheme = _shift,
            Background = ThemeBrush("WindowBackgroundBrush")
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header row
        var headerGrid = new Grid { ColumnSpacing = 8 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mainHeader = BuildHeaderPanel(mainTitle, mainBranch);
        Grid.SetColumn(mainHeader, 0);
        headerGrid.Children.Add(mainHeader);

        var divider = new Border
        {
            Width = 1,
            Background = ThemeBrush("BorderSubtleBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(divider, 1);
        headerGrid.Children.Add(divider);

        var compareHeader = BuildHeaderPanel(compareTitle, compareBranch);
        Grid.SetColumn(compareHeader, 2);
        headerGrid.Children.Add(compareHeader);

        Grid.SetRow(headerGrid, 0);
        root.Children.Add(headerGrid);

        // Content row: scrollable message lists
        var contentGrid = new Grid { ColumnSpacing = 0 };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mainScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 8, 8, 0)
        };
        var mainPanel = BuildBranchContentPanel(mainBranch);
        mainScroll.Content = mainPanel;
        Grid.SetColumn(mainScroll, 0);
        contentGrid.Children.Add(mainScroll);

        var contentDivider = new Border
        {
            Width = 1,
            Background = ThemeBrush("BorderSubtleBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(contentDivider, 1);
        contentGrid.Children.Add(contentDivider);

        var compareScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8, 8, 0, 0)
        };
        var comparePanel = BuildBranchContentPanel(compareBranch);
        compareScroll.Content = comparePanel;
        Grid.SetColumn(compareScroll, 2);
        contentGrid.Children.Add(compareScroll);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        this.Content = root;
    }

    /// <summary>
    /// A theme token for the operator's shift, or its HighContrast system brush.
    /// </summary>
    private Brush? ThemeBrush(string key) => ThemeResources.Get(key, _shift) as Brush;

    private StackPanel BuildHeaderPanel(string title, ConversationBranchTree branch)
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });

        var label = !string.IsNullOrEmpty(branch.BranchLabel)
            ? _localization.GetString("BranchCompare_BranchLabel", branch.BranchLabel)
            : _localization.GetString("Chat_MainThread");
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ThemeBrush("TextSecondaryBrush"),
            FontSize = 12
        });

        var subLabel = branch.Conversation?.Title ?? _localization.GetString("BranchCompare_Untitled");
        var summary = branch.Children.Count == 1
            ? _localization.GetString("BranchCompare_HeaderSummaryOne", subLabel, branch.Children.Count)
            : _localization.GetString("BranchCompare_HeaderSummaryMany", subLabel, branch.Children.Count);
        panel.Children.Add(new TextBlock
        {
            Text = summary,
            FontSize = 12,
            Foreground = ThemeBrush("TextSecondaryBrush")
        });

        return panel;
    }

    private StackPanel BuildBranchContentPanel(ConversationBranchTree branch)
    {
        var panel = new StackPanel { Spacing = 8 };

        // Show branch metadata: an inset content card (CardInsetStyle's recipe)
        if (branch.Conversation is not null)
        {
            var metaBorder = new Border
            {
                Background = ThemeBrush("CardBrush"),
                BorderBrush = ThemeBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var metaPanel = new StackPanel { Spacing = 4 };

            metaPanel.Children.Add(new TextBlock
            {
                Text = branch.Conversation.Title ?? _localization.GetString("BranchCompare_UntitledConversation"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            });

            if (!string.IsNullOrEmpty(branch.BranchLabel))
            {
                metaPanel.Children.Add(new TextBlock
                {
                    Text = _localization.GetString("BranchCompare_Label", branch.BranchLabel),
                    FontSize = 12,
                    Foreground = ThemeBrush("TextSecondaryBrush")
                });
            }

            if (branch.BranchPointMessageId is not null)
            {
                metaPanel.Children.Add(new TextBlock
                {
                    Text = _localization.GetString("BranchCompare_BranchedFrom", branch.BranchPointMessageId),
                    FontSize = 11,
                    Foreground = ThemeBrush("TextSecondaryBrush")
                });
            }

            metaPanel.Children.Add(new TextBlock
            {
                Text = branch.Children.Count == 1
                    ? _localization.GetString("BranchCompare_SubBranchesOne", branch.Children.Count)
                    : _localization.GetString("BranchCompare_SubBranchesMany", branch.Children.Count),
                FontSize = 11,
                Foreground = ThemeBrush("TextSecondaryBrush")
            });

            metaBorder.Child = metaPanel;
            panel.Children.Add(metaBorder);
        }

        // Show child branches summary: inset cards with the medium hairline outline
        foreach (var child in branch.Children)
        {
            var childBorder = new Border
            {
                Background = ThemeBrush("CardBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                BorderBrush = ThemeBrush("BorderMediumBrush"),
                BorderThickness = new Thickness(1)
            };

            var childPanel = new StackPanel { Spacing = 2 };

            var childLabel = !string.IsNullOrEmpty(child.BranchLabel)
                ? child.BranchLabel
                : _localization.GetString(
                    "BranchCompare_BranchAtMessage",
                    child.BranchPointMessageId?.ToString() ?? string.Empty);
            childPanel.Children.Add(new TextBlock
            {
                Text = childLabel,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            });

            if (child.Conversation is not null)
            {
                childPanel.Children.Add(new TextBlock
                {
                    Text = child.Conversation.Title ?? _localization.GetString("BranchCompare_Untitled"),
                    FontSize = 12,
                    Foreground = ThemeBrush("TextSecondaryBrush")
                });
            }

            childBorder.Child = childPanel;
            panel.Children.Add(childBorder);
        }

        return panel;
    }
}
