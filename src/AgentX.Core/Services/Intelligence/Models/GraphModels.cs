namespace AgentX.Core.Services.Intelligence.Models;

/// <summary>
/// Classifies the type of node in the knowledge graph.
/// Each type is rendered with a distinct color and size.
/// </summary>
public enum GraphNodeType
{
    /// <summary>An indexed document in the knowledge vault.</summary>
    Document,

    /// <summary>A user-defined or auto-created collection (folder).</summary>
    Collection,

    /// <summary>A tag applied to one or more documents.</summary>
    Tag
}

/// <summary>
/// Represents a single node in the knowledge graph visualization.
/// Contains both semantic metadata (label, type) and spatial layout
/// properties (X, Y, velocity) used by the force-directed algorithm.
/// </summary>
public class GraphNode
{
    /// <summary>
    /// Unique identifier for this node, prefixed by type (e.g., "doc-42", "col-7", "tag-3").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label displayed on the graph.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The semantic type of this node, determining its visual treatment.
    /// </summary>
    public GraphNodeType NodeType { get; set; }

    /// <summary>
    /// Hex color code for rendering this node (e.g., "#3B82F6" for blue).
    /// </summary>
    public string ColorHex { get; set; } = "#6B7280";

    /// <summary>
    /// Diameter of the node circle in logical pixels. Scaled by connection count or importance.
    /// </summary>
    public double Size { get; set; } = 20;

    /// <summary>
    /// Horizontal position after force-directed layout (graph-space coordinates).
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Vertical position after force-directed layout (graph-space coordinates).
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Horizontal velocity component used during force simulation.
    /// </summary>
    public double Vx { get; set; }

    /// <summary>
    /// Vertical velocity component used during force simulation.
    /// </summary>
    public double Vy { get; set; }

    /// <summary>
    /// The database primary key of the underlying entity (document, collection, or tag).
    /// </summary>
    public long EntityId { get; set; }

    /// <summary>
    /// Optional secondary label (e.g., file type for documents, document count for collections).
    /// </summary>
    public string? Subtitle { get; set; }

    /// <summary>
    /// Number of edges connected to this node. Used for sizing and detail display.
    /// </summary>
    public int ConnectionCount { get; set; }
}

/// <summary>
/// Represents a directed or undirected edge (connection) between two graph nodes.
/// </summary>
public class GraphEdge
{
    /// <summary>
    /// The <see cref="GraphNode.Id"/> of the source (origin) node.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// The <see cref="GraphNode.Id"/> of the target (destination) node.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Optional label describing the relationship (e.g., "in collection", "tagged").
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Edge weight influencing attraction strength. Higher values bring connected nodes closer.
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// Hex color code for rendering this edge line.
    /// </summary>
    public string ColorHex { get; set; } = "#374151";
}

/// <summary>
/// Complete knowledge graph data payload containing all nodes, edges, and summary statistics.
/// Returned by <see cref="IKnowledgeGraphService.BuildGraphAsync"/> with layout positions
/// already computed.
/// </summary>
public class KnowledgeGraphData
{
    /// <summary>
    /// All nodes in the graph (documents, collections, tags).
    /// </summary>
    public List<GraphNode> Nodes { get; set; } = new();

    /// <summary>
    /// All edges in the graph (relationships between nodes).
    /// </summary>
    public List<GraphEdge> Edges { get; set; } = new();

    /// <summary>
    /// Total number of document nodes in the graph.
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// Total number of collection nodes in the graph.
    /// </summary>
    public int CollectionCount { get; set; }

    /// <summary>
    /// Total number of tag nodes in the graph.
    /// </summary>
    public int TagCount { get; set; }
}
