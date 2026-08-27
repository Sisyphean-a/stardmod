using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StoryDataCollector;

internal sealed class TimelineDataCollector
{
    private const string PlayerActor = "Player";

    private readonly IModHelper helper;
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly List<GameEvent> debugRawEvents = new();
    private readonly Dictionary<string, int> talkCounts = new(StringComparer.Ordinal);

    private DailyRecord? record;
    private LocationStay? activeLocationStay;
    private string? saveId;
    private bool finalized;
    private bool playerSnapshotReady;
    private bool knockoutWasRecorded;
    private bool completionLogged;
    private int lastMoney;
    private int lastHealth;
    private int lastTalkTick = -1;
    private string? lastTalkNpc;
    private string? lastTalkLocation;
    private int lastPassOutTick = -1;
    private int checkpointElapsedSeconds;
    private int pendingKnownMoneyDelta;
    private int pendingKnownMoneyExpiryTick = -1;
    private long nextSequence;

    internal TimelineDataCollector(IModHelper helper, ModConfig config, IMonitor monitor)
    {
        this.helper = helper;
        this.config = config;
        this.monitor = monitor;
    }

    internal void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ResetState();
        LogDebug("[Lifecycle] SaveLoaded：等待 DayStarted 建立当天记录。");
    }

    internal void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (record is not null && !finalized)
            SaveRecord("day-start-recovery");

        StartNewDay();
        LogDebug($"[Lifecycle] DayStarted：开始采集 Year{record?.Date.Year}-{record?.Date.Season}-{record?.Date.Day:00}。");
    }

    internal void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (record is null || finalized)
            return;

        EnsureCurrentLocation();
        CloseActiveLocationStay(Game1.timeOfDay);
        finalized = true;
        record.IsComplete = true;
        SaveRecord("day-ending");
    }

    internal void OnSaving(object? sender, SavingEventArgs e)
    {
        if (record is null)
            return;

        SaveRecord("saving");
    }

    internal void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        if (record is not null && !finalized)
            SaveRecord("returned-to-title");

        ResetState();
        LogDebug("[Lifecycle] ReturnedToTitle：已清理当天内存状态。");
    }

    internal void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (record is null)
            StartNewDay();

        if (record is null || finalized)
            return;

        EnsureCurrentLocation();
        TrackPlayerState();

        if (!e.IsOneSecond)
            return;

        checkpointElapsedSeconds++;
        if (config.CheckpointIntervalSeconds > 0
            && checkpointElapsedSeconds >= config.CheckpointIntervalSeconds)
        {
            checkpointElapsedSeconds = 0;
            SaveRecord("periodic-checkpoint");
        }
    }

    internal void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer || !Context.IsWorldReady)
            return;

        if (record is null)
            StartNewDay();

        if (record is null || finalized)
            return;

        SwitchLocation(e.NewLocation, Game1.timeOfDay);
    }

    internal void RecordNpcTalk(NPC npc, GameLocation? location, int invocationTick)
    {
        if (!CanRecord())
            return;

        string npcName = npc.Name;
        string locationId = GetLocationInfo(location).Id;
        if (lastTalkTick == invocationTick
            && string.Equals(lastTalkNpc, npcName, StringComparison.Ordinal)
            && string.Equals(lastTalkLocation, locationId, StringComparison.Ordinal))
        {
            return;
        }

        lastTalkTick = invocationTick;
        lastTalkNpc = npcName;
        lastTalkLocation = locationId;
        talkCounts.TryGetValue(npcName, out int conversationNumber);
        conversationNumber++;
        talkCounts[npcName] = conversationNumber;

        AddEvent(
            "NpcTalk",
            location,
            PlayerActor,
            npcName,
            new Dictionary<string, object?>
            {
                ["conversationNumber"] = conversationNumber
            },
            importance: 1,
            evidence: "Observed");
    }

    internal void RecordGift(
        NPC npc,
        StardewValley.Object item,
        int taste,
        bool birthday,
        float friendshipChangeMultiplier)
    {
        if (!CanRecord())
            return;

        AddEvent(
            "GiftGiven",
            Game1.currentLocation,
            PlayerActor,
            npc.Name,
            new Dictionary<string, object?>
            {
                ["itemId"] = item.QualifiedItemId,
                ["itemName"] = item.DisplayName,
                ["count"] = item.Stack,
                ["quality"] = item.Quality,
                ["taste"] = GetGiftTasteName(taste),
                ["birthday"] = birthday,
                ["friendshipChangeMultiplier"] = friendshipChangeMultiplier
            },
            importance: birthday || taste is 0 or 7 ? 2 : 1,
            evidence: "Observed");
    }

    internal void RecordPurchase(
        ISalable item,
        string shopId,
        string? shopTarget,
        int count,
        int cost,
        int currency,
        int pricePerUnit,
        int moneyDelta)
    {
        if (!CanRecord())
            return;

        AddEvent(
            "Purchase",
            Game1.currentLocation,
            PlayerActor,
            shopTarget,
            new Dictionary<string, object?>
            {
                ["shopId"] = shopId,
                ["itemId"] = item.QualifiedItemId,
                ["itemName"] = item.DisplayName,
                ["count"] = count,
                ["cost"] = cost,
                ["currency"] = currency,
                ["pricePerUnit"] = pricePerUnit,
                ["moneyDelta"] = moneyDelta
            },
            importance: 1,
            evidence: "Observed");
    }

    internal void RecordSleep(GameLocation location)
    {
        if (!CanRecord())
            return;

        AddEvent(
            "Sleep",
            location,
            PlayerActor,
            null,
            new Dictionary<string, object?>
            {
                ["source"] = "GameLocation.doSleep"
            },
            importance: 2,
            evidence: "Observed");
    }

    internal void RecordPassedOut(string source)
    {
        if (!CanRecord() || lastPassOutTick == Game1.ticks)
            return;

        lastPassOutTick = Game1.ticks;
        AddEvent(
            "PlayerPassedOut",
            Game1.currentLocation,
            PlayerActor,
            null,
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["health"] = Game1.player.health
            },
            importance: 4,
            evidence: "Observed");
    }

    internal void MarkKnownMoneyChange(int delta)
    {
        if (!CanRecord() || delta == 0)
            return;

        pendingKnownMoneyDelta += delta;
        pendingKnownMoneyExpiryTick = pendingKnownMoneyDelta == 0 ? -1 : Game1.ticks + 30;
    }

    internal void FlushCheckpoint()
    {
        if (record is null)
        {
            monitor.Log("当前没有可写出的当天记录。请在进入存档后再执行 story_data flush。", LogLevel.Warn);
            return;
        }

        SaveRecord("console");
    }

    internal void LogStatus()
    {
        if (record is null)
        {
            monitor.Log("[StoryDataCollector] 当前没有正在采集的当天记录。", LogLevel.Info);
            return;
        }

        monitor.Log(
            $"[StoryDataCollector] Year{record.Date.Year}-{record.Date.Season}-{record.Date.Day:00}; "
            + $"events={record.Events.Count}; locationStays={record.LocationStays.Count}; "
            + $"complete={record.IsComplete}; saveId={saveId}",
            LogLevel.Info);
    }

    private void StartNewDay()
    {
        if (!Context.IsWorldReady || Game1.player is null)
            return;

        ResetState();
        saveId = Game1.uniqueIDForThisGame.ToString(CultureInfo.InvariantCulture);
        record = new DailyRecord
        {
            Date = new DailyDate
            {
                Year = Game1.year,
                Season = NormalizeSeason(Game1.currentSeason),
                Day = Game1.dayOfMonth
            },
            Context = BuildDailyContext()
        };

        EnsureCurrentLocation();
        CapturePlayerSnapshot();
    }

    private DailyContext BuildDailyContext()
    {
        bool festival = Utility.isFestivalDay();
        string? festivalLocation = festival && !string.IsNullOrWhiteSpace(Game1.whereIsTodaysFest)
            ? Game1.whereIsTodaysFest
            : null;

        return new DailyContext
        {
            Weather = GetWeatherName(festival),
            IsRaining = Game1.isRaining,
            IsSnowing = Game1.isSnowing,
            IsLightning = Game1.isLightning,
            IsGreenRain = Game1.isGreenRain,
            IsDebrisWeather = Game1.isDebrisWeather,
            IsFestival = festival,
            FestivalLocation = festivalLocation,
            Luck = Game1.player.DailyLuck,
            Spouse = string.IsNullOrWhiteSpace(Game1.player.spouse) ? null : Game1.player.spouse,
            FarmType = Game1.whichFarm
        };
    }

    private static string GetWeatherName(bool festival)
    {
        if (Game1.isGreenRain)
            return "GreenRain";
        if (Game1.isLightning)
            return "Storm";
        if (Game1.isRaining)
            return "Rain";
        if (Game1.isSnowing)
            return "Snow";
        if (festival)
            return "Festival";
        return "Sun";
    }

    private void EnsureCurrentLocation()
    {
        if (Game1.currentLocation is GameLocation location)
            SwitchLocation(location, Game1.timeOfDay);
    }

    private void SwitchLocation(GameLocation location, int enterTime)
    {
        if (!CanRecord())
            return;

        LocationInfo info = GetLocationInfo(location);
        if (activeLocationStay is not null
            && string.Equals(activeLocationStay.Location, info.Id, StringComparison.Ordinal))
        {
            activeLocationStay.LocationDisplayName = info.DisplayName;
            activeLocationStay.IsOutdoors = location.IsOutdoors;
            activeLocationStay.IsTemporary = location.IsTemporary;
            return;
        }

        CloseActiveLocationStay(enterTime);
        activeLocationStay = new LocationStay
        {
            Location = info.Id,
            LocationDisplayName = info.DisplayName,
            EnterTime = enterTime,
            IsOutdoors = location.IsOutdoors,
            IsTemporary = location.IsTemporary
        };
        record!.LocationStays.Add(activeLocationStay);
        LogDebug($"[Timeline] {FormatTime(enterTime)} LocationEntered {info.Id} ({info.DisplayName})");
    }

    private void CloseActiveLocationStay(int leaveTime)
    {
        if (activeLocationStay is null)
            return;

        activeLocationStay.LeaveTime = leaveTime;
        activeLocationStay.Duration = CalculateDuration(activeLocationStay.EnterTime, leaveTime);
        activeLocationStay = null;
    }

    private void UpdateOpenLocationStayDuration()
    {
        if (activeLocationStay is null || !Context.IsWorldReady)
            return;

        activeLocationStay.Duration = CalculateDuration(activeLocationStay.EnterTime, Game1.timeOfDay);
    }

    private void TrackPlayerState()
    {
        Farmer player = Game1.player;
        if (!playerSnapshotReady)
        {
            CapturePlayerSnapshot();
            return;
        }

        int currentHealth = player.health;
        if (lastHealth > 0 && currentHealth <= 0 && !knockoutWasRecorded)
        {
            knockoutWasRecorded = true;
            AddEvent(
                "PlayerKnockedOut",
                Game1.currentLocation,
                PlayerActor,
                null,
                new Dictionary<string, object?>
                {
                    ["health"] = currentHealth
                },
                importance: 4,
                evidence: "Observed");
        }
        else if (currentHealth > 0)
        {
            knockoutWasRecorded = false;
        }

        int moneyDelta = player.Money - lastMoney;
        if (moneyDelta != 0)
        {
            int unknownDelta = ConsumeKnownMoneyDelta(moneyDelta);
            if (unknownDelta != 0)
            {
                AddEvent(
                    "MoneyChanged",
                    Game1.currentLocation,
                    PlayerActor,
                    null,
                    new Dictionary<string, object?>
                    {
                        ["amount"] = unknownDelta,
                        ["reason"] = "Unknown"
                    },
                    importance: 1,
                    evidence: "Unknown");
            }
        }
        else if (pendingKnownMoneyDelta != 0)
        {
            // 本轮没有观察到净变化，说明已知扣款被抵消或采样窗口已失配，不再跨轮次归因。
            ClearKnownMoneyCorrelation();
        }

        lastHealth = currentHealth;
        lastMoney = player.Money;
    }

    private void CapturePlayerSnapshot()
    {
        lastHealth = Game1.player.health;
        lastMoney = Game1.player.Money;
        playerSnapshotReady = true;
    }

    private int ConsumeKnownMoneyDelta(int observedDelta)
    {
        if (pendingKnownMoneyDelta == 0)
            return observedDelta;

        if (Game1.ticks >= pendingKnownMoneyExpiryTick)
        {
            ClearKnownMoneyCorrelation();
            return observedDelta;
        }

        if (Math.Sign(observedDelta) != Math.Sign(pendingKnownMoneyDelta)
            || Math.Abs(observedDelta) < Math.Abs(pendingKnownMoneyDelta))
        {
            // 无法把净变化唯一归因到已知交易时，放弃关联，避免把后续未知变化吞掉。
            ClearKnownMoneyCorrelation();
            return observedDelta;
        }

        int unknownDelta = observedDelta - pendingKnownMoneyDelta;
        ClearKnownMoneyCorrelation();
        return unknownDelta;
    }

    private void ClearKnownMoneyCorrelation()
    {
        pendingKnownMoneyDelta = 0;
        pendingKnownMoneyExpiryTick = -1;
    }

    private void AddEvent(
        string type,
        GameLocation? location,
        string? actor,
        string? target,
        Dictionary<string, object?> details,
        int importance,
        string evidence)
    {
        if (!CanRecord())
            return;

        LocationInfo locationInfo = GetLocationInfo(location);
        GameEvent gameEvent = new()
        {
            Day = record!.Date.Day,
            Time = Game1.timeOfDay,
            RealTimestamp = config.SaveRawEvents ? DateTime.UtcNow : null,
            Type = type,
            Location = locationInfo.Id,
            LocationDisplayName = locationInfo.DisplayName,
            Actor = actor,
            Target = target,
            Details = details,
            Importance = importance,
            Evidence = evidence,
            Sequence = ++nextSequence
        };
        record.Events.Add(gameEvent);
        if (config.SaveRawEvents)
            debugRawEvents.Add(gameEvent);

        LogEvent(gameEvent);
    }

    private void SaveRecord(string reason)
    {
        if (record is null || saveId is null)
            return;

        UpdateOpenLocationStayDuration();
        SortEvents();
        record.DebugRawEvents = config.SaveRawEvents ? new List<GameEvent>(debugRawEvents) : null;
        record.SummaryStats = BuildSummaryStats();

        try
        {
            helper.Data.WriteJsonFile(GetRecordPath(), record);
            if (record.IsComplete && !completionLogged)
            {
                completionLogged = true;
                monitor.Log(
                    "Daily collection completed: "
                    + $"Events: {record.Events.Count}; "
                    + $"Location stays: {record.LocationStays.Count}; "
                    + $"Social events: {record.Events.Count(IsSocialEvent)}; "
                    + $"Combat events: {record.Events.Count(IsCombatEvent)}; "
                    + "Aggregated events: 0.",
                    LogLevel.Info);
            }
            else
            {
                LogDebug($"[Lifecycle] 已写出记录（{reason}）：{GetRecordPath()}");
            }
        }
        catch (Exception ex)
        {
            monitor.Log($"写入当天数据失败（{reason}）：{ex.GetBaseException().Message}", LogLevel.Error);
        }
    }

    private Dictionary<string, object?> BuildSummaryStats()
    {
        return new Dictionary<string, object?>
        {
            ["events"] = record!.Events.Count,
            ["locationStays"] = record.LocationStays.Count,
            ["debugRawEvents"] = config.SaveRawEvents ? debugRawEvents.Count : 0,
            ["observedEvents"] = record.Events.Count(gameEvent => gameEvent.Evidence == "Observed"),
            ["unknownEvents"] = record.Events.Count(gameEvent => gameEvent.Evidence == "Unknown"),
            ["socialEvents"] = record.Events.Count(IsSocialEvent),
            ["combatEvents"] = record.Events.Count(IsCombatEvent),
            ["aggregatedEvents"] = 0,
            ["complete"] = record.IsComplete
        };
    }

    private string GetRecordPath()
    {
        DailyDate date = record!.Date;
        string season = ToFileSeason(date.Season);
        return Path.Combine(
            "data",
            saveId!,
            $"Year{date.Year}-{season}-{date.Day:00}.json");
    }

    private void SortEvents()
    {
        record!.Events.Sort((left, right) =>
        {
            int timeComparison = left.Time.CompareTo(right.Time);
            return timeComparison != 0
                ? timeComparison
                : left.Sequence.CompareTo(right.Sequence);
        });
    }

    private bool CanRecord()
    {
        return record is not null && !finalized && Context.IsWorldReady;
    }

    private void ResetState()
    {
        record = null;
        activeLocationStay = null;
        saveId = null;
        finalized = false;
        completionLogged = false;
        playerSnapshotReady = false;
        knockoutWasRecorded = false;
        lastMoney = 0;
        lastHealth = 0;
        lastTalkTick = -1;
        lastTalkNpc = null;
        lastTalkLocation = null;
        lastPassOutTick = -1;
        checkpointElapsedSeconds = 0;
        pendingKnownMoneyDelta = 0;
        pendingKnownMoneyExpiryTick = -1;
        nextSequence = 0;
        debugRawEvents.Clear();
        talkCounts.Clear();
    }

    private void LogEvent(GameEvent gameEvent)
    {
        if (!config.DebugLogging)
            return;

        string prefix = gameEvent.Type switch
        {
            "NpcTalk" or "GiftGiven" => "[Social]",
            "Purchase" or "MoneyChanged" => "[Economy]",
            _ => "[Timeline]"
        };
        string target = gameEvent.Target is null ? "" : $" target={gameEvent.Target}";
        LogDebug(
            $"{prefix} {FormatTime(gameEvent.Time)} {gameEvent.Type} "
            + $"location={gameEvent.Location}{target}");
    }

    private void LogDebug(string message)
    {
        if (config.DebugLogging)
            monitor.Log(message, LogLevel.Debug);
    }

    private static bool IsSocialEvent(GameEvent gameEvent)
    {
        return gameEvent.Type is "NpcTalk" or "GiftGiven";
    }

    private static bool IsCombatEvent(GameEvent gameEvent)
    {
        return gameEvent.Type == "PlayerKnockedOut";
    }

    private static int CalculateDuration(int enterTime, int leaveTime)
    {
        return Math.Max(0, Utility.CalculateMinutesBetweenTimes(enterTime, leaveTime));
    }

    private static LocationInfo GetLocationInfo(GameLocation? location)
    {
        if (location is null)
            return new LocationInfo("Unknown", "Unknown");

        string id = string.IsNullOrWhiteSpace(location.NameOrUniqueName)
            ? "Unknown"
            : location.NameOrUniqueName;
        string displayName = string.IsNullOrWhiteSpace(location.DisplayName)
            ? location.Name
            : location.DisplayName;
        return new LocationInfo(id, displayName);
    }

    private static string GetGiftTasteName(int taste)
    {
        return taste switch
        {
            0 => "Loved",
            2 => "Liked",
            8 => "Neutral",
            4 => "Disliked",
            6 => "Hated",
            7 => "StardropTea",
            _ => "Unknown"
        };
    }

    private static string NormalizeSeason(string? season)
    {
        return string.IsNullOrWhiteSpace(season) ? "unknown" : season.Trim().ToLowerInvariant();
    }

    private static string ToFileSeason(string season)
    {
        string normalized = NormalizeSeason(season);
        if (normalized.Length == 0)
            return "Unknown";

        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string FormatTime(int time)
    {
        return time.ToString("0000", CultureInfo.InvariantCulture);
    }

    private readonly record struct LocationInfo(string Id, string DisplayName);
}
