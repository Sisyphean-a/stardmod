using System.Collections.Generic;

namespace StoryDataCollector;

// This is the only persisted contract intended for a future story-generation client.
public sealed class NarrativeDailyInput
{
    public int SchemaVersion { get; set; } = 2;

    public DailyDate Date { get; set; } = new();

    public DailyContext Context { get; set; } = new();

    public NarrativePlayerState? StartState { get; set; }

    public NarrativePlayerState? EndState { get; set; }

    public List<NarrativeFact> Facts { get; set; } = new();

    public Dictionary<string, int> OmittedEventCounts { get; set; } = new();

    public NarrativeInputBudget Budget { get; set; } = new();
}

public sealed class NarrativePlayerState
{
    public int Time { get; set; }

    public int Money { get; set; }

    public int Health { get; set; }

    public string Location { get; set; } = "Unknown";

    public string LocationDisplayName { get; set; } = "Unknown";
}

public sealed class NarrativeFact
{
    public string Kind { get; set; } = "Unknown";

    public string? Target { get; set; }

    public string? Location { get; set; }

    public string? LocationDisplayName { get; set; }

    public string? ItemName { get; set; }

    public int? ItemQuality { get; set; }

    public int FirstTime { get; set; }

    public int LastTime { get; set; }

    public int Occurrences { get; set; }

    public int Importance { get; set; }

    public string Evidence { get; set; } = "Observed";

    public int? Quantity { get; set; }

    public int? MoneyAmount { get; set; }

    public int? DurationMinutes { get; set; }

    public int? MoneyChange { get; set; }

    public int? HealthChange { get; set; }

    public List<string> Participants { get; set; } = new();

    public List<string> DialogueHighlights { get; set; } = new();

    public List<string> ActionCues { get; set; } = new();

    public List<string> PlayerChoices { get; set; } = new();

    public bool? PlayerParticipated { get; set; }

    public bool? Completed { get; set; }

    public bool? Skipped { get; set; }

    public bool ShouldSerializeParticipants() => Participants.Count > 0;

    public bool ShouldSerializeDialogueHighlights() => DialogueHighlights.Count > 0;

    public bool ShouldSerializeActionCues() => ActionCues.Count > 0;

    public bool ShouldSerializePlayerChoices() => PlayerChoices.Count > 0;

    public bool ShouldSerializePlayerParticipated() => PlayerParticipated.HasValue;

    public bool ShouldSerializeCompleted() => Completed.HasValue;

    public bool ShouldSerializeSkipped() => Skipped.HasValue;
}

public sealed class NarrativeInputBudget
{
    public int SourceEventCount { get; set; }

    public int CandidateFactCount { get; set; }

    public int SelectedFactCount { get; set; }

    public int MaxFacts { get; set; }

    public int OmittedSourceEventCount { get; set; }
}
