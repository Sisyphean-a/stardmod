using System.Collections.Generic;

namespace StoryDataCollector;

internal sealed class DailyCheckpoint
{
    public int SchemaVersion { get; set; } = 1;

    public DailyDate Date { get; set; } = new();

    public DailyContext Context { get; set; } = new();

    public PlayerDayState? StartState { get; set; }

    public List<GameEvent> Events { get; set; } = new();

    // This is a bounded, exact recovery snapshot, not a second archival event stream.
    public List<LocationStay> LocationStays { get; set; } = new();

    public Dictionary<string, int> DroppedEventCounts { get; set; } = new();

    public int DroppedLocationStays { get; set; }

    public long LastSequence { get; set; }
}
