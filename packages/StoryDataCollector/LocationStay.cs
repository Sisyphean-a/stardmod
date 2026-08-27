namespace StoryDataCollector;

public sealed class LocationStay
{
    public string Location { get; set; } = "Unknown";

    public string LocationDisplayName { get; set; } = "Unknown";

    public int EnterTime { get; set; }

    public int? LeaveTime { get; set; }

    public int Duration { get; set; }

    public bool IsOutdoors { get; set; }

    public bool IsTemporary { get; set; }

    public string Evidence { get; set; } = "Derived";
}
