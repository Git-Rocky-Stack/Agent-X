using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// ViewModel for the Knowledge Graph page. Manages graph data loading,
/// node selection, filter toggles, and summary statistics. The actual
/// canvas rendering is handled by the code-behind, which observes
/// <see cref="GraphData"/> changes via PropertyChanged.
/// </summary>
public partial class KnowledgeGraphViewModel : ObservableObject
{
    private readonly IKnowledgeGraphService _graphService;

    // ── Observable Properties ─────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private KnowledgeGraphData? _graphData;

    [ObservableProperty]
    private GraphNode? _selectedNode;

    [ObservableProperty]
    private int _documentCount;

    [ObservableProperty]
    private int _collectionCount;

    [ObservableProperty]
    private int _tagCount;

    [ObservableProperty]
    private int _edgeCount;

    [ObservableProperty]
    private string _statusMessage = "Loading graph...";

    [ObservableProperty]
    private bool _showDocuments = true;

    [ObservableProperty]
    private bool _showCollections = true;

    [ObservableProperty]
    private bool _showTags = true;

    // ── Search / Highlight ─────────────────────────────────────────

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _searchMatchCount;

    [ObservableProperty]
    private bool _hasSearchResults;

    // ── Zoom ───────────────────────────────────────────────────────

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    // ── Cluster Highlight ──────────────────────────────────────────

    [ObservableProperty]
    private string? _highlightedClusterId;

    [ObservableProperty]
    private bool _isClusterHighlighted;

    /// <summary>IDs of nodes that match the search or belong to the highlighted cluster.</summary>
    public HashSet<string> HighlightedNodeIds { get; } = new();

    public KnowledgeGraphViewModel(IKnowledgeGraphService graphService)
    {
        _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
    }

    /// <summary>
    /// Loads the knowledge graph data from the service and updates all
    /// summary statistics. Called by the page on Loaded.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Building knowledge graph...";

        try
        {
            GraphData = await _graphService.BuildGraphAsync().ConfigureAwait(false);

            DocumentCount = GraphData.DocumentCount;
            CollectionCount = GraphData.CollectionCount;
            TagCount = GraphData.TagCount;
            EdgeCount = GraphData.Edges.Count;
            StatusMessage = $"{GraphData.Nodes.Count} nodes, {GraphData.Edges.Count} connections";

            Log.Information(
                "Knowledge graph loaded: {Nodes} nodes, {Edges} edges",
                GraphData.Nodes.Count, GraphData.Edges.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to build knowledge graph");
            StatusMessage = "Failed to build graph";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the graph by rebuilding it from the current vault state.
    /// </summary>
    [RelayCommand]
    private async Task RefreshGraphAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Selects a node for detail display in the side panel.
    /// </summary>
    [RelayCommand]
    private void SelectNode(GraphNode? node)
    {
        SelectedNode = node;
    }

    /// <summary>
    /// Clears the current node selection, hiding the detail panel.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectedNode = null;
    }

    // =================================================================
    // SEARCH
    // =================================================================

    partial void OnSearchTextChanged(string value)
    {
        UpdateSearchHighlights(value);
    }

    private void UpdateSearchHighlights(string query)
    {
        HighlightedNodeIds.Clear();

        if (string.IsNullOrWhiteSpace(query) || GraphData is null)
        {
            SearchMatchCount = 0;
            HasSearchResults = false;
            OnPropertyChanged(nameof(HighlightedNodeIds));
            return;
        }

        var trimmed = query.Trim();
        foreach (var node in GraphData.Nodes)
        {
            if (node.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (node.Subtitle?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                HighlightedNodeIds.Add(node.Id);
            }
        }

        SearchMatchCount = HighlightedNodeIds.Count;
        HasSearchResults = HighlightedNodeIds.Count > 0;
        OnPropertyChanged(nameof(HighlightedNodeIds));
    }

    // =================================================================
    // ZOOM
    // =================================================================

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel + 0.25, 4.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel - 0.25, 0.25);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    // =================================================================
    // CLUSTER HIGHLIGHT
    // =================================================================

    [RelayCommand]
    private void HighlightCluster(GraphNode? node)
    {
        if (node is null || GraphData is null)
        {
            ClearClusterHighlight();
            return;
        }

        // Toggle off if clicking same cluster
        if (HighlightedClusterId == node.Id)
        {
            ClearClusterHighlight();
            return;
        }

        HighlightedNodeIds.Clear();
        HighlightedNodeIds.Add(node.Id);

        foreach (var edge in GraphData.Edges)
        {
            if (edge.SourceId == node.Id)
                HighlightedNodeIds.Add(edge.TargetId);
            else if (edge.TargetId == node.Id)
                HighlightedNodeIds.Add(edge.SourceId);
        }

        HighlightedClusterId = node.Id;
        IsClusterHighlighted = true;
        OnPropertyChanged(nameof(HighlightedNodeIds));
    }

    [RelayCommand]
    private void ClearClusterHighlight()
    {
        HighlightedNodeIds.Clear();
        HighlightedClusterId = null;
        IsClusterHighlighted = false;
        OnPropertyChanged(nameof(HighlightedNodeIds));
    }
}
