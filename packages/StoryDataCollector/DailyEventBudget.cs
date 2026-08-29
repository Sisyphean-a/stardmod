using System;

namespace StoryDataCollector;

internal static class DailyEventBudget
{
    // Keeps the strongest facts if a pathological or automated session exceeds the archive budget.
    internal static bool TryAdd(DailyRecord record, GameEvent gameEvent, int maximumEvents)
    {
        if (record.Events.Count < maximumEvents)
        {
            record.Events.Add(gameEvent);
            return true;
        }

        int weakestIndex = FindWeakestIndex(record);
        if (weakestIndex >= 0 && record.Events[weakestIndex].Importance < gameEvent.Importance)
        {
            CountDropped(record, record.Events[weakestIndex]);
            record.Events[weakestIndex] = gameEvent;
            return true;
        }

        CountDropped(record, gameEvent);
        return false;
    }

    private static int FindWeakestIndex(DailyRecord record)
    {
        int weakestIndex = -1;
        for (int index = 0; index < record.Events.Count; index++)
        {
            if (weakestIndex < 0
                || record.Events[index].Importance < record.Events[weakestIndex].Importance
                || record.Events[index].Importance == record.Events[weakestIndex].Importance
                && record.Events[index].Sequence < record.Events[weakestIndex].Sequence)
            {
                weakestIndex = index;
            }
        }

        return weakestIndex;
    }

    private static void CountDropped(DailyRecord record, GameEvent gameEvent)
    {
        string type = string.IsNullOrWhiteSpace(gameEvent.Type) ? "Unknown" : gameEvent.Type;
        record.DroppedEventCounts.TryGetValue(type, out int count);
        record.DroppedEventCounts[type] = checked(count + 1);
    }
}
