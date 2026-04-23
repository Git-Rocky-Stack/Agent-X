using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class DuplicateDetectionServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<DuplicateDetectionServiceTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task FindDuplicatesAsync_marks_groups_as_exact()
    {
        using var db = _dbFactory.CreateContext();
        await using var vectorStore = new StubVectorStore();

        db.Documents.AddRange(
            CreateDocument("alpha-v1.txt", "hash-alpha", new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc)),
            CreateDocument("alpha-copy.txt", "hash-alpha", new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var sut = new DuplicateDetectionService(db, vectorStore, _logger);

        var groups = await sut.FindDuplicatesAsync();

        groups.Should().ContainSingle();
        groups[0].MatchKind.Should().Be(DuplicateMatchKind.Exact);
        groups[0].Documents.Should().HaveCount(2);
        groups[0].Documents.All(document => document.Evidence is null).Should().BeTrue();
    }

    [Fact]
    public async Task FindNearDuplicatesAsync_attaches_semantic_evidence_to_matching_documents()
    {
        using var db = _dbFactory.CreateContext();

        var referenceDocument = CreateDocument(
            "draft-v1.md",
            "hash-draft-v1",
            new DateTime(2026, 4, 10, 8, 0, 0, DateTimeKind.Utc));
        var revisedDocument = CreateDocument(
            "draft-v2.md",
            "hash-draft-v2",
            new DateTime(2026, 4, 11, 8, 0, 0, DateTimeKind.Utc));

        db.Documents.AddRange(referenceDocument, revisedDocument);
        await db.SaveChangesAsync();

        var referenceChunk = new DocumentChunkEntity
        {
            DocumentId = referenceDocument.Id,
            ChunkIndex = 0,
            Content = "Reference content",
            StartCharOffset = 0,
            EndCharOffset = 17,
            TokenCount = 8,
            IsEmbedded = true,
            VectorRowId = 101,
        };
        var revisedChunk = new DocumentChunkEntity
        {
            DocumentId = revisedDocument.Id,
            ChunkIndex = 0,
            Content = "Revised content",
            StartCharOffset = 0,
            EndCharOffset = 15,
            TokenCount = 8,
            IsEmbedded = true,
            VectorRowId = 102,
        };

        db.DocumentChunks.AddRange(referenceChunk, revisedChunk);
        await db.SaveChangesAsync();

        await InsertEmbeddingAsync(db, referenceChunk.Id, [1f, 0f, 0f]);
        await InsertEmbeddingAsync(db, revisedChunk.Id, [0.95f, 0.05f, 0f]);

        await using var vectorStore = new StubVectorStore(
        [
            new VectorSearchResult { ChunkId = referenceChunk.Id, Distance = 0.0 },
            new VectorSearchResult { ChunkId = revisedChunk.Id, Distance = 0.05 },
        ]);

        var sut = new DuplicateDetectionService(db, vectorStore, _logger);

        var groups = await sut.FindNearDuplicatesAsync(0.9f);

        groups.Should().ContainSingle();

        var group = groups[0];
        group.MatchKind.Should().Be(DuplicateMatchKind.Semantic);
        group.Documents.Should().HaveCount(2);

        var referenceResult = group.Documents.Single(document => document.DocumentId == referenceDocument.Id);
        var revisedResult = group.Documents.Single(document => document.DocumentId == revisedDocument.Id);

        referenceResult.Evidence.Should().BeNull();
        revisedResult.Evidence.Should().NotBeNull();
        revisedResult.Evidence!.SupportingChunkCount.Should().Be(1);
        revisedResult.Evidence.MaxSimilarity.Should().BeApproximately(0.95, 0.0001);
        revisedResult.Evidence.AverageSimilarity.Should().BeApproximately(0.95, 0.0001);
        revisedResult.Evidence.Confidence.Should().BeApproximately(0.955, 0.0001);
    }

    private static DocumentEntity CreateDocument(string fileName, string contentHash, DateTime importedAt)
    {
        return new DocumentEntity
        {
            FileName = fileName,
            FilePath = $@"C:\docs\{fileName}",
            FileType = Path.GetExtension(fileName).TrimStart('.'),
            FileSizeBytes = 4096,
            ContentHash = contentHash,
            ImportedAt = importedAt,
            FileModifiedAt = importedAt,
            IndexingStatus = "completed",
        };
    }

    private static async Task InsertEmbeddingAsync(AgentXDbContext db, long chunkId, float[] embedding)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS vec_embeddings (
                    chunk_id  INTEGER PRIMARY KEY,
                    embedding BLOB NOT NULL,
                    magnitude REAL NOT NULL
                );
                """;
            await create.ExecuteNonQueryAsync();
        }

        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        var magnitude = Math.Sqrt(embedding.Sum(value => value * value));

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT OR REPLACE INTO vec_embeddings (chunk_id, embedding, magnitude)
            VALUES (@chunkId, @embedding, @magnitude);
            """;

        var chunkIdParameter = insert.CreateParameter();
        chunkIdParameter.ParameterName = "@chunkId";
        chunkIdParameter.Value = chunkId;
        insert.Parameters.Add(chunkIdParameter);

        var embeddingParameter = insert.CreateParameter();
        embeddingParameter.ParameterName = "@embedding";
        embeddingParameter.Value = bytes;
        insert.Parameters.Add(embeddingParameter);

        var magnitudeParameter = insert.CreateParameter();
        magnitudeParameter.ParameterName = "@magnitude";
        magnitudeParameter.Value = magnitude;
        insert.Parameters.Add(magnitudeParameter);

        await insert.ExecuteNonQueryAsync();
    }

    private sealed class StubVectorStore : IVectorStore
    {
        private readonly IReadOnlyList<VectorSearchResult> _results;

        public StubVectorStore(IReadOnlyList<VectorSearchResult>? results = null)
        {
            _results = results ?? Array.Empty<VectorSearchResult>();
        }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default)
            => Task.FromResult(chunkId);

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK = 5,
            double minSimilarity = 0.3,
            CancellationToken ct = default)
        {
            var filtered = _results
                .Where(result => result.Similarity >= minSimilarity)
                .Take(topK)
                .ToList();

            return Task.FromResult((IReadOnlyList<VectorSearchResult>)filtered);
        }

        public Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteEmbeddingsForDocumentAsync(long documentId, IReadOnlyList<long> chunkIds, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<long> GetEmbeddingCountAsync(CancellationToken ct = default) => Task.FromResult(0L);

        public Task OptimizeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
