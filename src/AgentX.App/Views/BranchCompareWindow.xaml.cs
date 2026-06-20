using AgentX.Core.Services.Chat.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace AgentX.App.Views;

/// <summary>
/// Side-by-side branch comparison window. Built programmatically (no XAML)
/// to avoid the WinUI 3 XAML compiler crash that occurs when a secondary
/// Window class has its own XAML file.
/// </summary>
public sealed class BranchCompareWindow : Window
{
    public BranchCompareWindow(
        ConversationBranchTree mainBranch,
        ConversationBranchTree compareBranch,
        string mainTitle,
        string compareTitle)
    {
        Title = "Branch Comparison";

        // Size and position the window
        var appWindow = this.AppWindow;
        appWindow.Resize(new SizeInt32(1000, 600));

        // Build the root content
        var root = new Grid
        {
            ColumnSpacing = 0,
            Margin = new Thickness(16),
            RowSpacing = 8
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
            Background = new SolidColorBrush(Colors.Gray),
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
            Background = new SolidColorBrush(Colors.Gray),
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

    private static StackPanel BuildHeaderPanel(string title, ConversationBranchTree branch)
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });

        var label = !string.IsNullOrEmpty(branch.BranchLabel)
            ? $"Branch: {branch.BranchLabel}"
            : "Main Thread";
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Colors.Gray),
            FontSize = 12
        });

        var subLabel = branch.Conversation?.Title ?? "Untitled";
        panel.Children.Add(new TextBlock
        {
            Text = $"{subLabel} \u2014 {branch.Children.Count} sub-branches",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        return panel;
    }

    private static StackPanel BuildBranchContentPanel(ConversationBranchTree branch)
    {
        var panel = new StackPanel { Spacing = 8 };

        // Show branch metadata
        if (branch.Conversation is not null)
        {
            var metaBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var metaPanel = new StackPanel { Spacing = 4 };

            metaPanel.Children.Add(new TextBlock
            {
                Text = branch.Conversation.Title ?? "Untitled Conversation",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            });

            if (!string.IsNullOrEmpty(branch.BranchLabel))
            {
                metaPanel.Children.Add(new TextBlock
                {
                    Text = $"Label: {branch.BranchLabel}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
            }

            if (branch.BranchPointMessageId is not null)
            {
                metaPanel.Children.Add(new TextBlock
                {
                    Text = $"Branched from message #{branch.BranchPointMessageId}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
            }

            metaPanel.Children.Add(new TextBlock
            {
                Text = $"{branch.Children.Count} sub-branch(es)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray)
            });

            metaBorder.Child = metaPanel;
            panel.Children.Add(metaBorder);
        }

        // Show child branches summary
        foreach (var child in branch.Children)
        {
            var childBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            var childPanel = new StackPanel { Spacing = 2 };

            var childLabel = !string.IsNullOrEmpty(child.BranchLabel)
                ? child.BranchLabel
                : $"Branch at msg #{child.BranchPointMessageId}";
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
                    Text = child.Conversation.Title ?? "Untitled",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
            }

            childBorder.Child = childPanel;
            panel.Children.Add(childBorder);
        }

        return panel;
    }
}
