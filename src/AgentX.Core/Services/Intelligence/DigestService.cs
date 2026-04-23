using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Generates weekly digest reports by querying the knowledge vault database
/// for activity summaries including document imports, search trends,
/// collection usage, and conversation highlights.
/// </summary>
public sealed class DigestService : IDigestService
{
    private readonly AgentXDbContext _db;
    private readonly IDigestInsightService _digestInsightService;
    private readonly ILogger _logger;

    public DigestService(
        AgentXDbContext db,
        ILogger logger,
        IDigestInsightService? digestInsightService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _digestInsightService = digestInsightService ?? new DigestInsightService(db, logger);
        _logger = (logger ?? throw new ArgumentNullException(nameof(logger)))
            .ForContext<DigestService>();
    }

    /// <inheritdoc />
    public async Task<DigestReportEntity> GenerateDigestAsync(
        DateTime? periodStart = null,
        DateTime? periodEnd = null,
        CancellationToken ct = default)
    {
        var end = periodEnd ?? DateTime.UtcNow;
        var start = periodStart ?? end.AddDays(-7);

        _logger.Information("Generating digest report for {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}", start, end);

        // ── Count new documents in period ───────────────────────
        var newDocs = await _db.Documents
            .CountAsync(d => d.ImportedAt >= start && d.ImportedAt <= end, ct);

        // ── Count new conversations ─────────────────────────────
        var newConvos = await _db.Conversations
            .CountAsync(c => c.CreatedAt >= start && c.CreatedAt <= end, ct);

        // ── Total searches in period ────────────────────────────
        var totalSearches = await _db.SearchHistory
            .CountAsync(s => s.SearchedAt >= start && s.SearchedAt <= end, ct);

        // ── Tokens used in period (from messages) ───────────────
        var tokensUsed = await _db.Messages
            .Where(m => m.Timestamp >= start && m.Timestamp <= end && m.TokenCount > 0)
            .SumAsync(m => m.TokenCount, ct);

        // ── Period-over-period trend details ────────────────────
        var topSearches = await _digestInsightService.BuildSearchTrendsAsync(start, end, ct);
        var topCollections = await _digestInsightService.BuildCollectionTrendsAsync(start, end, ct);
        var fileTypes = await _digestInsightService.BuildFileTypeTrendsAsync(start, end, ct);

        // ── Storage delta ───────────────────────────────────────
        long storageDelta = 0;
        try
        {
            var storageTotal = await _db.Documents.SumAsync(d => d.FileSizeBytes, ct);
            var storageBeforePeriod = await _db.Documents
                .Where(d => d.ImportedAt < start)
                .SumAsync(d => d.FileSizeBytes, ct);
            storageDelta = storageTotal - storageBeforePeriod;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to compute storage delta for digest");
        }

        // ── Conversation highlights (most active) ───────────────
        var highlights = await _db.Conversations
            .Where(c => c.UpdatedAt >= start && c.UpdatedAt <= end)
            .OrderByDescending(c => c.MessageCount)
            .Take(3)
            .Select(c => new { c.Title, c.MessageCount, TokensUsed = (int)c.TokensUsed })
            .ToListAsync(ct);

        // ── Build and persist the report ────────────────────────
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var report = new DigestReportEntity
        {
            PeriodStart = start,
            PeriodEnd = end,
            GeneratedAt = DateTime.UtcNow,
            NewDocumentsCount = newDocs,
            NewConversationsCount = newConvos,
            TotalSearches = totalSearches,
            TotalTokensUsed = tokensUsed,
            StorageDeltaBytes = storageDelta,
            TopSearchesJson = JsonSerializer.Serialize(topSearches, jsonOptions),
            TopCollectionsJson = JsonSerializer.Serialize(topCollections, jsonOptions),
            FileTypeBreakdownJson = JsonSerializer.Serialize(fileTypes, jsonOptions),
            HighlightsJson = JsonSerializer.Serialize(highlights, jsonOptions),
            IsRead = false
        };

        _db.DigestReports.Add(report);
        await _db.SaveChangesAsync(ct);

        _logger.Information(
            "Digest report generated (ID {ReportId}): {NewDocs} docs, {NewConvos} conversations, {Searches} searches, {Tokens} tokens",
            report.Id, newDocs, newConvos, totalSearches, tokensUsed);

        return report;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DigestReportEntity>> GetReportHistoryAsync(
        int limit = 10, CancellationToken ct = default)
    {
        return await _db.DigestReports
            .OrderByDescending(d => d.GeneratedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<DigestReportEntity?> GetLatestReportAsync(CancellationToken ct = default)
    {
        return await _db.DigestReports
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task MarkAsReadAsync(long reportId, CancellationToken ct = default)
    {
        var report = await _db.DigestReports.FindAsync(new object[] { reportId }, ct);
        if (report is not null)
        {
            report.IsRead = true;
            await _db.SaveChangesAsync(ct);
            _logger.Debug("Digest report {ReportId} marked as read", reportId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasUnreadReportsAsync(CancellationToken ct = default)
    {
        return await _db.DigestReports.AnyAsync(d => !d.IsRead, ct);
    }
}
