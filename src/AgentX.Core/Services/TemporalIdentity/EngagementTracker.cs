using AgentX.Core.Services.TemporalIdentity.Models;
using Serilog;

namespace AgentX.Core.Services.TemporalIdentity;

/// <summary>
/// Times how long one item stays open in a viewer (a conversation in Chat, a document in the
/// Vault preview) and reports it to <see cref="ITemporalIdentityService.RecordEngagementAsync"/>
/// when the viewer moves off it. Engagement had no caller before, so "what did I spend time on"
/// always came back empty.
/// </summary>
/// <remarks>
/// Not thread-safe: a viewer drives it from its UI thread. A recording failure is logged and
/// swallowed, because losing an engagement sample must never break the viewer.
/// </remarks>
public sealed class EngagementTracker
{
    /// <summary>
    /// The most one view can count for. An item left open while the operator is away (an app
    /// left running overnight) would otherwise outweigh everything they actually read.
    /// </summary>
    public static readonly TimeSpan MaxViewDuration = TimeSpan.FromMinutes(30);

    private readonly ITemporalIdentityService _temporalIdentity;
    private readonly EngagementTargetType _targetType;
    private readonly Func<DateTime> _utcNow;
    private long? _openTargetId;
    private DateTime _openedAtUtc;

    public EngagementTracker(
        ITemporalIdentityService temporalIdentity,
        EngagementTargetType targetType,
        Func<DateTime>? utcNow = null)
    {
        _temporalIdentity = temporalIdentity ?? throw new ArgumentNullException(nameof(temporalIdentity));
        _targetType = targetType;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The item being timed, or null when the viewer shows nothing.</summary>
    public long? OpenTargetId => _openTargetId;

    /// <summary>
    /// Starts timing <paramref name="targetId"/>, first recording the item that was open before.
    /// Opening the item already open keeps its running time.
    /// </summary>
    /// <returns>The recording of the previous item, for callers (and tests) that await it.</returns>
    public Task OpenAsync(long targetId)
    {
        if (_openTargetId == targetId)
        {
            return Task.CompletedTask;
        }

        var recording = CloseAsync();
        _openTargetId = targetId;
        _openedAtUtc = _utcNow();
        return recording;
    }

    /// <summary>
    /// Stops timing and records the whole seconds the open item was shown, capped at
    /// <see cref="MaxViewDuration"/>. A view shorter than a second (flicking past an item) is
    /// not engagement and records nothing.
    /// </summary>
    public Task CloseAsync()
    {
        if (_openTargetId is not long targetId)
        {
            return Task.CompletedTask;
        }

        _openTargetId = null;
        var elapsed = _utcNow() - _openedAtUtc;
        if (elapsed > MaxViewDuration)
        {
            elapsed = MaxViewDuration;
        }

        var seconds = (int)elapsed.TotalSeconds;
        return seconds < 1 ? Task.CompletedTask : RecordAsync(targetId, seconds);
    }

    /// <summary>Stops timing without recording, for an item that no longer exists.</summary>
    public void Discard() => _openTargetId = null;

    private async Task RecordAsync(long targetId, int seconds)
    {
        try
        {
            await _temporalIdentity.RecordEngagementAsync(_targetType, targetId, seconds);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to record {Seconds}s of engagement with {TargetType} {TargetId}",
                seconds, _targetType, targetId);
        }
    }
}
