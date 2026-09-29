namespace AgentX.Core.Search.Models;

/// <summary>
/// Turns a calendar day picked in the UI into UTC bounds, for filtering timestamps that are
/// stored in UTC (such as <c>DocumentEntity.ImportedAt</c>). Comparing a stored UTC value
/// with the picked day's local midnight shifts the range by the UTC offset, and using that
/// midnight as an upper bound drops the whole chosen day.
/// </summary>
public static class LocalDayRange
{
    /// <summary>
    /// The UTC instant at which <paramref name="day"/> begins in <paramref name="timeZone"/>
    /// (the local time zone when null). Only the date part of <paramref name="day"/> is used.
    /// Use it as an inclusive lower bound.
    /// </summary>
    public static DateTime StartUtc(DateTime day, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);

        // Where clocks jump forward at midnight, the day begins at the first valid time.
        for (var minutes = 0; zone.IsInvalidTime(local) && minutes < 24 * 60; minutes++)
        {
            local = local.AddMinutes(1);
        }

        // Where midnight happens twice, the day begins at the earlier of the two instants,
        // the one with the larger UTC offset.
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    /// <summary>
    /// The last UTC instant (one tick before the next day starts) of <paramref name="day"/>
    /// in <paramref name="timeZone"/> (the local time zone when null). Use it as an inclusive
    /// upper bound so the whole chosen day is included.
    /// </summary>
    public static DateTime EndUtc(DateTime day, TimeZoneInfo? timeZone = null)
        => StartUtc(day.Date.AddDays(1), timeZone).AddTicks(-1);
}
