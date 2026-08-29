using System.Collections.Generic;

namespace StoryDataCollector;

public sealed class DailyRecord
{
    public int SchemaVersion { get; set; } = 2;

    public DailyDate Date { get; set; } = new();

    public DailyContext Context { get; set; } = new();

    public PlayerDayState? StartState { get; set; }

    public PlayerDayState? EndState { get; set; }

    public List<LocationStay> LocationStays { get; set; } = new();

    public List<GameEvent> Events { get; set; } = new();

    // Facts omitted only because the bounded daily archive was full.
    public Dictionary<string, int> DroppedEventCounts { get; set; } = new();

    public int DroppedLocationStays { get; set; }

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

public sealed class PlayerDayState
{
    public int Time { get; set; }

    public int Money { get; set; }

    public int Health { get; set; }

    public string Location { get; set; } = "Unknown";

    public string LocationDisplayName { get; set; } = "Unknown";

    // Two bounded snapshots reveal results across gameplay systems without recording every action.
    public List<InventoryStack> Inventory { get; set; } = new();
}

public sealed class InventoryStack
{
    public string ItemId { get; set; } = "Unknown";

    public string ItemName { get; set; } = "Unknown";

    public int Quality { get; set; }

    public int Count { get; set; }
}
