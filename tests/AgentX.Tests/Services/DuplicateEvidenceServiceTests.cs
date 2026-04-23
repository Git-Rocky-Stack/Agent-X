using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Intelligence;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class DuplicateEvidenceServiceTests
{
    [Fact]
    public void BuildEvidence_GroupsResultsByDocumentAndRanksByConfidence()
    {
        var sut = new DuplicateEvidenceService(Log.ForContext<DuplicateEvidenceService>());

        var results = new List<VectorSearchResult>
        {
            new() { ChunkId = 10, Distance = 0.05 },
            new() { ChunkId = 11, Distance = 0.10 },
            new() { ChunkId = 20, Distance = 0.18 }
        };

        var chunkToDocument = new Dictionary<long, long>
        {
            [10] = 2,
            [11] = 2,
            [20] = 3
        };

        var evidence = sut.BuildEvidence(results, chunkToDocument);

        evidence.Should().HaveCount(2);
        evidence[0].DocumentId.Should().Be(2);
        evidence[0].SupportingChunkCount.Should().Be(2);
        evidence[0].Confidence.Should().BeGreaterThan(evidence[1].Confidence);
    }
}
