using System;
using System.Collections.Generic;
using System.Linq;
using StoryDataCollector;

static class Program
{
    private static int Main()
    {
        try
        {
            NarrativeProjectionIsBoundedAndAggregated();
            StoryEventsExposeNarrativeMeaningAndWinTheBudget();
            EventBudgetRetainsHigherValueFacts();
            CheckpointValidationRejectsMalformedSnapshots();
            ReloadedDaysDiscardAbandonedCurrentAttempt();
            ConfigurationClampsPersistenceBudgets();
            AiContractDoesNotExposeRawEventFields();
            Console.WriteLine("StoryDataCollector.Tests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"StoryDataCollector.Tests: FAIL - {ex.Message}");
            return 1;
        }
    }

    private static void NarrativeProjectionIsBoundedAndAggregated()
    {
        DailyRecord record = CreateRecord();
        record.Events.AddRange(new[]
        {
            Event("NpcTalk", 800, 1, target: "Clint", importance: 1),
            Event("NpcTalk", 900, 2, target: "Clint", importance: 1),
            Event("NpcTalk", 1000, 3, target: "Clint", importance: 1),
            Event("GiftGiven", 1100, 4, target: "Clint", importance: 2, details: new() { ["itemName"] = "Amethyst", ["count"] = 1 }),
            Event("Purchase", 1200, 5, target: "Clint", importance: 1, details: new() { ["itemName"] = "Coal", ["count"] = 3, ["cost"] = 450 }),
            Event("PlayerPassedOut", 2500, 6, importance: 4)
        });
        record.LocationStays.Add(new LocationStay { Location = "Mine", LocationDisplayName = "The Mines", EnterTime = 1200, LeaveTime = 2400, Duration = 720 });

        NarrativeDailyInput input = new NarrativeProjectionBuilder().Build(record, 8);
        NarrativeFact social = RequireSingle(input.Facts, fact => fact.Kind == "NpcEncounter");
        NarrativeFact purchase = RequireSingle(input.Facts, fact => fact.Kind == "Purchase");
        NarrativeFact inventory = RequireSingle(input.Facts, fact => fact.Kind == "InventoryChange");
        Require(social.Occurrences == 3, "NPC encounters must aggregate repeated talks.");
        Require(purchase.Quantity == 3 && purchase.MoneyAmount == 450, "Purchases must retain aggregated quantity and cost.");
        Require(inventory.ItemName == "Parsnip" && inventory.Quantity == 5, "Inventory snapshots must expose observed daily results without an action claim.");
        Require(input.Facts.Count <= 8, "Narrative facts must obey the configured budget.");

        NarrativeDailyInput narrowInput = new NarrativeProjectionBuilder().Build(record, 2);
        Require(narrowInput.Facts.Count == 2, "The projection must select no more than the narrow budget.");
        Require(narrowInput.Facts.Any(fact => fact.Kind == "PlayerPassedOut"), "High-importance setbacks must survive selection.");
    }

    private static void StoryEventsExposeNarrativeMeaningAndWinTheBudget()
    {
        string[] commands =
        {
            "none",
            "10 10",
            "Shane 8 8 2 farmer 10 8 0 Emily 12 8 3",
            "skippable",
            "speak Shane \"We're shooting a Joja Cola commercial today.\"",
            "animate Shane false true 100 5 6",
            "playSound cameraNoise",
            "quickQuestion \"Will you join the filming?#Yes, I'll take part.#No, thanks.\""
        };
        StoryEventScriptSummary summary = StoryEventScriptParser.Extract(commands);
        Require(summary.Participants.SequenceEqual(new[] { "Shane", "Player", "Emily" }), "Story script participants must include the player and NPC actors in scene order.");
        Require(summary.PlayerParticipated, "The farmer actor must mark player participation.");
        Require(summary.DialogueHighlights.Any(line => line.Contains("Joja Cola commercial", StringComparison.Ordinal)), "Story dialogue must retain the meaning of the cutscene.");
        Require(summary.ActionCues.Any(line => line.Contains("cameraNoise", StringComparison.Ordinal)), "Story actions must retain meaningful scene cues.");
        StoryEventScriptSummary bounded = StoryEventScriptParser.Extract(
            commands.Concat(Enumerable.Range(0, 30).Select(index => $"speak Shane \"extra dialogue {index}\"")));
        Require(bounded.DialogueHighlights.Count == StoryEventScriptParser.MaxDialogueHighlights, "Story dialogue extraction must obey its hard bound.");
        Require(
            StoryEventScriptParser.ExtractSelectedChoice(commands[^1], 0) == "Yes, I'll take part.",
            "The selected quick-question answer must retain its displayed text.");
        const string normalQuestion = "question filmingAnswer \"Where should we film?#Outside Joja.#Inside the store.\"";
        Require(
            StoryEventScriptParser.ExtractQuestionText(normalQuestion) == "Where should we film?"
            && StoryEventScriptParser.ExtractSelectedChoice(normalQuestion, 1) == "Inside the store.",
            "Normal questions must omit the internal key and retain the selected displayed text.");

        StoryEventScriptSummary executed = StoryEventScriptParser.CreateInitial(commands);
        StoryEventScriptParser.ObserveCommand(executed, new[] { "speak", "Shane", "Thanks for joining the commercial." });
        Require(
            executed.DialogueHighlights.Single().Contains("Thanks for joining", StringComparison.Ordinal),
            "A dynamically inserted selected branch must be captured when its command actually executes.");
        Require(
            !executed.DialogueHighlights.Any(line => line.Contains("No, thanks", StringComparison.Ordinal)),
            "Unexecuted question branches must not be recorded as outcomes.");

        DailyRecord record = CreateRecord();
        record.Events.Add(Event(
            "StoryEvent",
            900,
            1,
            importance: 5,
            details: new()
            {
                ["eventId"] = "shane-commercial",
                ["endTime"] = 930,
                ["participants"] = summary.Participants,
                ["dialogueHighlights"] = summary.DialogueHighlights,
                ["actionCues"] = summary.ActionCues,
                ["playerChoices"] = new List<string> { "Will you join the filming? → Yes, I'll take part." },
                ["playerParticipated"] = true,
                ["completed"] = true,
                ["skipped"] = false
            }));
        record.Events.Add(Event("PlayerPassedOut", 2500, 2, importance: 4));
        record.LocationStays.Add(new LocationStay { Location = "Town", LocationDisplayName = "Pelican Town", EnterTime = 800, LeaveTime = 1200, Duration = 240 });

        NarrativeDailyInput input = new NarrativeProjectionBuilder().Build(record, 1);
        NarrativeFact story = RequireSingle(input.Facts, fact => fact.Kind == "StoryEvent");
        Require(input.SchemaVersion == 2, "The expanded AI story-event contract must use schema version 2.");
        Require(story.LastTime == 930, "Story facts must retain the event end time.");
        Require(story.Participants.SequenceEqual(summary.Participants), "AI input must receive bounded story participants.");
        Require(story.PlayerParticipated == true && story.Completed == true && story.Skipped == false, "AI input must distinguish player participation and event completion.");
        Require(story.DialogueHighlights.Any(line => line.Contains("Joja Cola commercial", StringComparison.Ordinal)), "AI input must carry semantic dialogue rather than only an event ID.");
        Require(story.PlayerChoices.Single().Contains("Yes, I'll take part", StringComparison.Ordinal), "AI input must carry the selected answer text.");
    }

    private static void EventBudgetRetainsHigherValueFacts()
    {
        DailyRecord record = CreateRecord();
        Require(DailyEventBudget.TryAdd(record, Event("NpcTalk", 800, 1, importance: 1), 2), "First event should fit.");
        Require(DailyEventBudget.TryAdd(record, Event("Purchase", 900, 2, importance: 1), 2), "Second event should fit.");
        Require(DailyEventBudget.TryAdd(record, Event("PlayerPassedOut", 1000, 3, importance: 4), 2), "Higher-value event should replace weaker retained data.");
        Require(record.Events.Count == 2, "The archive must stay bounded.");
        Require(record.Events.Any(gameEvent => gameEvent.Type == "PlayerPassedOut"), "The higher-value event was not retained.");
        Require(record.DroppedEventCounts.Values.Sum() == 1, "The displaced low-value event must remain visible in dropped counts.");
    }

    private static void CheckpointValidationRejectsMalformedSnapshots()
    {
        DailyCheckpoint checkpoint = new()
        {
            Date = new DailyDate { Year = 1, Season = "spring", Day = 1 },
            Context = new DailyContext { Weather = "Sun" },
            StartState = new PlayerDayState { Time = 600, Money = 100, Health = 100 },
            Events = new List<GameEvent> { Event("NpcTalk", 800, 1, target: "Clint") },
            LastSequence = 1
        };
        Require(CheckpointValidator.IsValid(checkpoint), "A complete checkpoint snapshot must validate.");

        checkpoint.Events.Add(Event("Purchase", 900, 1));
        Require(!CheckpointValidator.IsValid(checkpoint), "Duplicate event IDs must reject a checkpoint before recovery writes raw data.");

        List<string> participants = Enumerable.Range(0, StoryEventScriptParser.MaxParticipants)
            .Select(index => $"Actor{index}")
            .ToList();
        checkpoint.Events = new List<GameEvent>
        {
            Event(
                "StoryEvent",
                1000,
                2,
                importance: 5,
                details: new()
                {
                    ["eventId"] = "story-event",
                    ["sourceAsset"] = "Data/Events/Town",
                    ["participants"] = participants,
                    ["dialogueHighlights"] = new List<string>(),
                    ["actionCues"] = new List<string>(),
                    ["playerChoices"] = new List<string>(),
                    ["playerParticipated"] = true,
                    ["completed"] = true,
                    ["skipped"] = false,
                    ["endTime"] = 1030
                })
        };
        checkpoint.LastSequence = 2;
        Require(CheckpointValidator.IsValid(checkpoint), "A bounded story event checkpoint must validate.");
        participants.Add("ActorOverflow");
        Require(!CheckpointValidator.IsValid(checkpoint), "Unbounded story details must reject a checkpoint before recovery writes raw data.");
    }

    private static void ReloadedDaysDiscardAbandonedCurrentAttempt()
    {
        DailyDate currentDate = new() { Year = 1, Season = "summer", Day = 4 };
        DailyDate priorDate = new() { Year = 1, Season = "summer", Day = 3 };

        Require(
            CheckpointRecoveryPolicy.Classify(currentDate, currentDate, completedRecordExists: false)
                == CheckpointRecoveryAction.DiscardAbandonedCurrentAttempt,
            "Reloading the same game day must not merge an abandoned checkpoint timeline into the restarted day.");
        Require(
            CheckpointRecoveryPolicy.Classify(priorDate, currentDate, completedRecordExists: false)
                == CheckpointRecoveryAction.ArchiveHistoricalAttempt,
            "A bounded historical checkpoint may still recover its incomplete archive without joining the current day.");
        Require(
            CheckpointRecoveryPolicy.Classify(currentDate, currentDate, completedRecordExists: true)
                == CheckpointRecoveryAction.CompleteFinalRecord,
            "A completed raw record must retain narrative-input retry recovery.");
    }

    private static void ConfigurationClampsPersistenceBudgets()
    {
        ModConfig config = new()
        {
            CheckpointIntervalSeconds = -1,
            MaxEventsPerDay = 99999,
            MaxLocationStaysPerDay = 1,
            MaxNarrativeFacts = 0
        };
        Require(config.Normalize(), "Out-of-range persistence settings must normalize.");
        Require(config.CheckpointIntervalSeconds == 0, "Checkpoint interval lower bound was not applied.");
        Require(config.MaxEventsPerDay == 4096, "Event archive hard bound was not applied.");
        Require(config.MaxLocationStaysPerDay == 16, "Location archive hard bound was not applied.");
        Require(config.MaxNarrativeFacts == 4, "Narrative fact lower bound was not applied.");
    }

    private static void AiContractDoesNotExposeRawEventFields()
    {
        Require(typeof(NarrativeFact).GetProperty(nameof(GameEvent.Id)) is null, "AI facts must not expose raw event IDs.");
        Require(typeof(NarrativeFact).GetProperty("EventId") is null, "AI story facts must expose meaning, not internal event IDs.");
        Require(typeof(NarrativeFact).GetProperty(nameof(GameEvent.Details)) is null, "AI facts must not expose untyped raw details.");
        Require(typeof(NarrativePlayerState).GetProperty(nameof(PlayerDayState.Inventory)) is null, "AI state must not copy complete inventory snapshots outside the fact budget.");
        Require(typeof(GameEvent).GetProperty("RealTimestamp") is null, "Debug configuration must not alter the core event schema.");
    }

    private static DailyRecord CreateRecord()
    {
        return new DailyRecord
        {
            Date = new DailyDate { Year = 1, Season = "spring", Day = 1 },
            Context = new DailyContext { Weather = "Sun" },
            StartState = new PlayerDayState { Time = 600, Money = 100, Health = 100, Location = "Farm", LocationDisplayName = "Farm", Inventory = new List<InventoryStack>() },
            EndState = new PlayerDayState
            {
                Time = 2600,
                Money = 500,
                Health = 10,
                Location = "FarmHouse",
                LocationDisplayName = "Farmhouse",
                Inventory = new List<InventoryStack> { new() { ItemId = "(O)24", ItemName = "Parsnip", Count = 5 } }
            }
        };
    }

    private static GameEvent Event(
        string type,
        int time,
        long sequence,
        string? target = null,
        int importance = 1,
        Dictionary<string, object?>? details = null)
    {
        return new GameEvent
        {
            Id = $"event-{sequence}",
            Type = type,
            Time = time,
            Sequence = sequence,
            Location = "Town",
            LocationDisplayName = "Pelican Town",
            Target = target,
            Importance = importance,
            Evidence = "Observed",
            Details = details ?? new Dictionary<string, object?>()
        };
    }

    private static NarrativeFact RequireSingle(IEnumerable<NarrativeFact> facts, Func<NarrativeFact, bool> predicate)
    {
        NarrativeFact[] matches = facts.Where(predicate).ToArray();
        Require(matches.Length == 1, "Expected exactly one matching narrative fact.");
        return matches[0];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
