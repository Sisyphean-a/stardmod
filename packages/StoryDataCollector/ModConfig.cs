namespace StoryDataCollector;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;

    public bool DebugLogging { get; set; }

    public int CheckpointIntervalSeconds { get; set; } = 30;

    public int MaxEventsPerDay { get; set; } = 512;

    public int MaxLocationStaysPerDay { get; set; } = 256;

    public int MaxNarrativeFacts { get; set; } = 24;

    internal bool Normalize()
    {
        int checkpointInterval = Clamp(CheckpointIntervalSeconds, 0, 600);
        int maximumEvents = Clamp(MaxEventsPerDay, 32, 4096);
        int maximumLocations = Clamp(MaxLocationStaysPerDay, 16, 2048);
        int maximumFacts = Clamp(MaxNarrativeFacts, 4, 128);
        bool changed = checkpointInterval != CheckpointIntervalSeconds
            || maximumEvents != MaxEventsPerDay
            || maximumLocations != MaxLocationStaysPerDay
            || maximumFacts != MaxNarrativeFacts;
        CheckpointIntervalSeconds = checkpointInterval;
        MaxEventsPerDay = maximumEvents;
        MaxLocationStaysPerDay = maximumLocations;
        MaxNarrativeFacts = maximumFacts;
        return changed;
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
