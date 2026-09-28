using System.Text;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Mathematics;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Caching wrapper for embedding generation that deduplicates identical queries.
/// Entries are keyed by the inner service's <see cref="IEmbeddingService.ModelVersion"/> plus a
/// hash of the normalized text, so a vector produced by one provider, model or vector size is
/// never returned after the embedding model changes. The cache is bounded (least recently used
/// entries are evicted) and entries of a previous model version are dropped as soon as the
/// version changes.
/// </summary>
public sealed class CachedEmbeddingService : IEmbeddingService
{
    /// <summary>Default maximum number of cached vectors.</summary>
    public const int DefaultMaxEntries = 2048;

    private readonly IEmbeddingService _inner;
    private readonly IRagConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly int _maxEntries;

    // LRU: most recently used entries at the front of the list.
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache;
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly object _lock = new();
    private string? _lastModelVersion;

    // Cache statistics (for monitoring/diagnostics)
    private long _cacheHits;
    private long _cacheMisses;
    private long _totalRequests;

    public CachedEmbeddingService(
        IEmbeddingService inner,
        IRagConfiguration configuration,
        ILogger logger)
        : this(inner, configuration, logger, DefaultMaxEntries)
    {
    }

    /// <summary>Creates the cache with an explicit size bound.</summary>
    public CachedEmbeddingService(
        IEmbeddingService inner,
        IRagConfiguration configuration,
        ILogger logger,
        int maxEntries)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxEntries = maxEntries > 0 ? maxEntries : throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _cache = new Dictionary<string, LinkedListNode<CacheEntry>>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public int Dimensions => _inner.Dimensions;

    /// <inheritdoc />
    public string ModelName => _inner.ModelName;

    /// <inheritdoc />
    public string ModelVersion => _inner.ModelVersion;

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text to embed cannot be null or empty.", nameof(text));

        Interlocked.Increment(ref _totalRequests);

        // Normalize text for cache key (remove excess whitespace)
        var normalizedText = NormalizeForCache(text);
        var textHash = ComputeTextHash(normalizedText);
        var version = CurrentModelVersion();

        if (TryGet(version, textHash, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            _logger.Debug("Embedding cache hit");
            return cached;
        }

        Interlocked.Increment(ref _cacheMisses);

        // Cache miss - generate embedding
        _logger.Debug("Embedding cache miss; generating embedding");
        var embedding = await _inner.EmbedAsync(normalizedText, ct).ConfigureAwait(false);

        // Stored under the version read after the call: the inner service may only learn the
        // real vector size from this first embedding.
        Store(CurrentModelVersion(), textHash, embedding);
        return embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var textList = texts as IList<string> ?? texts.ToList();

        if (textList.Count == 0)
            return Array.Empty<float[]>();

        _logger.Information("CachedEmbeddingService: Batch embedding {Count} texts", textList.Count);

        // For batches, we cache each text individually
        // This means the batch optimization of the inner service is still utilized
        var results = new List<float[]>(textList.Count);
        var cacheMisses = new List<(int Index, string Text, string Hash)>();
        var version = CurrentModelVersion();

        // First pass: check cache for each text
        for (int i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results.Add(Array.Empty<float>());
                continue;
            }

            // Every looked-up text counts as a request, so the hit rate stays within [0, 1].
            Interlocked.Increment(ref _totalRequests);

            var normalizedText = NormalizeForCache(text);
            var textHash = ComputeTextHash(normalizedText);

            if (TryGet(version, textHash, out var cached))
            {
                results.Add(cached);
                Interlocked.Increment(ref _cacheHits);
            }
            else
            {
                cacheMisses.Add((i, normalizedText, textHash));
                Interlocked.Increment(ref _cacheMisses);
            }
        }

        // Second pass: batch generate the cache misses
        if (cacheMisses.Count > 0)
        {
            var missedTexts = cacheMisses.Select(x => x.Text).ToList();
            var batchResults = await _inner.EmbedBatchAsync(missedTexts, ct).ConfigureAwait(false);
            var storeVersion = CurrentModelVersion();

            // Store results in cache and fill in the output list
            for (int i = 0; i < cacheMisses.Count; i++)
            {
                var (index, _, hash) = cacheMisses[i];
                var embedding = batchResults[i];

                Store(storeVersion, hash, embedding);

                // Place in correct position in results
                results.Insert(index, embedding);
            }
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Clears all cached embeddings.
    /// Useful after model changes or when memory pressure is high.
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            var count = _cache.Count;
            _cache.Clear();
            _lru.Clear();
            _logger.Information("Cleared {Count} entries from embedding cache", count);
        }
    }

    /// <summary>
    /// Removes all cached embeddings for a specific model version.
    /// Use this when upgrading to a new embedding model.
    /// </summary>
    public void ClearCacheForModel(string modelVersion)
    {
        if (string.IsNullOrWhiteSpace(modelVersion))
            return;

        lock (_lock)
        {
            var removed = RemoveWhere(entry => string.Equals(entry.ModelVersion, modelVersion, StringComparison.Ordinal));
            _logger.Information("Cleared {Count} entries from embedding cache for model {Model}",
                removed, modelVersion);
        }
    }

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    public (long Hits, long Misses, long Total, int CacheSize, double HitRate) GetStatistics()
    {
        lock (_lock)
        {
            var hits = Interlocked.Read(ref _cacheHits);
            var misses = Interlocked.Read(ref _cacheMisses);
            var total = Interlocked.Read(ref _totalRequests);
            var hitRate = total > 0 ? Math.Min(1.0, (double)hits / total) : 0.0;
            return (hits, misses, total, _cache.Count, hitRate);
        }
    }

    // ===================================================================
    //  Private helpers
    // ===================================================================

    /// <summary>
    /// Returns the current model version and, when it differs from the last one seen, drops the
    /// entries of every other version: they belong to a different embedding space.
    /// </summary>
    private string CurrentModelVersion()
    {
        var version = _inner.ModelVersion ?? string.Empty;

        lock (_lock)
        {
            if (!string.Equals(version, _lastModelVersion, StringComparison.Ordinal))
            {
                if (_lastModelVersion is not null)
                {
                    var removed = RemoveWhere(entry => !string.Equals(entry.ModelVersion, version, StringComparison.Ordinal));
                    _logger.Information(
                        "Embedding model changed from {Previous} to {Current}; dropped {Count} cached vectors",
                        _lastModelVersion, version, removed);
                }

                _lastModelVersion = version;
            }
        }

        return version;
    }

    private bool TryGet(string modelVersion, string textHash, out float[] embedding)
    {
        var key = ComposeKey(modelVersion, textHash);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                if (DateTime.UtcNow < node.Value.ExpiresAt)
                {
                    // Mark as most recently used.
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    embedding = node.Value.Embedding;
                    return true;
                }

                // Remove expired entry
                _lru.Remove(node);
                _cache.Remove(key);
            }
        }

        embedding = Array.Empty<float>();
        return false;
    }

    private void Store(string modelVersion, string textHash, float[] embedding)
    {
        var key = ComposeKey(modelVersion, textHash);
        var expiresAt = DateTime.UtcNow.AddMinutes(_configuration.EmbeddingCacheExpirationMinutes);

        lock (_lock)
        {
            // Double-check in case another thread already added it
            if (_cache.ContainsKey(key))
                return;

            var node = _lru.AddFirst(new CacheEntry(key, modelVersion, embedding, expiresAt));
            _cache[key] = node;

            if (_cache.Count > _maxEntries)
            {
                CleanupExpiredEntries();
                while (_cache.Count > _maxEntries && _lru.Last is { } oldest)
                {
                    _lru.RemoveLast();
                    _cache.Remove(oldest.Value.Key);
                }
            }
        }
    }

    /// <summary>
    /// Composes the cache key from the model version and the text hash. The version comes first
    /// so a vector is only ever found by the model that produced it.
    /// </summary>
    private static string ComposeKey(string modelVersion, string textHash) => $"{modelVersion}|{textHash}";

    /// <summary>
    /// Computes a stable hash of the normalized text.
    /// </summary>
    private static string ComputeTextHash(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Normalizes text for consistent cache keys.
    /// Collapses multiple whitespace characters into single spaces.
    /// </summary>
    private static string NormalizeForCache(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return string.Join(" ", text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Removes matching entries. Caller holds the lock.</summary>
    private int RemoveWhere(Func<CacheEntry, bool> predicate)
    {
        var removed = 0;
        var node = _lru.First;
        while (node is not null)
        {
            var next = node.Next;
            if (predicate(node.Value))
            {
                _lru.Remove(node);
                _cache.Remove(node.Value.Key);
                removed++;
            }

            node = next;
        }

        return removed;
    }

    /// <summary>
    /// Removes expired entries from the cache. Caller holds the lock.
    /// </summary>
    private void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var removed = RemoveWhere(entry => entry.ExpiresAt <= now);
        if (removed > 0)
        {
            _logger.Debug("Cleaned up {Count} expired cache entries", removed);
        }
    }

    /// <summary>
    /// Internal cache entry structure.
    /// </summary>
    private sealed record CacheEntry(string Key, string ModelVersion, float[] Embedding, DateTime ExpiresAt);
}
