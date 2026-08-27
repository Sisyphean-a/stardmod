using System.Collections.Generic;

namespace StoryDataCollector;

public sealed class DailyRecord
{
    public int SchemaVersion { get; set; } = 1;

    public DailyDate Date { get; set; } = new();

    public DailyContext Context { get; set; } = new();

    public List<LocationStay> LocationStays { get; set; } = new();

    public List<GameEvent> Events { get; set; } = new();

    public List<GameEvent>? DebugRawEvents { get; set; }

    public Dictionary<string, object?> SummaryStats { get; set; } = new();

    public bool IsComplete { get; set; }
}

public sealed class DailyDate
{
    public int Year { get; set; }

    public string Season { get; set; } = "unknown";

    public int Day { get; set; }
}

public sealed class DailyContext
{
    public string Weather { get; set; } = "Unknown";

    public bool IsRaining { get; set; }

    public bool IsSnowing { get; set; }

    public bool IsLightning { get; set; }

    public bool IsGreenRain { get; set; }

    public bool IsDebrisWeather { get; set; }

    public bool IsFestival { get; set; }

    public string? FestivalLocation { get; set; }

    public double? Luck { get; set; }

    public string? Spouse { get; set; }

    public int FarmType { get; set; }
}
