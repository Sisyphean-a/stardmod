using System;
using System.Collections.Generic;

namespace StoryDataCollector;

public sealed class GameEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int Day { get; set; }

    public int Time { get; set; }

    public DateTime? RealTimestamp { get; set; }

    public string Type { get; set; } = "";

    public string? Location { get; set; }

    public string? LocationDisplayName { get; set; }

    public string? Actor { get; set; }

    public string? Target { get; set; }

    public Dictionary<string, object?> Details { get; set; } = new();

    public int Importance { get; set; }

    public string Evidence { get; set; } = "Observed";

    public long Sequence { get; set; }
}
