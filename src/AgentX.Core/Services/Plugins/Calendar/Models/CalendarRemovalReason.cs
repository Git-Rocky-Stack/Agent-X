namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// Why the calendar sync retires a stored event.
/// </summary>
public enum CalendarRemovalReason
{
    /// <summary>
    /// An incremental read reported the event (or this occurrence) as deleted or cancelled,
    /// which Google treats alike: the event is no longer shown.
    /// </summary>
    Deleted,

    /// <summary>
    /// A full read of the synced date range no longer listed it: it was deleted, or moved to a
    /// date outside the range.
    /// </summary>
    NoLongerListed,
}
