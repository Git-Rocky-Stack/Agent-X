using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgentX.Core.Search.Models;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Thread-safe, in-memory LRU (Least Recently Used) cache for search results.
///
/// <para>
/// Internally maintains a <see cref="LinkedList{T}"/> of cache entries ordered by access
/// recency (most-recently-used at the head) and a <see cref="Dictionary{TKey,TValue}"/>
/// mapping cache keys to their linked-list nodes, giving O(1) lookup, insertion, and eviction.
/// </para>
///
/// <para>
/// Each entry has a configurable time-to-live (TTL). Expired entries are treated as misses
/// and are lazily evicted on access. When the cache reaches its maximum capacity, the
/// least recently used (tail) entry is evicted to make room for new entries.
/// </para>
///
/// <para>
/// Thread safety is provided by a <see cref="ReaderWriterLockSlim"/>, allowing concurrent
/// reads while serializing writes.
/// </para>
/// </summary>
public sealed class SearchCacheService : ISearchCacheService, IDisposable
{
    // ═══════════════════════════════════════════════════════════════════
    //  Constants & defaults
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default maximum number of entries the cache can hold.</summary>
    private const int DefaultMaxEntries = 100;

    /// <summary>Default time-to-live for each cache entry.</summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    // ═══════════════════════════════════════════════════════════════════
    //  Internal data structures
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a single cached search result set, including metadata used
    /// for TTL expiration and LRU ordering.
    /// </summary>
    private sealed class CacheEntry
    {
        /// <summary>The deterministic cache key derived from the <see cref="SearchQuery"/>.</summary>
        public required string Key { get; init; }

        /// <summary>The cached search results.</summary>
        public required IReadOnlyList<SearchResult> Results { get; init; }

        /// <summary>UTC timestamp when this entry was created (used for TTL checks).</summary>
        public required DateTime CreatedAtUtc { get; init; }

        /// <summary>UTC timestamp when this entry was last accessed (updated on cache hits).</summary>
        public DateTime LastAccessedAtUtc { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Fields
    // ═══════════════════════════════════════════════════════════════════

    private readonly int _maxEntries;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Doubly-linked list ordered by access recency. The head is the most recently
    /// used entry; the tail is the least recently used and the eviction candidate.
    /// </summary>
    private readonly LinkedList<CacheEntry> _lruList = new();

    /// <summary>
    /// Maps cache keys to their corresponding linked-list nodes for O(1) lookup.
    /// </summary>
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheMap = new();

    /// <summary>
    /// Reader/writer lock allowing concurrent reads and exclusive writes.
    /// </summary>
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    // Statistics (using volatile + Interlocked for lock-free reads/writes)
    private int _hitCount;
    private int _missCount;
    private bool _disposed;

    // ═══════════════════════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initializes a new instance of <see cref="SearchCacheService"/> with configurable
    /// capacity and TTL.
    /// </summary>
    /// <param name="maxEntries">
    /// Maximum number of cache entries before LRU eviction occurs. Must be at least 1.
    /// Defaults to <c>100</c>.
    /// </param>
    /// <param name="ttl">
    /// Time-to-live for each cache entry. Entries older than this are treated as expired.
    /// Defaults to <c>5 minutes</c>. Must be a positive duration.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="maxEntries"/> is less than 1 or <paramref name="ttl"/> is not positive.
    /// </exception>
    public SearchCacheService(int maxEntries = DefaultMaxEntries, TimeSpan? ttl = null)
    {
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries,
                "Maximum cache entries must be at least 1.");
        }

        var resolvedTtl = ttl ?? DefaultTtl;
        if (resolvedTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), resolvedTtl,
                "TTL must be a positive duration.");
        }

        _maxEntries = maxEntries;
        _ttl = resolvedTtl;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ISearchCacheService implementation
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public IReadOnlyList<SearchResult>? TryGetCached(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();

        var key = GenerateCacheKey(query);

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (!_cacheMap.TryGetValue(key, out var node))
            {
                Interlocked.Increment(ref _missCount);
                return null;
            }

            var entry = node.Value;

            // Check TTL expiration
            if (IsExpired(entry))
            {
                // Entry has expired — evict it
                _lock.EnterWriteLock();
                try
                {
                    EvictNode(node);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                Interlocked.Increment(ref _missCount);
                return null;
            }

            // Cache hit: promote this entry to the head of the LRU list
            _lock.EnterWriteLock();
            try
            {
                entry.LastAccessedAtUtc = DateTime.UtcNow;
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            Interlocked.Increment(ref _hitCount);
            return entry.Results;
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <inheritdoc />
    public void Cache(SearchQuery query, IReadOnlyList<SearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(results);
        ThrowIfDisposed();

        var key = GenerateCacheKey(query);
        var now = DateTime.UtcNow;

        var entry = new CacheEntry
        {
            Key = key,
            Results = results,
            CreatedAtUtc = now,
            LastAccessedAtUtc = now
        };

        _lock.EnterWriteLock();
        try
        {
            // If this key already exists, remove the old entry first
            if (_cacheMap.TryGetValue(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _cacheMap.Remove(key);
            }

            // Evict least recently used entries until we have room
            while (_cacheMap.Count >= _maxEntries && _lruList.Last is not null)
            {
                EvictNode(_lruList.Last);
            }

            // Insert the new entry at the head (most recently used)
            var newNode = _lruList.AddFirst(entry);
            _cacheMap[key] = newNode;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        ThrowIfDisposed();

        _lock.EnterWriteLock();
        try
        {
            _lruList.Clear();
            _cacheMap.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void InvalidateForDocument(long documentId)
    {
        ThrowIfDisposed();

        _lock.EnterWriteLock();
        try
        {
            // Collect nodes to remove (cannot modify the list while iterating)
            var nodesToRemove = new List<LinkedListNode<CacheEntry>>();
            var currentNode = _lruList.First;

            while (currentNode is not null)
            {
                var entry = currentNode.Value;
                bool referencesDocument = entry.Results.Any(r => r.DocumentId == documentId);

                if (referencesDocument)
                {
                    nodesToRemove.Add(currentNode);
                }

                currentNode = currentNode.Next;
            }

            foreach (var node in nodesToRemove)
            {
                EvictNode(node);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public CacheStatistics GetStatistics()
    {
        ThrowIfDisposed();

        int entryCount;
        _lock.EnterReadLock();
        try
        {
            entryCount = _cacheMap.Count;
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var hits = Volatile.Read(ref _hitCount);
        var misses = Volatile.Read(ref _missCount);
        var total = hits + misses;
        var hitRate = total > 0 ? (double)hits / total : 0.0;

        return new CacheStatistics(entryCount, hits, misses, hitRate);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cache key generation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a deterministic cache key from a <see cref="SearchQuery"/> by normalizing
    /// all relevant properties into a canonical string and then computing a SHA-256 hash.
    ///
    /// <para>
    /// Normalization rules:
    /// <list type="bullet">
    ///   <item><c>QueryText</c> is trimmed and lowercased (using <see cref="CultureInfo.InvariantCulture"/>).</item>
    ///   <item>Nullable fields use a sentinel value (<c>"_null_"</c>) when absent.</item>
    ///   <item><c>DateTime</c> values are formatted using round-trip ("O") format for precision.</item>
    ///   <item>All fields are concatenated with a pipe delimiter to prevent ambiguity.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static string GenerateCacheKey(SearchQuery query)
    {
        var normalizedText = (query.QueryText ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture);
        var fileType = (query.FileTypeFilter ?? "_null_").Trim().ToLower(CultureInfo.InvariantCulture);
        var collectionId = query.CollectionId?.ToString(CultureInfo.InvariantCulture) ?? "_null_";
        var createdAfter = query.CreatedAfter?.ToString("O", CultureInfo.InvariantCulture) ?? "_null_";
        var createdBefore = query.CreatedBefore?.ToString("O", CultureInfo.InvariantCulture) ?? "_null_";

        var canonical = string.Join('|',
            normalizedText,
            query.TopK.ToString(CultureInfo.InvariantCulture),
            query.MinScore.ToString("F4", CultureInfo.InvariantCulture),
            collectionId,
            fileType,
            createdAfter,
            createdBefore,
            query.Mode.ToString());

        // Compute SHA-256 hash and return as a hex string for a compact, fixed-length key
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Internal helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether a cache entry has exceeded its TTL.
    /// </summary>
    private bool IsExpired(CacheEntry entry)
    {
        return (DateTime.UtcNow - entry.CreatedAtUtc) > _ttl;
    }

    /// <summary>
    /// Removes a node from both the LRU linked list and the lookup dictionary.
    /// Must be called while holding the write lock.
    /// </summary>
    private void EvictNode(LinkedListNode<CacheEntry> node)
    {
        _cacheMap.Remove(node.Value.Key);
        _lruList.Remove(node);
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if this instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IDisposable
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Releases the <see cref="ReaderWriterLockSlim"/> and clears all cached data.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lruList.Clear();
        _cacheMap.Clear();
        _lock.Dispose();
    }
}
