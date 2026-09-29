using AgentX.Core.AI.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class CostTrackerTests
{
    [Fact]
    public void Versioned_ids_are_priced_by_the_longest_matching_entry()
    {
        var tracker = InMemory();

        // gpt-4o-mini-2024-07-18 contains "gpt-4o" too; it must not be billed at gpt-4o rates.
        tracker.RecordUsage("gpt-4o-mini-2024-07-18", "openai", 1_000_000, 0);

        tracker.GetTotalCostUsd().Should().BeApproximately(0.15, 1e-9);
    }

    [Fact]
    public void Haiku_4_5_uses_its_own_price_not_the_3_5_price()
    {
        var tracker = InMemory();

        tracker.RecordUsage("claude-haiku-4-5-20251001", "anthropic", 1_000_000, 1_000_000);

        tracker.GetTotalCostUsd().Should().BeApproximately(1.0 + 5.0, 1e-9);
    }

    [Theory]
    [InlineData("claude-sonnet-5", 2.0, 10.0)]
    [InlineData("claude-opus-5-5", 4.0, 20.0)]
    [InlineData("claude-opus-5", 5.0, 25.0)]
    [InlineData("claude-opus-4-1-20250805", 15.0, 75.0)]
    [InlineData("claude-sonnet-4-5-20250929", 3.0, 15.0)]
    public void Current_Anthropic_models_are_priced_per_million_tokens(string model, double input, double output)
    {
        var tracker = InMemory();

        tracker.RecordUsage(model, "anthropic", 1_000_000, 1_000_000);

        tracker.GetTotalCostUsd().Should().BeApproximately(input + output, 1e-9);
    }

    [Fact]
    public void Prompt_cache_writes_and_reads_are_priced_separately()
    {
        var tracker = InMemory();

        // Sonnet 5: $2 input -> writes at 1.25x ($2.50/M), reads at 0.1x ($0.20/M).
        tracker.RecordUsage("claude-sonnet-5", "anthropic", 0, 0, cacheCreationInputTokens: 1_000_000, cacheReadInputTokens: 1_000_000);

        tracker.GetTotalCostUsd().Should().BeApproximately(2.5 + 0.2, 1e-9);
        tracker.GetTotalInputTokens().Should().Be(2_000_000);
    }

    [Theory]
    [InlineData("gpt-4o", "ollama")]
    [InlineData("llama3.2", "ollama")]
    [InlineData("llama-3.2-3b-instruct-q4_k_m.gguf", "local")]
    [InlineData("some-new-cloud-model", "openai")]
    public void Local_and_unknown_models_cost_nothing(string model, string provider)
    {
        var tracker = InMemory();

        tracker.RecordUsage(model, provider, 10_000, 10_000);

        tracker.GetTotalCostUsd().Should().Be(0);
        tracker.GetTotalOutputTokens().Should().Be(10_000, "token counts are still tracked");
    }

    /// <summary>A tracker that keeps usage in memory only, so a test never writes the real history.</summary>
    private static CostTracker InMemory() => new(historyFilePath: null);
}

/// <summary>
/// The cost tracker kept its usage only in memory, so the Cost Tracking totals in Settings went
/// back to zero at every restart. It now keeps a bounded history in a JSON file.
/// </summary>
public sealed class CostTrackerPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agentx-usage-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    public CostTrackerPersistenceTests() => Directory.CreateDirectory(_directory);

    private string HistoryPath => Path.Combine(_directory, CostTracker.HistoryFileName);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private CostTracker Open(int maxStoredRecords = CostTracker.MaxStoredRecords) =>
        new(HistoryPath, () => _now, maxStoredRecords);

    [Fact]
    public void The_totals_survive_a_restart()
    {
        var firstCall = _now;
        using (var first = Open())
        {
            first.RecordUsage("gpt-4o", "openai", 1_000_000, 100_000);                 // $2.50 + $1.00
            _now = _now.AddMinutes(1);
            first.RecordUsage("claude-sonnet-5", "anthropic", 0, 0,
                cacheCreationInputTokens: 1_000_000, cacheReadInputTokens: 0);         // $2.50
        }

        using var second = Open();

        second.GetTotalCostUsd().Should().BeApproximately(6.0, 1e-9);
        second.GetTotalInputTokens().Should().Be(2_000_000);
        second.GetTotalOutputTokens().Should().Be(100_000);
        second.GetCostForPeriod(firstCall.Date, firstCall.AddHours(1)).Should().BeApproximately(6.0, 1e-9);
        second.GetUsageHistory().Select(r => r.ModelId).Should().Equal("claude-sonnet-5", "gpt-4o");
        second.GetUsageHistory()[1].Should().BeEquivalentTo(
            new UsageRecord
            {
                ModelId = "gpt-4o",
                ProviderId = "openai",
                InputTokens = 1_000_000,
                OutputTokens = 100_000,
                EstimatedCostUsd = 3.5,
                Timestamp = firstCall,
            },
            options => options
                .Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-9))
                .WhenTypeIs<double>());
    }

    [Fact]
    public void New_usage_is_saved_without_waiting_for_shutdown()
    {
        var tracker = Open();

        tracker.RecordUsage("gpt-4o", "openai", 1_000_000, 0);
        tracker.SaveNow(); // what the save delay runs

        using var reopened = Open();
        reopened.GetTotalCostUsd().Should().BeApproximately(2.5, 1e-9);
        tracker.Dispose();
    }

    [Fact]
    public void Records_older_than_the_retention_window_are_dropped_but_still_counted_in_the_totals()
    {
        using (var tracker = Open())
        {
            tracker.RecordUsage("gpt-4o", "openai", 1_000_000, 0);   // $2.50, ages out
            _now = _now.AddDays(80);
            tracker.RecordUsage("gpt-4o", "openai", 0, 100_000);     // $1.00, kept
        }

        _now = _now.AddDays(20); // the first record is now 100 days old
        using (var tracker = Open())
        {
            tracker.GetUsageHistory().Should().ContainSingle().Which.OutputTokens.Should().Be(100_000);
            tracker.GetTotalCostUsd().Should().BeApproximately(3.5, 1e-9, "the all-time total keeps what dropped records cost");
            tracker.GetTotalInputTokens().Should().Be(1_000_000);
            tracker.GetCostForPeriod(_now.AddDays(-30), _now).Should().BeApproximately(1.0, 1e-9);
        }

        // The pruning is written back, and the carried totals with it.
        using var reopened = Open();
        reopened.GetUsageHistory().Should().ContainSingle();
        reopened.GetTotalCostUsd().Should().BeApproximately(3.5, 1e-9);
    }

    [Fact]
    public void At_most_the_record_cap_is_kept_the_newest_ones()
    {
        using (var tracker = Open(maxStoredRecords: 3))
        {
            for (var i = 1; i <= 5; i++)
            {
                _now = _now.AddMinutes(1);
                tracker.RecordUsage($"model-{i}", "ollama", i, 0);
            }
        }

        using var reopened = Open(maxStoredRecords: 3);
        reopened.GetUsageHistory().Select(r => r.ModelId).Should().Equal("model-5", "model-4", "model-3");
        reopened.GetTotalInputTokens().Should().Be(1 + 2 + 3 + 4 + 5);
    }

    [Fact]
    public void The_file_is_replaced_whole_and_no_temporary_file_is_left()
    {
        using (var tracker = Open())
        {
            tracker.RecordUsage("gpt-4o", "openai", 1_000, 0);
        }

        Directory.GetFiles(_directory).Select(Path.GetFileName).Should().Equal(CostTracker.HistoryFileName);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(HistoryPath));
        document.RootElement.GetProperty("version").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("records").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void A_corrupt_history_is_set_aside_and_a_new_one_starts()
    {
        File.WriteAllText(HistoryPath, "{ \"records\": [ { \"modelId\": ");

        using (var tracker = Open())
        {
            tracker.GetTotalCostUsd().Should().Be(0);
            tracker.RecordUsage("gpt-4o", "openai", 1_000_000, 0);
        }

        File.Exists(HistoryPath + ".corrupt").Should().BeTrue();
        using var reopened = Open();
        reopened.GetTotalCostUsd().Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void Token_totals_past_int_max_are_capped_instead_of_overflowing()
    {
        File.WriteAllText(HistoryPath,
            "{ \"version\": 1, \"carriedOver\": { \"estimatedCostUsd\": 0, \"inputTokens\": 3000000000, \"outputTokens\": 0 }, \"records\": [] }");

        using var tracker = Open();

        tracker.GetTotalInputTokens().Should().Be(int.MaxValue);
    }
}
