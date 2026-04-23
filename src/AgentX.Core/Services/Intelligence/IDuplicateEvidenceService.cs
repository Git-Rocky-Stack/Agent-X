using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

public interface IDuplicateEvidenceService
{
    IReadOnlyList<DuplicateEvidence> BuildEvidence(
        IReadOnlyList<VectorSearchResult> searchResults,
        IReadOnlyDictionary<long, long> chunkToDocument);
}
