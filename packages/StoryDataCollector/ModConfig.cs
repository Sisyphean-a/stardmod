namespace StoryDataCollector;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;

    public bool DebugLogging { get; set; } = true;

    public bool SaveRawEvents { get; set; } = true;

    public int CheckpointIntervalSeconds { get; set; } = 30;

    internal bool Normalize()
    {
        if (CheckpointIntervalSeconds >= 0)
            return false;

        CheckpointIntervalSeconds = 0;
        return true;
    }
}
