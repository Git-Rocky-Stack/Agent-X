using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Builds a knowledge graph representation of document relationships,
/// collection clusters, and tag connections from the knowledge vault.
/// The resulting graph data includes force-directed layout positions
/// suitable for direct visualization.
/// </summary>
public interface IKnowledgeGraphService
{
    /// <summary>
    /// Loads all documents, collections, and tags from the database,
    /// constructs a node-edge graph of their relationships, and applies
    /// a force-directed layout algorithm to compute spatial positions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="KnowledgeGraphData"/> containing positioned nodes and weighted edges
    /// representing the full knowledge vault topology.
    /// </returns>
    Task<KnowledgeGraphData> BuildGraphAsync(CancellationToken ct = default);
}
