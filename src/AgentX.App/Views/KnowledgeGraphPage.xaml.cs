using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Intelligence.Models;
using Serilog;
using Windows.UI;

namespace AgentX.App.Views;

/// <summary>
/// Knowledge Graph visualization page. Renders a force-directed node graph
/// on a Canvas, showing document relationships, collection clusters, and
/// tag connections. Nodes are clickable for detail display in the sidebar.
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
        if (e.PropertyName == nameof(KnowledgeGraphViewModel.GraphData)
            || e.PropertyName == nameof(KnowledgeGraphViewModel.SelectedNode))
        {
            // Must dispatch to UI thread since ViewModel may raise from a background thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.PropertyName == nameof(KnowledgeGraphViewModel.GraphData))
                {
                    RenderGraph();
                }

                if (e.PropertyName == nameof(KnowledgeGraphViewModel.SelectedNode))
                {
                    UpdateDetailPanel();
                    UpdateDetailColumnWidth();
                }
            });
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
    /// current graph data. Applies coordinate normalization to fit the
    /// graph into the available canvas space with padding.
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

            var sx = sourceNode.X * scale + offsetX;
            var sy = sourceNode.Y * scale + offsetY;
            var tx = targetNode.X * scale + offsetX;
            var ty = targetNode.Y * scale + offsetY;

            var edgeColor = ParseColor(edge.ColorHex);
            var line = new Line
            {
                X1 = sx,
                Y1 = sy,
                X2 = tx,
                Y2 = ty,
                Stroke = new SolidColorBrush(edgeColor),
                StrokeThickness = Math.Clamp(edge.Weight * 0.8, 0.5, 3.0),
                Opacity = 0.3,
            };

            GraphCanvas.Children.Add(line);
        }

        // ── Draw nodes ───────────────────────────────────────────────
        var textBrush = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"];

        foreach (var node in visibleNodes)
        {
            var nx = node.X * scale + offsetX;
            var ny = node.Y * scale + offsetY;
            var nodeColor = ParseColor(node.ColorHex);

            // Draw the node circle
            var ellipse = new Ellipse
            {
                Width = node.Size,
                Height = node.Size,
                Fill = new SolidColorBrush(nodeColor),
                Opacity = 0.9,
            };

            // Highlight the selected node
            if (ViewModel.SelectedNode != null && ViewModel.SelectedNode.Id == node.Id)
            {
                ellipse.Opacity = 1.0;
                ellipse.StrokeThickness = 2;
                ellipse.Stroke = new SolidColorBrush(Colors.White);
            }

            Canvas.SetLeft(ellipse, nx - node.Size / 2);
            Canvas.SetTop(ellipse, ny - node.Size / 2);

            // Make the node clickable
            ellipse.Tag = node;
            ellipse.PointerPressed += Node_PointerPressed;
            ellipse.PointerEntered += Node_PointerEntered;
            ellipse.PointerExited += Node_PointerExited;

            GraphCanvas.Children.Add(ellipse);

            // Draw the label below the node
            var label = new TextBlock
            {
                Text = TruncateLabel(node.Label, MaxLabelLength),
                FontSize = 10,
                Foreground = textBrush,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 80,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Canvas.SetLeft(label, nx - 40);
            Canvas.SetTop(label, ny + node.Size / 2 + 3);
            GraphCanvas.Children.Add(label);
        }
    }

    // =================================================================
    // NODE INTERACTION HANDLERS
    // =================================================================

    /// <summary>
    /// Handles click/tap on a node ellipse to select it.
    /// </summary>
    private void Node_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Ellipse { Tag: GraphNode node })
        {
            ViewModel.SelectNodeCommand.Execute(node);
            e.Handled = true;

            // Re-render to update selection highlight
            RenderGraph();
        }
    }

    /// <summary>
    /// Provides hover feedback by increasing node opacity.
    /// </summary>
    private void Node_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Ellipse ellipse)
        {
            ellipse.Opacity = 1.0;
        }
    }

    /// <summary>
    /// Restores node opacity when pointer leaves.
    /// </summary>
    private void Node_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Ellipse ellipse)
        {
            // Keep full opacity if this is the selected node
            if (ellipse.Tag is GraphNode node &&
                ViewModel.SelectedNode != null &&
                ViewModel.SelectedNode.Id == node.Id)
            {
                return;
            }

            ellipse.Opacity = 0.9;
        }
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
}
