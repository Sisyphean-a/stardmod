using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace StoryDataCollector;

public sealed class NarrativeProjectionBuilder
{
    public NarrativeDailyInput Build(DailyRecord record, int maximumFacts)
    {
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        int maxFacts = Math.Max(1, maximumFacts);
        List<FactCandidate> candidates = BuildEventCandidates(record);
        candidates.AddRange(BuildLocationCandidates(record));
        candidates.AddRange(BuildInventoryChangeCandidates(record));
        AddDayOutcomeCandidate(record, candidates);

        List<NarrativeFact> facts = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Fact.FirstTime)
            .ThenBy(candidate => candidate.Fact.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Fact.Target, StringComparer.Ordinal)
            .Take(maxFacts)
            .Select(candidate => candidate.Fact)
            .OrderBy(fact => fact.FirstTime)
            .ThenBy(fact => fact.LastTime)
            .ThenBy(fact => fact.Kind, StringComparer.Ordinal)
            .ToList();

        int omittedSourceEvents = record.DroppedEventCounts.Values.Sum();
        return new NarrativeDailyInput
        {
            Date = CopyDate(record.Date),
            Context = CopyContext(record.Context),
            StartState = CopyState(record.StartState),
            EndState = CopyState(record.EndState),
            Facts = facts,
            OmittedEventCounts = new Dictionary<string, int>(record.DroppedEventCounts, StringComparer.Ordinal),
            Budget = new NarrativeInputBudget
            {
                SourceEventCount = checked(record.Events.Count + omittedSourceEvents),
                CandidateFactCount = candidates.Count,
                SelectedFactCount = facts.Count,
                MaxFacts = maxFacts,
                OmittedSourceEventCount = omittedSourceEvents
            }
        };
    }

    private static List<FactCandidate> BuildEventCandidates(DailyRecord record)
    {
        List<FactCandidate> candidates = new();
        foreach (IGrouping<EventGroupKey, GameEvent> group in record.Events
                     .OrderBy(gameEvent => gameEvent.Time)
                     .ThenBy(gameEvent => gameEvent.Sequence)
                     .GroupBy(CreateEventGroupKey))
        {
            List<GameEvent> events = group.ToList();
            GameEvent first = events[0];
            GameEvent last = events[^1];
            string kind = GetNarrativeKind(first.Type);
            NarrativeFact fact = new()
            {
                Kind = kind,
                Target = first.Target,
                Location = first.Location,
                LocationDisplayName = first.LocationDisplayName,
                FirstTime = first.Time,
                LastTime = last.Time,
                Occurrences = events.Count,
                Importance = events.Max(gameEvent => gameEvent.Importance),
                Evidence = events.All(gameEvent => gameEvent.Evidence == "Observed") ? "Observed" : "Mixed"
            };

            switch (first.Type)
            {
                case "GiftGiven":
                    fact.ItemName = GetStringDetail(first, "itemName");
                    fact.Quantity = SumDetail(events, "count");
                    break;
                case "Purchase":
                    fact.ItemName = GetStringDetail(first, "itemName");
                    fact.Quantity = SumDetail(events, "count");
                    fact.MoneyAmount = SumDetail(events, "cost");
                    break;
                case "MoneyChanged":
                    fact.MoneyAmount = SumDetail(events, "amount");
                    break;
                case "StoryEvent":
                    fact.LastTime = GetIntDetail(first, "endTime") is int endTime && endTime > 0
                        ? endTime
                        : first.Time;
                    fact.Participants = GetStringListDetail(first, "participants", StoryEventScriptParser.MaxParticipants, 80);
                    fact.DialogueHighlights = GetStringListDetail(first, "dialogueHighlights", StoryEventScriptParser.MaxDialogueHighlights, 240);
                    fact.ActionCues = GetStringListDetail(first, "actionCues", StoryEventScriptParser.MaxActionCues, 160);
                    fact.PlayerChoices = GetStringListDetail(first, "playerChoices", StoryEventScriptParser.MaxPlayerChoices, 500);
                    fact.PlayerParticipated = GetBoolDetail(first, "playerParticipated");
                    fact.Completed = GetBoolDetail(first, "completed");
                    fact.Skipped = GetBoolDetail(first, "skipped");
                    break;
            }

            candidates.Add(new FactCandidate(fact, Score(fact)));
        }

        return candidates;
    }

    private static IEnumerable<FactCandidate> BuildLocationCandidates(DailyRecord record)
    {
        foreach (IGrouping<LocationGroupKey, LocationStay> group in record.LocationStays
                     .Where(stay => stay.Duration > 0)
                     .GroupBy(stay => new LocationGroupKey(stay.Location, stay.LocationDisplayName)))
        {
            List<LocationStay> stays = group.OrderBy(stay => stay.EnterTime).ToList();
            LocationStay first = stays[0];
            LocationStay last = stays[^1];
            int duration = stays.Sum(stay => stay.Duration);
            NarrativeFact fact = new()
            {
                Kind = "LocationVisit",
                Location = first.Location,
                LocationDisplayName = first.LocationDisplayName,
                FirstTime = first.EnterTime,
                LastTime = last.LeaveTime ?? last.EnterTime,
                Occurrences = stays.Count,
                Importance = duration >= 240 ? 2 : 1,
                Evidence = "Derived",
                DurationMinutes = duration
            };
            yield return new FactCandidate(fact, Score(fact));
        }
    }

    private static IEnumerable<FactCandidate> BuildInventoryChangeCandidates(DailyRecord record)
    {
        if (record.StartState is null || record.EndState is null)
            yield break;

        Dictionary<InventoryKey, InventoryStack> start = SumInventory(record.StartState.Inventory);
        Dictionary<InventoryKey, InventoryStack> end = SumInventory(record.EndState.Inventory);
        foreach (InventoryKey key in start.Keys.Union(end.Keys).OrderBy(key => key.ItemId, StringComparer.Ordinal).ThenBy(key => key.Quality))
        {
            start.TryGetValue(key, out InventoryStack? before);
            end.TryGetValue(key, out InventoryStack? after);
            int change = (after?.Count ?? 0) - (before?.Count ?? 0);
            if (change == 0)
                continue;

            InventoryStack item = after ?? before!;
            NarrativeFact fact = new()
            {
                Kind = "InventoryChange",
                ItemName = item.ItemName,
                ItemQuality = item.Quality,
                FirstTime = record.StartState.Time,
                LastTime = record.EndState.Time,
                Occurrences = 1,
                Importance = Math.Abs(change) >= 10 ? 2 : 1,
                Evidence = "Observed",
                Quantity = change
            };
            yield return new FactCandidate(fact, Score(fact));
        }
    }

    private static Dictionary<InventoryKey, InventoryStack> SumInventory(IEnumerable<InventoryStack> inventory)
    {
        Dictionary<InventoryKey, InventoryStack> result = new();
        foreach (InventoryStack stack in inventory.Where(stack => stack.Count > 0 && !string.IsNullOrWhiteSpace(stack.ItemId)))
        {
            InventoryKey key = new(stack.ItemId, stack.Quality);
            if (result.TryGetValue(key, out InventoryStack? existing))
            {
                existing.Count = checked(existing.Count + stack.Count);
                continue;
            }

            result[key] = new InventoryStack
            {
                ItemId = stack.ItemId,
                ItemName = stack.ItemName,
                Quality = stack.Quality,
                Count = stack.Count
            };
        }

        return result;
    }

    private static void AddDayOutcomeCandidate(DailyRecord record, ICollection<FactCandidate> candidates)
    {
        if (record.StartState is null || record.EndState is null)
            return;

        int moneyChange = record.EndState.Money - record.StartState.Money;
        int healthChange = record.EndState.Health - record.StartState.Health;
        if (moneyChange == 0 && healthChange == 0)
            return;

        NarrativeFact fact = new()
        {
            Kind = "DayOutcome",
            Location = record.EndState.Location,
            LocationDisplayName = record.EndState.LocationDisplayName,
            FirstTime = record.StartState.Time,
            LastTime = record.EndState.Time,
            Occurrences = 1,
            Importance = moneyChange != 0 && healthChange != 0 ? 2 : 1,
            Evidence = "Derived",
            MoneyChange = moneyChange,
            HealthChange = healthChange
        };
        candidates.Add(new FactCandidate(fact, Score(fact)));
    }

    private static EventGroupKey CreateEventGroupKey(GameEvent gameEvent)
    {
        string? itemName = gameEvent.Type switch
        {
            "GiftGiven" or "Purchase" => GetStringDetail(gameEvent, "itemName"),
            "StoryEvent" => $"{GetStringDetail(gameEvent, "eventId")}#{gameEvent.Sequence}",
            _ => null
        };
        int moneyDirection = gameEvent.Type == "MoneyChanged"
            ? Math.Sign(GetIntDetail(gameEvent, "amount"))
            : 0;
        return new EventGroupKey(gameEvent.Type, gameEvent.Target, gameEvent.Location, itemName, moneyDirection);
    }

    private static string GetNarrativeKind(string eventType)
    {
        return eventType switch
        {
            "NpcTalk" => "NpcEncounter",
            "GiftGiven" => "Gift",
            "Purchase" => "Purchase",
            "MoneyChanged" => "UnattributedMoneyChange",
            "Sleep" => "Sleep",
            "PlayerPassedOut" => "PlayerPassedOut",
            "PlayerKnockedOut" => "PlayerKnockedOut",
            _ => string.IsNullOrWhiteSpace(eventType) ? "Unknown" : eventType
        };
    }

    private static int Score(NarrativeFact fact)
    {
        int score = fact.Importance * 100 + Math.Min(fact.Occurrences, 20) * 3;
        score += fact.Kind switch
        {
            "StoryEvent" => 1000,
            "PlayerPassedOut" or "PlayerKnockedOut" => 300,
            "Gift" => 100,
            "NpcEncounter" => 50,
            "DayOutcome" => 40,
            "LocationVisit" => Math.Min(fact.DurationMinutes.GetValueOrDefault(), 360) / 6,
            "InventoryChange" => Math.Min(Math.Abs(fact.Quantity.GetValueOrDefault()), 99) / 3,
            "UnattributedMoneyChange" => -20,
            _ => 0
        };
        return score;
    }

    private static int SumDetail(IEnumerable<GameEvent> events, string name)
    {
        int total = 0;
        foreach (GameEvent gameEvent in events)
            total = checked(total + GetIntDetail(gameEvent, name));
        return total;
    }

    private static string? GetStringDetail(GameEvent gameEvent, string name)
    {
        return gameEvent.Details.TryGetValue(name, out object? value) ? value?.ToString() : null;
    }

    private static int GetIntDetail(GameEvent gameEvent, string name)
    {
        if (!gameEvent.Details.TryGetValue(name, out object? value) || value is null)
            return 0;

        return value switch
        {
            int integer => integer,
            long integer when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
            short integer => integer,
            byte integer => integer,
            _ when int.TryParse(value.ToString(), out int parsed) => parsed,
            _ => 0
        };
    }

    private static bool? GetBoolDetail(GameEvent gameEvent, string name)
    {
        if (!gameEvent.Details.TryGetValue(name, out object? value) || value is null)
            return null;
        if (value is bool boolean)
            return boolean;
        return bool.TryParse(value.ToString(), out bool parsed) ? parsed : null;
    }

    private static List<string> GetStringListDetail(GameEvent gameEvent, string name, int maximumCount, int maximumLength)
    {
        if (!gameEvent.Details.TryGetValue(name, out object? value) || value is null)
            return new List<string>();

        IEnumerable values = value is string scalar ? new[] { scalar } : value as IEnumerable ?? Array.Empty<object>();
        List<string> result = new();
        foreach (object? item in values)
        {
            string? itemText = item?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(itemText))
                continue;
            if (itemText.Length > maximumLength)
                itemText = itemText[..maximumLength].TrimEnd() + "…";
            if (result.Contains(itemText, StringComparer.Ordinal))
                continue;
            result.Add(itemText);
            if (result.Count >= maximumCount)
                break;
        }
        return result;
    }

    private static DailyDate CopyDate(DailyDate date)
    {
        return new DailyDate { Year = date.Year, Season = date.Season, Day = date.Day };
    }

    private static DailyContext CopyContext(DailyContext context)
    {
        return new DailyContext
        {
            Weather = context.Weather,
            IsRaining = context.IsRaining,
            IsSnowing = context.IsSnowing,
            IsLightning = context.IsLightning,
            IsGreenRain = context.IsGreenRain,
            IsDebrisWeather = context.IsDebrisWeather,
            IsFestival = context.IsFestival,
            FestivalLocation = context.FestivalLocation,
            Luck = context.Luck,
            Spouse = context.Spouse,
            FarmType = context.FarmType
        };
    }

    private static NarrativePlayerState? CopyState(PlayerDayState? state)
    {
        return state is null
            ? null
            : new NarrativePlayerState
            {
                Time = state.Time,
                Money = state.Money,
                Health = state.Health,
                Location = state.Location,
                LocationDisplayName = state.LocationDisplayName
            };
    }

    private sealed record FactCandidate(NarrativeFact Fact, int Score);

    private readonly record struct EventGroupKey(
        string Type,
        string? Target,
        string? Location,
        string? ItemName,
        int MoneyDirection);

    private readonly record struct LocationGroupKey(string Location, string LocationDisplayName);

    private readonly record struct InventoryKey(string ItemId, int Quality);
}
