using AgentX.App.ViewModels;
using AgentX.Core.Services.Intelligence.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Serilog;
using Windows.UI;

namespace AgentX.App.Views;

/// <summary>
/// Knowledge Graph visualization page. Renders a force-directed node graph
/// on a Canvas, showing document relationships, collection clusters, and
/// tag connections. Nodes are clickable for detail display in the sidebar.
/// Supports zoom, search highlighting, cluster highlighting, and hover tooltips.
/// </summary>
public sealed partial class KnowledgeGraphPage : Page
{
    /// <summary>
    /// Minimum canvas dimension required before rendering the graph.
    /// Prevents rendering into a zero-size or negligible canvas.
    /// </summary>
    private const double MinCanvasDimension = 50.0;

    /// <summary>
    /// Padding inside the canvas edges to prevent nodes from being clipped.
    /// </summary>
    private const double CanvasPadding = 40.0;

    /// <summary>
    /// Maximum characters for a node label displayed on the graph.
    /// </summary>
    private const int MaxLabelLength = 15;

    public KnowledgeGraphViewModel ViewModel { get; }

    public KnowledgeGraphPage()
    {
        ViewModel = App.GetService<KnowledgeGraphViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    // =================================================================
    // LIFECYCLE
    // =================================================================

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("KnowledgeGraphPage loaded");

        // Subscribe to ViewModel property changes for reactive rendering
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        await ViewModel.InitializeAsync();

        // Initial render (DispatcherQueue ensures canvas has measured)
        DispatcherQueue.TryEnqueue(RenderGraph);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(KnowledgeGraphViewModel.GraphData):
            case nameof(KnowledgeGraphViewModel.ZoomLevel):
            case nameof(KnowledgeGraphViewModel.HighlightedNodeIds):
                DispatcherQueue.TryEnqueue(RenderGraph);
                break;

            case nameof(KnowledgeGraphViewModel.SelectedNode):
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateDetailPanel();
                    UpdateDetailColumnWidth();
                });
                break;
        }
    }

    // =================================================================
    // CANVAS SIZE CHANGED
    // =================================================================

    private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-render on canvas resize to keep the graph fitted
        RenderGraph();
    }

    // =================================================================
    // FILTER TOGGLE HANDLER
    // =================================================================

    private void OnFilterToggleChanged(object sender, RoutedEventArgs e)
    {
        // Re-render the graph when any filter toggle changes
        RenderGraph();
    }

    // =================================================================
    // DETAIL SIDEBAR
    // =================================================================

    /// <summary>
    /// Updates the detail sidebar column width based on whether a node is selected.
    /// </summary>
    private void UpdateDetailColumnWidth()
    {
        DetailColumnDef.Width = ViewModel.SelectedNode != null
            ? new GridLength(280)
            : new GridLength(0);
    }

    /// <summary>
    /// Populates the detail panel with the currently selected node's data.
    /// </summary>
    private void UpdateDetailPanel()
    {
        var node = ViewModel.SelectedNode;
        if (node == null)
            return;

        // Node type badge
        NodeTypeText.Text = node.NodeType.ToString().ToUpperInvariant();
        var badgeColor = ParseColor(node.ColorHex);
        NodeTypeBadge.Background = new SolidColorBrush(
            Color.FromArgb(40, badgeColor.R, badgeColor.G, badgeColor.B));
        NodeTypeText.Foreground = new SolidColorBrush(badgeColor);

        // Labels
        NodeLabelText.Text = node.Label;
        NodeSubtitleText.Text = node.Subtitle ?? "--";
        SubtitlePanel.Visibility = !string.IsNullOrEmpty(node.Subtitle)
            ? Visibility.Visible : Visibility.Collapsed;

        // Stats
        NodeConnectionCountText.Text = node.ConnectionCount.ToString();
        NodePositionText.Text = $"X: {node.X:F1}  Y: {node.Y:F1}";
    }

    // =================================================================
    // GRAPH RENDERING
    // =================================================================

    /// <summary>
    /// Clears the canvas and renders all visible nodes and edges from the
    /// current graph data. Applies coordinate normalization, zoom, and
    /// highlighting (search or cluster) to fit the graph into the available
    /// canvas space with padding.
    /// </summary>
    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();

        var graphData = ViewModel.GraphData;
        if (graphData == null || graphData.Nodes.Count == 0)
            return;

        var canvasWidth = GraphCanvas.ActualWidth;
        var canvasHeight = GraphCanvas.ActualHeight;

        if (canvasWidth < MinCanvasDimension || canvasHeight < MinCanvasDimension)
            return;

        // ── Filter nodes by toggle state ─────────────────────────────
        var visibleNodes = graphData.Nodes.Where(IsNodeVisible).ToList();
        var visibleNodeIds = new HashSet<string>(visibleNodes.Select(n => n.Id));

        if (visibleNodes.Count == 0)
            return;

        // ── Calculate bounding box and normalization ─────────────────
        var (offsetX, offsetY, scale) = CalculateTransform(visibleNodes, canvasWidth, canvasHeight);

        // ── Zoom ─────────────────────────────────────────────────────
        var zoomScale = ViewModel.ZoomLevel;
        var centerX = canvasWidth / 2.0;
        var centerY = canvasHeight / 2.0;

        // ── Highlight state ──────────────────────────────────────────
        var isHighlighting = ViewModel.IsClusterHighlighted || ViewModel.HasSearchResults;
        var highlightedIds = ViewModel.HighlightedNodeIds;

        // ── Draw edges first (behind nodes) ──────────────────────────
        foreach (var edge in graphData.Edges)
        {
            // Only draw edges where both endpoints are visible
            if (!visibleNodeIds.Contains(edge.SourceId) || !visibleNodeIds.Contains(edge.TargetId))
                continue;

            var sourceNode = visibleNodes.FirstOrDefault(n => n.Id == edge.SourceId);
            var targetNode = visibleNodes.FirstOrDefault(n => n.Id == edge.TargetId);

            if (sourceNode == null || targetNode == null)
                continue;

            // Apply zoom transform (scale from canvas center)
            var sx = (sourceNode.X * scale + offsetX - centerX) * zoomScale + centerX;
            var sy = (sourceNode.Y * scale + offsetY - centerY) * zoomScale + centerY;
            var tx = (targetNode.X * scale + offsetX - centerX) * zoomScale + centerX;
            var ty = (targetNode.Y * scale + offsetY - centerY) * zoomScale + centerY;

            var edgeColor = ParseColor(edge.ColorHex);

            // Determine edge highlighting
            var bothHighlighted = isHighlighting &&
                highlightedIds.Contains(edge.SourceId) && highlightedIds.Contains(edge.TargetId);

            var edgeOpacity = isHighlighting
                ? (bothHighlighted ? 0.6 : 0.06)
                : 0.3;
            var edgeThickness = bothHighlighted
                ? Math.Clamp(edge.Weight * 1.2, 1.0, 4.0)
                : Math.Clamp(edge.Weight * 0.8, 0.5, 3.0);

            var line = new Line
            {
                X1 = sx,
                Y1 = sy,
                X2 = tx,
                Y2 = ty,
                Stroke = new SolidColorBrush(edgeColor),
                StrokeThickness = edgeThickness,
                Opacity = edgeOpacity,
            };

            GraphCanvas.Children.Add(line);
        }

        // ── Draw nodes ───────────────────────────────────────────────
        var textBrush = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"];

        foreach (var node in visibleNodes)
        {
            // Apply zoom transform
            var rawX = node.X * scale + offsetX;
            var rawY = node.Y * scale + offsetY;
            var nx = (rawX - centerX) * zoomScale + centerX;
            var ny = (rawY - centerY) * zoomScale + centerY;
            var nodeColor = ParseColor(node.ColorHex);
            var nodeSize = node.Size * zoomScale;

            var isNodeHighlighted = !isHighlighting || highlightedIds.Contains(node.Id);
            var nodeOpacity = isNodeHighlighted ? 0.9 : 0.12;

            // Draw glow ring for highlighted nodes when highlighting is active
            if (isHighlighting && highlightedIds.Contains(node.Id))
            {
                var glowSize = nodeSize + 8;
                var glow = new Ellipse
                {
                    Width = glowSize,
                    Height = glowSize,
                    Fill = new SolidColorBrush(
                        Color.FromArgb(50, nodeColor.R, nodeColor.G, nodeColor.B)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(glow, nx - glowSize / 2);
                Canvas.SetTop(glow, ny - glowSize / 2);
                GraphCanvas.Children.Add(glow);
            }

            // Draw the node circle
            var ellipse = new Ellipse
            {
                Width = nodeSize,
                Height = nodeSize,
                Fill = new SolidColorBrush(nodeColor),
                Opacity = nodeOpacity,
            };

            // Highlight the selected node
            if (ViewModel.SelectedNode != null && ViewModel.SelectedNode.Id == node.Id)
            {
                ellipse.Opacity = 1.0;
                ellipse.StrokeThickness = 2;
                ellipse.Stroke = new SolidColorBrush(Colors.White);
            }

            Canvas.SetLeft(ellipse, nx - nodeSize / 2);
            Canvas.SetTop(ellipse, ny - nodeSize / 2);

            // Make the node clickable
            ellipse.Tag = node;
            ellipse.PointerPressed += Node_PointerPressed;
            ellipse.PointerEntered += Node_PointerEntered;
            ellipse.PointerExited += Node_PointerExited;

            GraphCanvas.Children.Add(ellipse);

            // Draw label (only for visible/highlighted nodes)
            if (isNodeHighlighted)
            {
                var scaledFontSize = 10 * Math.Min(zoomScale, 1.5);
                var scaledMaxWidth = 80 * zoomScale;

                var label = new TextBlock
                {
                    Text = TruncateLabel(node.Label, MaxLabelLength),
                    FontSize = scaledFontSize,
                    Foreground = textBrush,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = scaledMaxWidth,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                Canvas.SetLeft(label, nx - scaledMaxWidth / 2);
                Canvas.SetTop(label, ny + nodeSize / 2 + 3);
                GraphCanvas.Children.Add(label);
            }
        }
    }

    // =================================================================
    // NODE INTERACTION HANDLERS
    // =================================================================

    /// <summary>
    /// Handles click/tap on a node ellipse to select it and optionally
    /// trigger cluster highlighting for Collection/Tag nodes.
    /// </summary>
    private void Node_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Ellipse { Tag: GraphNode node })
        {
            ViewModel.SelectNodeCommand.Execute(node);

            // Trigger cluster highlight for Collection/Tag nodes
            if (node.NodeType is GraphNodeType.Collection or GraphNodeType.Tag)
            {
                ViewModel.HighlightClusterCommand.Execute(node);
            }

            e.Handled = true;

            // Re-render to update selection highlight
            RenderGraph();
        }
    }

    /// <summary>
    /// Provides hover feedback by increasing node opacity and showing tooltip.
    /// </summary>
    private void Node_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Ellipse { Tag: GraphNode node } ellipse)
        {
            // Increase opacity unless node is dimmed by highlighting
            var isHighlighting = ViewModel.IsClusterHighlighted || ViewModel.HasSearchResults;
            if (!isHighlighting || ViewModel.HighlightedNodeIds.Contains(node.Id))
            {
                ellipse.Opacity = 1.0;
            }

            ShowTooltip(node, e);
        }
    }

    /// <summary>
    /// Restores node opacity when pointer leaves and hides tooltip.
    /// </summary>
    private void Node_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Ellipse { Tag: GraphNode node } ellipse)
        {
            var isHighlighting = ViewModel.IsClusterHighlighted || ViewModel.HasSearchResults;

            // Keep full opacity if this is the selected node
            if (ViewModel.SelectedNode?.Id == node.Id)
            {
                ellipse.Opacity = 1.0;
            }
            else if (isHighlighting && !ViewModel.HighlightedNodeIds.Contains(node.Id))
            {
                ellipse.Opacity = 0.12;
            }
            else
            {
                ellipse.Opacity = 0.9;
            }

            HideTooltip();
        }
    }

    // =================================================================
    // CANVAS INTERACTION
    // =================================================================

    /// <summary>
    /// Clicking on canvas background clears cluster highlight.
    /// </summary>
    private void GraphCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsClusterHighlighted)
        {
            ViewModel.ClearClusterHighlightCommand.Execute(null);
        }
    }

    /// <summary>
    /// Mouse wheel zoom on the canvas.
    /// </summary>
    private void GraphCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(GraphCanvas).Properties.MouseWheelDelta;
        if (delta > 0)
            ViewModel.ZoomInCommand.Execute(null);
        else if (delta < 0)
            ViewModel.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }

    // =================================================================
    // TOOLTIP
    // =================================================================

    /// <summary>
    /// Displays the hover tooltip near the pointer with node details.
    /// </summary>
    private void ShowTooltip(GraphNode node, PointerRoutedEventArgs e)
    {
        TooltipLabel.Text = node.Label;
        TooltipType.Text = node.NodeType.ToString();
        TooltipTypeIndicator.Fill = new SolidColorBrush(ParseColor(node.ColorHex));
        TooltipConnections.Text = $"{node.ConnectionCount} connection{(node.ConnectionCount != 1 ? "s" : "")}";

        var point = e.GetCurrentPoint(GraphCanvas).Position;
        NodeTooltip.Margin = new Thickness(point.X + 16, point.Y - 8, 0, 0);
        NodeTooltip.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hides the hover tooltip.
    /// </summary>
    private void HideTooltip()
    {
        NodeTooltip.Visibility = Visibility.Collapsed;
    }

    // =================================================================
    // COORDINATE TRANSFORMATION
    // =================================================================

    /// <summary>
    /// Calculates the scale and offset needed to fit all visible nodes
    /// into the canvas with padding, preserving aspect ratio.
    /// Returns (offsetX, offsetY, scale).
    /// </summary>
    private static (double offsetX, double offsetY, double scale) CalculateTransform(
        List<GraphNode> nodes, double canvasWidth, double canvasHeight)
    {
        if (nodes.Count == 0)
            return (canvasWidth / 2, canvasHeight / 2, 1.0);

        var minX = nodes.Min(n => n.X);
        var maxX = nodes.Max(n => n.X);
        var minY = nodes.Min(n => n.Y);
        var maxY = nodes.Max(n => n.Y);

        var graphWidth = maxX - minX;
        var graphHeight = maxY - minY;

        // Handle degenerate cases (all nodes at same position)
        if (graphWidth < 1.0) graphWidth = 1.0;
        if (graphHeight < 1.0) graphHeight = 1.0;

        var availableWidth = canvasWidth - CanvasPadding * 2;
        var availableHeight = canvasHeight - CanvasPadding * 2;

        var scaleX = availableWidth / graphWidth;
        var scaleY = availableHeight / graphHeight;
        var scale = Math.Min(scaleX, scaleY);

        // Center the graph in the canvas
        var centerX = (minX + maxX) / 2.0;
        var centerY = (minY + maxY) / 2.0;

        var offsetX = canvasWidth / 2.0 - centerX * scale;
        var offsetY = canvasHeight / 2.0 - centerY * scale;

        return (offsetX, offsetY, scale);
    }

    // =================================================================
    // HELPERS
    // =================================================================

    /// <summary>
    /// Determines whether a node should be visible based on the current filter toggles.
    /// </summary>
    private bool IsNodeVisible(GraphNode node)
    {
        return node.NodeType switch
        {
            GraphNodeType.Document => ViewModel.ShowDocuments,
            GraphNodeType.Collection => ViewModel.ShowCollections,
            GraphNodeType.Tag => ViewModel.ShowTags,
            _ => true,
        };
    }

    /// <summary>
    /// Truncates a string to the specified maximum length, appending
    /// an ellipsis if truncation occurs.
    /// </summary>
    private static string TruncateLabel(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        return string.Concat(text.AsSpan(0, maxLength - 1), "...");
    }

    /// <summary>
    /// Parses a hex color string (e.g., "#3B82F6") into a <see cref="Color"/>.
    /// Falls back to gray if parsing fails.
    /// </summary>
    private static Color ParseColor(string hex)
    {
        try
        {
            if (string.IsNullOrEmpty(hex))
                return Color.FromArgb(255, 107, 114, 128); // Gray fallback

            hex = hex.TrimStart('#');

            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return Color.FromArgb(255, r, g, b);
            }

            if (hex.Length == 8)
            {
                var a = Convert.ToByte(hex[..2], 16);
                var r = Convert.ToByte(hex[2..4], 16);
                var g = Convert.ToByte(hex[4..6], 16);
                var b = Convert.ToByte(hex[6..8], 16);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch
        {
            // Silently fall back to gray on any parse error
        }

        return Color.FromArgb(255, 107, 114, 128);
    }

    /// <summary>
    /// Determines whether the empty state overlay should be shown.
    /// Visible only when: not loading and graph data is null or has no nodes.
    /// </summary>
    public Visibility ShowEmptyState(bool isLoading, KnowledgeGraphData? graphData)
    {
        if (isLoading)
            return Visibility.Collapsed;

        if (graphData == null || graphData.Nodes.Count == 0)
            return Visibility.Visible;

        return Visibility.Collapsed;
    }

    // =================================================================
    // STATIC HELPERS for x:Bind in DataTemplate
    // =================================================================

    /// <summary>
    /// Formats the zoom level for display (e.g., "1.0x").
    /// </summary>
    public static string FormatZoom(double zoomLevel) => $"{zoomLevel:F1}x";

    /// <summary>
    /// Formats the search match count for display (e.g., "3 found").
    /// </summary>
    public static string FormatMatchCount(int count) => $"{count} found";
}
