using System.Collections.Generic;

namespace StoryDataCollector;

// A tiny pending-date index keeps failed narrative writes recoverable without scanning all daily records.
internal sealed class CheckpointPointer
{
    public int SchemaVersion { get; set; } = 1;

    public List<DailyDate> PendingDates { get; set; } = new();
}
