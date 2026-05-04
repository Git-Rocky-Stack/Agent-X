using System.Text;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Mathematics;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Caching wrapper for embedding generation that deduplicates identical queries.
/// Uses a hash-based key for cache lookup with configurable expiration.
/// </summary>
public sealed class CachedEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly IRagConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly Dictionary<string, CacheEntry> _cache;
    private readonly object _lock = new();

    // Cache statistics (for monitoring/diagnostics)
    private long _cacheHits;
    private long _cacheMisses;
    private long _totalRequests;

    public CachedEmbeddingService(
        IEmbeddingService inner,
        IRagConfiguration configuration,
        ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
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
        var cacheKey = ComputeCacheKey(normalizedText);

        // Check cache
        float[]? cached = null;
        bool isHit = false;

        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                // Check if entry hasn't expired
                if (DateTime.UtcNow < entry.ExpiresAt)
                {
                    cached = entry.Embedding;
                    isHit = true;
                    Interlocked.Increment(ref _cacheHits);
                }
                else
                {
                    // Remove expired entry
                    _cache.Remove(cacheKey);
                }
            }
        }

        if (isHit && cached is not null)
        {
            _logger.Debug("Cache HIT for text hash {HashLength} chars", cacheKey.Length);
            return cached;
        }

        Interlocked.Increment(ref _cacheMisses);

        // Cache miss - generate embedding
        _logger.Debug("Cache MISS for text hash {HashLength} chars; generating embedding", cacheKey.Length);
        var embedding = await _inner.EmbedAsync(normalizedText, ct).ConfigureAwait(false);

        // Store in cache
        var expiresAt = DateTime.UtcNow.AddMinutes(_configuration.EmbeddingCacheExpirationMinutes);

        lock (_lock)
        {
            // Double-check in case another thread already added it
            if (!_cache.ContainsKey(cacheKey))
            {
                _cache[cacheKey] = new CacheEntry
                {
                    Embedding = embedding,
                    ExpiresAt = expiresAt
                };
            }
        }

        // Periodic cleanup of expired entries (every 1000 requests)
        if (_totalRequests % 1000 == 0)
        {
            CleanupExpiredEntries();
        }

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
        var cacheMisses = new List<(int Index, string Text)>();

        // First pass: check cache for each text
        for (int i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results.Add(Array.Empty<float>());
                continue;
            }

            var normalizedText = NormalizeForCache(text);
            var cacheKey = ComputeCacheKey(normalizedText);

            bool found = false;
            lock (_lock)
            {
                if (_cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
                {
                    results.Add(entry.Embedding);
                    found = true;
                    Interlocked.Increment(ref _cacheHits);
                }
            }

            if (!found)
            {
                cacheMisses.Add((i, normalizedText));
                Interlocked.Increment(ref _cacheMisses);
            }
        }

        // Second pass: batch generate the cache misses
        if (cacheMisses.Count > 0)
        {
            var missedTexts = cacheMisses.Select(x => x.Text).ToList();
            var batchResults = await _inner.EmbedBatchAsync(missedTexts, ct).ConfigureAwait(false);

            // Store results in cache and fill in the output list
            for (int i = 0; i < cacheMisses.Count; i++)
            {
                var (index, text) = cacheMisses[i];
                var embedding = batchResults[i];

                // Store in cache
                var cacheKey = ComputeCacheKey(text);
                var expiresAt = DateTime.UtcNow.AddMinutes(_configuration.EmbeddingCacheExpirationMinutes);

                lock (_lock)
                {
                    if (!_cache.ContainsKey(cacheKey))
                    {
                        _cache[cacheKey] = new CacheEntry
                        {
                            Embedding = embedding,
                            ExpiresAt = expiresAt
                        };
                    }
                }

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
            var keysToRemove = _cache
                .Where(kvp => kvp.Key.StartsWith(modelVersion + ":", StringComparison.Ordinal))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }

            _logger.Information("Cleared {Count} entries from embedding cache for model {Model}",
                keysToRemove.Count, modelVersion);
        }
    }

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    public (long Hits, long Misses, long Total, int CacheSize, double HitRate) GetStatistics()
    {
        lock (_lock)
        {
            var hitRate = _totalRequests > 0 ? (double)_cacheHits / _totalRequests : 0.0;
            return (_cacheHits, _cacheMisses, _totalRequests, _cache.Count, hitRate);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes a cache key from text using a stable hash.
    /// Format: "{ModelName}:{Hash}" where Hash is a stable representation of the text content.
    /// </summary>
    private static string ComputeCacheKey(string text)
    {
        // Use a simple, fast hash for cache keys
        // SHA-256 would be more collision-resistant but slower
        var hash = new System.Security.Cryptography.SHA256Managed().ComputeHash(Encoding.UTF8.GetBytes(text));
        var hashBase64 = Convert.ToBase64String(hash).Substring(0, 16); // First 16 chars is enough
        return $"embedding:{hashBase64}";
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

    /// <summary>
    /// Removes expired entries from the cache.
    /// </summary>
    private void CleanupExpiredEntries()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var keysToRemove = _cache
                .Where(kvp => kvp.Value.ExpiresAt < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.Debug("Cleaned up {Count} expired cache entries", keysToRemove.Count);
            }
        }
    }

    /// <summary>
    /// Internal cache entry structure.
    /// </summary>
    private sealed class CacheEntry
    {
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public DateTime ExpiresAt { get; set; }
    }
}
