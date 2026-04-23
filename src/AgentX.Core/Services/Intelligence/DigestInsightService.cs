using AgentX.Core.Data;
using AgentX.Core.Services.Intelligence.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

public sealed class DigestInsightService : IDigestInsightService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _logger;

    public DigestInsightService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = (logger ?? throw new ArgumentNullException(nameof(logger)))
            .ForContext<DigestInsightService>();
    }

    public async Task<IReadOnlyList<DigestSearchTrend>> BuildSearchTrendsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default)
    {
        var (previousStart, previousEnd) = GetPreviousPeriod(periodStart, periodEnd);

        var current = await _db.SearchHistory
            .Where(s => s.SearchedAt >= periodStart && s.SearchedAt <= periodEnd)
            .GroupBy(s => s.Query)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var previous = await _db.SearchHistory
            .Where(s => s.SearchedAt >= previousStart && s.SearchedAt < previousEnd)
            .GroupBy(s => s.Query)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var trends = MergeCounts(
            current,
            previous,
            (query, count, previousCount) => new DigestSearchTrend
            {
                Query = query,
                Count = count,
                PreviousCount = previousCount
            });

        _logger.Debug("Built {Count} digest search trends", trends.Count);
        return trends;
    }

    public async Task<IReadOnlyList<DigestCollectionTrend>> BuildCollectionTrendsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default)
    {
        var (previousStart, previousEnd) = GetPreviousPeriod(periodStart, periodEnd);

        var current = await _db.DocumentCollections
            .Where(dc => dc.AddedAt >= periodStart && dc.AddedAt <= periodEnd)
            .GroupBy(dc => dc.Collection.Name)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var previous = await _db.DocumentCollections
            .Where(dc => dc.AddedAt >= previousStart && dc.AddedAt < previousEnd)
            .GroupBy(dc => dc.Collection.Name)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var trends = MergeCounts(
            current,
            previous,
            (name, count, previousCount) => new DigestCollectionTrend
            {
                Name = name,
                Count = count,
                PreviousCount = previousCount
            });

        _logger.Debug("Built {Count} digest collection trends", trends.Count);
        return trends;
    }

    public async Task<IReadOnlyList<DigestFileTypeTrend>> BuildFileTypeTrendsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default)
    {
        var (previousStart, previousEnd) = GetPreviousPeriod(periodStart, periodEnd);

        var current = await _db.Documents
            .Where(d => d.ImportedAt >= periodStart && d.ImportedAt <= periodEnd)
            .GroupBy(d => d.FileType)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var previous = await _db.Documents
            .Where(d => d.ImportedAt >= previousStart && d.ImportedAt < previousEnd)
            .GroupBy(d => d.FileType)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var trends = MergeCounts(
            current,
            previous,
            (type, count, previousCount) => new DigestFileTypeTrend
            {
                Type = type,
                Count = count,
                PreviousCount = previousCount
            });

        _logger.Debug("Built {Count} digest file type trends", trends.Count);
        return trends;
    }

    private static (DateTime previousStart, DateTime previousEnd) GetPreviousPeriod(DateTime periodStart, DateTime periodEnd)
    {
        var duration = periodEnd - periodStart;
        return (periodStart - duration, periodStart);
    }

    private static IReadOnlyList<TItem> MergeCounts<TItem>(
        IReadOnlyDictionary<string, int> current,
        IReadOnlyDictionary<string, int> previous,
        Func<string, int, int, TItem> projector)
        where TItem : DigestTrendItem
    {
        return current
            .Select(pair =>
            {
                previous.TryGetValue(pair.Key, out var previousCount);
                return projector(pair.Key, pair.Value, previousCount);
            })
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.DeltaCount)
            .Take(5)
            .ToList();
    }
}
