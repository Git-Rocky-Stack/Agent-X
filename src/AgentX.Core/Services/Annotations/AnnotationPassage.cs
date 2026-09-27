namespace AgentX.Core.Services.Annotations;

/// <summary>
/// One passage of a document's indexed text that highlights can be placed on: the content of
/// a single chunk, in reading order. An annotation made on it carries <see cref="ChunkId"/> and
/// offsets into <see cref="Text"/>, which is what <c>AnnotationEntity</c> documents for a
/// chunk-positioned highlight.
/// </summary>
/// <param name="ChunkId">The chunk the passage is.</param>
/// <param name="Position">Zero-based position of the passage among the document's passages.</param>
/// <param name="Count">How many passages the document has.</param>
/// <param name="PageNumber">The page the chunk came from, when the source has pages.</param>
/// <param name="Text">The chunk content, exactly as stored.</param>
public sealed record AnnotationPassage(
    long ChunkId,
    int Position,
    int Count,
    int? PageNumber,
    string Text);
