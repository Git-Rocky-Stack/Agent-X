using System.Collections.ObjectModel;

namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// The events one <see cref="ICalendarProvider.GetEventsAsync"/> call returned, together with
/// whether they are everything the calendar holds in the requested date range.
/// </summary>
/// <remarks>
/// A full read (no delta token, or one the provider rejected) lists every event in the range,
/// so an event the vault still holds for that range but the read did not list was deleted at
/// the source or moved out of the range. An incremental read lists only what changed since the
/// token and says nothing about the other events; its deletions arrive as
/// <see cref="CalEvent.IsDeleted"/> notices instead.
/// </remarks>
public sealed class CalendarEventBatch : ReadOnlyCollection<CalEvent>
{
    /// <summary>Creates a batch over <paramref name="events"/>.</summary>
    /// <param name="events">The events read.</param>
    /// <param name="isCompleteWindow">True when the read listed every event in the range.</param>
    public CalendarEventBatch(IList<CalEvent> events, bool isCompleteWindow)
        : base(events)
    {
        IsCompleteWindow = isCompleteWindow;
    }

    /// <summary>
    /// True when the batch is the complete contents of the requested date range (a full read),
    /// false when it holds only the changes since a delta token.
    /// </summary>
    public bool IsCompleteWindow { get; }
}
