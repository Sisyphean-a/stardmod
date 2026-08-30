using System;
using System.Collections;
using System.Linq;

namespace StoryDataCollector;

internal static class CheckpointValidator
{
    internal const int MaximumEvents = 4096;
    internal const int MaximumLocations = 2048;

    internal static bool IsValid(DailyCheckpoint checkpoint)
    {
        return checkpoint.SchemaVersion == 1
            && checkpoint.Date is not null
            && checkpoint.Context is not null
            && checkpoint.StartState is not null
            && checkpoint.Events is not null
            && checkpoint.LocationStays is not null
            && checkpoint.DroppedEventCounts is not null
            && checkpoint.Date.Year >= 1
            && checkpoint.Date.Day is >= 1 and <= 28
            && !string.IsNullOrWhiteSpace(checkpoint.Date.Season)
            && checkpoint.Events.Count <= MaximumEvents
            && checkpoint.LocationStays.Count <= MaximumLocations
            && checkpoint.DroppedLocationStays >= 0
            && checkpoint.DroppedEventCounts.Values.All(count => count >= 0)
            && checkpoint.Events.All(IsValidEvent)
            && checkpoint.Events.Select(gameEvent => gameEvent.Id).Distinct(StringComparer.Ordinal).Count() == checkpoint.Events.Count
            && checkpoint.LastSequence >= checkpoint.Events.Select(gameEvent => gameEvent.Sequence).DefaultIfEmpty(0).Max();
    }

    private static bool IsValidEvent(GameEvent gameEvent)
    {
        if (gameEvent.Details is null
            || string.IsNullOrWhiteSpace(gameEvent.Id)
            || string.IsNullOrWhiteSpace(gameEvent.Type)
            || gameEvent.Sequence <= 0
            || gameEvent.Importance is < 0 or > 5)
        {
            return false;
        }
        if (gameEvent.Type != "StoryEvent")
            return true;

        return HasBoundedText(gameEvent, "eventId", 120)
            && HasOptionalBoundedText(gameEvent, "sourceAsset", 160)
            && HasBoundedStringList(gameEvent, "participants", StoryEventScriptParser.MaxParticipants, 80)
            && HasBoundedStringList(gameEvent, "dialogueHighlights", StoryEventScriptParser.MaxDialogueHighlights, 240)
            && HasBoundedStringList(gameEvent, "actionCues", StoryEventScriptParser.MaxActionCues, 160)
            && HasBoundedStringList(gameEvent, "playerChoices", StoryEventScriptParser.MaxPlayerChoices, 500)
            && HasBoolean(gameEvent, "playerParticipated")
            && HasBoolean(gameEvent, "completed")
            && HasBoolean(gameEvent, "skipped")
            && HasGameTime(gameEvent, "endTime");
    }

    private static bool HasBoundedText(GameEvent gameEvent, string name, int maximumLength)
    {
        return gameEvent.Details.TryGetValue(name, out object? value)
            && value is not null
            && !string.IsNullOrWhiteSpace(value.ToString())
            && value.ToString()!.Length <= maximumLength;
    }

    private static bool HasOptionalBoundedText(GameEvent gameEvent, string name, int maximumLength)
    {
        return !gameEvent.Details.TryGetValue(name, out object? value)
            || value is null
            || value.ToString()?.Length <= maximumLength;
    }

    private static bool HasBoundedStringList(GameEvent gameEvent, string name, int maximumCount, int maximumLength)
    {
        if (!gameEvent.Details.TryGetValue(name, out object? value)
            || value is string
            || value is not IEnumerable items)
        {
            return false;
        }

        int count = 0;
        foreach (object? item in items)
        {
            string? text = item?.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || ++count > maximumCount)
                return false;
        }
        return true;
    }

    private static bool HasBoolean(GameEvent gameEvent, string name)
    {
        return gameEvent.Details.TryGetValue(name, out object? value)
            && value is not null
            && (value is bool || bool.TryParse(value.ToString(), out _));
    }

    private static bool HasGameTime(GameEvent gameEvent, string name)
    {
        return gameEvent.Details.TryGetValue(name, out object? value)
            && value is not null
            && int.TryParse(value.ToString(), out int time)
            && time is >= 0 and <= 2800;
    }
}
