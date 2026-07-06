using System.Text.RegularExpressions;
using AgentX.App.Helpers;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgentX.App.Controls;

/// <summary>
/// A UserControl that renders a list of <see cref="MarkdownSegment"/> instances
/// into rich visual elements: code blocks with copy buttons, headings, list items,
/// and text with inline bold/code formatting.
///
/// Designed for the Agent-X dark theme with the red accent color system.
/// </summary>
public sealed partial class MarkdownMessageControl : UserControl
{
    // ── Inline formatting regex ──────────────────────────────────────
    // Matches **bold** and `inline code` patterns for rich text rendering.
    private static readonly Regex InlineFormattingRegex = new(
        @"(\*\*(.+?)\*\*)|(`([^`]+)`)",
        RegexOptions.Compiled);

    // ── Dependency Property ──────────────────────────────────────────

    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(
            nameof(Segments),
            typeof(List<MarkdownSegment>),
            typeof(MarkdownMessageControl),
            new PropertyMetadata(null, OnSegmentsChanged));

    public List<MarkdownSegment>? Segments
    {
        get => (List<MarkdownSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public MarkdownMessageControl()
    {
        InitializeComponent();
    }

    // ═══════════════════════════════════════════════════════════════════
    // PROPERTY CHANGE CALLBACK
    // ═══════════════════════════════════════════════════════════════════

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownMessageControl control)
        {
            control.RenderSegments();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEGMENT RENDERING
    // ═══════════════════════════════════════════════════════════════════

    private void RenderSegments()
    {
        ContentPanel.Children.Clear();

        var segments = Segments;
        if (segments is null || segments.Count == 0) return;

        foreach (var segment in segments)
        {
            UIElement element = segment.Type switch
            {
                SegmentType.CodeBlock => CreateCodeBlock(segment),
                SegmentType.Heading => CreateHeading(segment),
                SegmentType.ListItem => CreateListItem(segment),
                _ => CreateTextBlock(segment)
            };

            ContentPanel.Children.Add(element);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CODE BLOCK RENDERING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a complete code block element with:
    /// - A dark header bar showing the language label and a copy button
    /// - A scrollable monospace code content area
    /// </summary>
    private static UIElement CreateCodeBlock(MarkdownSegment segment)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Header: language label + copy button ──────────────────
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 13, 13, 13)),
            CornerRadius = new CornerRadius(2, 2, 0, 0),
            Padding = new Thickness(12, 6, 8, 6)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Language label
        var languageLabel = new TextBlock
        {
            Text = (segment.Language ?? "code").ToUpperInvariant(),
            FontFamily = (FontFamily)Application.Current.Resources["FontTelemetry"],
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            CharacterSpacing = 40,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(languageLabel, 0);
        headerGrid.Children.Add(languageLabel);

        // Copy button with icon and label
        var copyButtonContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };
        copyButtonContent.Children.Add(new FontIcon
        {
            Glyph = "\uE8C8",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
        });
        copyButtonContent.Children.Add(new TextBlock
        {
            Text = "Copy",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
        });

        var copyButton = new Button
        {
            Content = copyButtonContent,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Capture content for the closure to avoid capturing the segment reference
        var codeContent = segment.Content;
        copyButton.Click += async (s, e) =>
        {
            try
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(codeContent);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                // Provide visual feedback: briefly change the button text to "Copied!"
                if (s is Button btn && btn.Content is StackPanel sp && sp.Children.Count >= 2)
                {
                    if (sp.Children[1] is TextBlock tb)
                    {
                        var originalText = tb.Text;
                        tb.Text = "Copied!";
                        await System.Threading.Tasks.Task.Delay(1500);
                        tb.Text = originalText;
                    }
                }
            }
            catch
            {
                // Clipboard operations can fail in some edge cases; non-critical.
            }
        };

        Grid.SetColumn(copyButton, 1);
        headerGrid.Children.Add(copyButton);
        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        grid.Children.Add(headerBorder);

        // ── Code content area (with syntax highlighting) ──────────
        var codeBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 8, 8, 8)),
            CornerRadius = new CornerRadius(0, 0, 2, 2),
            Padding = new Thickness(16, 12, 16, 12)
        };

        var codeScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        if (SyntaxHighlighter.IsSupported(segment.Language))
        {
            // Syntax-highlighted code via RichTextBlock with colored Runs
            var richBlock = new RichTextBlock
            {
                FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true,
                LineHeight = 20
            };

            var paragraph = new Paragraph();
            var runs = SyntaxHighlighter.Highlight(segment.Content, segment.Language);
            foreach (var run in runs)
            {
                paragraph.Inlines.Add(run);
            }
            richBlock.Blocks.Add(paragraph);

            codeScroll.Content = richBlock;
        }
        else
        {
            // Fallback: plain monospace TextBlock (no highlighting)
            var codeText = new TextBlock
            {
                Text = segment.Content,
                FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 220, 220, 230)),
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true,
                LineHeight = 20
            };
            codeScroll.Content = codeText;
        }

        codeBorder.Child = codeScroll;
        Grid.SetRow(codeBorder, 1);
        grid.Children.Add(codeBorder);

        grid.Margin = new Thickness(0, 4, 0, 4);
        return grid;
    }

    // ═══════════════════════════════════════════════════════════════════
    // HEADING RENDERING
    // ═══════════════════════════════════════════════════════════════════

    private static UIElement CreateHeading(MarkdownSegment segment)
    {
        return new TextBlock
        {
            Text = segment.Content,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 4)
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // LIST ITEM RENDERING
    // ═══════════════════════════════════════════════════════════════════

    private static UIElement CreateListItem(MarkdownSegment segment)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 2, 0, 2)
        };

        // Bullet character with accent color
        panel.Children.Add(new TextBlock
        {
            Text = "\u2022",
            Foreground = (Brush)Application.Current.Resources["TextAccentBrush"],
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0)
        });

        // List item content with inline formatting support
        panel.Children.Add(CreateInlineFormattedText(segment.Content));
        return panel;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PLAIN TEXT RENDERING
    // ═══════════════════════════════════════════════════════════════════

    private static UIElement CreateTextBlock(MarkdownSegment segment)
    {
        return CreateInlineFormattedText(segment.Content);
    }

    // ═══════════════════════════════════════════════════════════════════
    // INLINE FORMATTING (bold + inline code)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a TextBlock that renders inline formatting:
    /// - **bold text** becomes a Bold inline
    /// - `inline code` becomes a monospace Run with accent color
    /// - All other text is rendered as normal Runs
    /// </summary>
    private static TextBlock CreateInlineFormattedText(string text)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            FontSize = 14,
            LineHeight = 22
        };

        var lastIndex = 0;
        var hasInlineFormatting = false;

        foreach (Match match in InlineFormattingRegex.Matches(text))
        {
            hasInlineFormatting = true;

            // Add plain text before this match
            if (match.Index > lastIndex)
            {
                textBlock.Inlines.Add(new Run
                {
                    Text = text[lastIndex..match.Index]
                });
            }

            if (match.Groups[2].Success)
            {
                // Bold text: **content**
                var bold = new Bold();
                bold.Inlines.Add(new Run
                {
                    Text = match.Groups[2].Value
                });
                textBlock.Inlines.Add(bold);
            }
            else if (match.Groups[4].Success)
            {
                // Inline code: `content`
                // Note: WinUI 3 does not support Background on Run, so we use
                // a distinct foreground color with monospace font to differentiate.
                var codeRun = new Run
                {
                    Text = match.Groups[4].Value,
                    FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 134, 132))
                };
                textBlock.Inlines.Add(codeRun);
            }

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after the last match
        if (lastIndex < text.Length && hasInlineFormatting)
        {
            textBlock.Inlines.Add(new Run
            {
                Text = text[lastIndex..]
            });
        }

        // If no inline formatting was found, set Text directly for simplicity
        if (!hasInlineFormatting)
        {
            textBlock.Text = text;
        }

        return textBlock;
    }
}
