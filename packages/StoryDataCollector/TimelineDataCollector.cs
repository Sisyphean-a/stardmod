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
    private readonly NarrativeProjectionBuilder narrativeProjectionBuilder = new();
    private readonly Dictionary<string, int> talkCounts = new(StringComparer.Ordinal);

    private DailyRecord? record;
    private LocationStay? activeLocationStay;
    private bool activeLocationStayIsStored;
    private string? saveId;
    private bool finalized;
    private bool playerSnapshotReady;
    private bool knockoutWasRecorded;
    private bool completionLogged;
    private bool finalRecordWritten;
    private int lastMoney;
    private int lastHealth;
    private int lastTalkTick = -1;
    private string? lastTalkNpc;
    private string? lastTalkLocation;
    private int lastPassOutTick = -1;
    private int checkpointElapsedSeconds;
    private int locationRevision;
    private int checkpointedLocationRevision = -1;
    private int recordRevision;
    private int checkpointedRecordRevision = -1;
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
        saveId = Game1.uniqueIDForThisGame.ToString(CultureInfo.InvariantCulture);
        RestoreIncompleteCheckpoints();
        LogDebug(record is null
            ? "[Lifecycle] SaveLoaded：等待 DayStarted 建立当天记录。"
            : "[Lifecycle] SaveLoaded：已从 checkpoint 恢复当天记录。");
    }

    internal void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (record is not null && finalized)
        {
            if (!finalRecordWritten)
                WriteDailyRecord("day-start-finalize", writeNarrativeInput: true);
            else if (EnsureNarrativeInput(record, saveId!))
                ClearCheckpoints(record.Date);
        }
        else if (record is not null)
        {
            if (IsRecordForCurrentGameDay())
                return;

            WriteDailyRecord("day-start-recovery", writeNarrativeInput: false);
        }

        StartNewDay();
        LogDebug($"[Lifecycle] DayStarted：开始采集 Year{record?.Date.Year}-{record?.Date.Season}-{record?.Date.Day:00}。");
    }

    internal void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (record is null || finalized)
            return;

        EnsureCurrentLocation();
        CloseActiveLocationStay(Game1.timeOfDay);
        record.EndState = CaptureDayState();
        finalized = true;
        record.IsComplete = true;
        WriteDailyRecord("day-ending", writeNarrativeInput: true);
    }

    internal void OnSaving(object? sender, SavingEventArgs e)
    {
        if (record is not null && !finalized)
            WriteCheckpoint("saving", force: true);
    }

    internal void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        if (record is not null && !finalized)
            WriteCheckpoint("returned-to-title", force: true);

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
            WriteCheckpoint("periodic-checkpoint", force: false);
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

        WriteCheckpoint("console", force: true);
    }

    internal void LogStatus()
    {
        if (record is null)
        {
            monitor.Log("[StoryDataCollector] 当前没有正在采集的当天记录。", LogLevel.Info);
            return;
        }

        int droppedEvents = record.DroppedEventCounts.Values.Sum();
        monitor.Log(
            $"[StoryDataCollector] Year{record.Date.Year}-{record.Date.Season}-{record.Date.Day:00}; "
            + $"storedEvents={record.Events.Count}/{config.MaxEventsPerDay}; droppedEvents={droppedEvents}; "
            + $"locationStays={record.LocationStays.Count}/{config.MaxLocationStaysPerDay}; "
            + $"droppedLocationStays={record.DroppedLocationStays}; narrativeBudget={config.MaxNarrativeFacts}; "
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
            Context = BuildDailyContext(),
            StartState = CaptureDayState()
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

    private PlayerDayState CaptureDayState()
    {
        LocationInfo location = GetLocationInfo(Game1.currentLocation);
        return new PlayerDayState
        {
            Time = Game1.timeOfDay,
            Money = Game1.player.Money,
            Health = Game1.player.health,
            Location = location.Id,
            LocationDisplayName = location.DisplayName,
            Inventory = CaptureInventory(Game1.player)
        };
    }

    private static List<InventoryStack> CaptureInventory(Farmer player)
    {
        List<InventoryStack> inventory = new();
        foreach (Item? item in player.Items)
        {
            if (item is null || item.Stack <= 0)
                continue;

            inventory.Add(new InventoryStack
            {
                ItemId = string.IsNullOrWhiteSpace(item.QualifiedItemId) ? item.Name : item.QualifiedItemId,
                ItemName = item.DisplayName,
                Quality = item is StardewValley.Object gameObject ? gameObject.Quality : 0,
                Count = item.Stack
            });
        }

        return inventory;
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
        if (record!.LocationStays.Count < config.MaxLocationStaysPerDay)
        {
            record.LocationStays.Add(activeLocationStay);
            activeLocationStayIsStored = true;
            locationRevision++;
        }
        else
        {
            activeLocationStayIsStored = false;
            record.DroppedLocationStays++;
            recordRevision++;
            LogDebug($"[Budget] 已省略地点区间 location={info.Id}; 上限={config.MaxLocationStaysPerDay}。");
        }

        LogDebug($"[Timeline] {FormatTime(enterTime)} LocationEntered {info.Id} ({info.DisplayName})");
    }

    private void CloseActiveLocationStay(int leaveTime)
    {
        if (activeLocationStay is null)
            return;

        if (activeLocationStayIsStored)
        {
            activeLocationStay.LeaveTime = leaveTime;
            activeLocationStay.Duration = CalculateDuration(activeLocationStay.EnterTime, leaveTime);
            locationRevision++;
        }

        activeLocationStay = null;
        activeLocationStayIsStored = false;
    }

    private void UpdateOpenLocationStayDuration()
    {
        if (activeLocationStay is null || !activeLocationStayIsStored || !Context.IsWorldReady)
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

        bool stored = DailyEventBudget.TryAdd(record, gameEvent, config.MaxEventsPerDay);
        recordRevision++;
        if (stored)
            LogEvent(gameEvent);
        else
            LogDebug($"[Budget] 已省略低价值事件 type={gameEvent.Type}; 上限={config.MaxEventsPerDay}。");
    }

    private void WriteCheckpoint(string reason, bool force)
    {
        if (record is null || saveId is null || finalized)
            return;

        UpdateOpenLocationStayDuration();
        bool hasRecordChanges = recordRevision != checkpointedRecordRevision;
        bool hasLocationChanges = locationRevision != checkpointedLocationRevision;
        if (!force && !hasRecordChanges && !hasLocationChanges)
            return;

        DailyCheckpoint checkpoint = new()
        {
            Date = CopyDate(record.Date),
            Context = CopyContext(record.Context),
            StartState = CopyState(record.StartState),
            Events = record.Events.OrderBy(gameEvent => gameEvent.Sequence).ToList(),
            LocationStays = record.LocationStays.Select(CopyLocationStay).ToList(),
            DroppedEventCounts = new Dictionary<string, int>(record.DroppedEventCounts, StringComparer.Ordinal),
            DroppedLocationStays = record.DroppedLocationStays,
            LastSequence = nextSequence
        };

        try
        {
            helper.Data.WriteJsonFile(GetCheckpointPath(), checkpoint);
            WritePendingCheckpointPointer(record.Date);
            checkpointedRecordRevision = recordRevision;
            checkpointedLocationRevision = locationRevision;
            LogDebug($"[Lifecycle] 已写出有界 checkpoint 快照（{reason}）：events={checkpoint.Events.Count}。 ");
        }
        catch (Exception ex)
        {
            monitor.Log($"写入当天 checkpoint 失败（{reason}）：{ex.GetBaseException().Message}", LogLevel.Error);
        }
    }

    private bool WriteDailyRecord(string reason, bool writeNarrativeInput)
    {
        if (record is null || saveId is null)
            return false;

        UpdateOpenLocationStayDuration();
        SortEvents();
        NarrativeDailyInput? narrativeInput = writeNarrativeInput
            ? narrativeProjectionBuilder.Build(record, config.MaxNarrativeFacts)
            : null;
        record.SummaryStats = BuildSummaryStats(record, narrativeInput);

        try
        {
            helper.Data.WriteJsonFile(GetRecordPath(), record);
            if (record.IsComplete)
                finalRecordWritten = true;
        }
        catch (Exception ex)
        {
            monitor.Log($"写入当天数据失败（{reason}）：{ex.GetBaseException().Message}", LogLevel.Error);
            return false;
        }

        if (narrativeInput is not null)
        {
            try
            {
                WritePendingCheckpointPointer(record.Date);
                helper.Data.WriteJsonFile(GetNarrativeInputPath(), narrativeInput);
                ClearCheckpoints(record.Date);
            }
            catch (Exception ex)
            {
                monitor.Log($"写入叙事输入失败（{reason}）：{ex.GetBaseException().Message}", LogLevel.Error);
                return false;
            }
        }

        if (record.IsComplete && !completionLogged)
        {
            completionLogged = true;
            monitor.Log(
                "Daily collection completed: "
                + $"Stored events: {record.Events.Count}; "
                + $"Dropped events: {record.DroppedEventCounts.Values.Sum()}; "
                + $"Location stays: {record.LocationStays.Count}; "
                + $"Narrative facts: {narrativeInput?.Facts.Count ?? 0}/{config.MaxNarrativeFacts}.",
                LogLevel.Info);
        }
        else
        {
            LogDebug($"[Lifecycle] 已写出当天记录（{reason}）：{GetRecordPath()}");
        }

        return true;
    }

    private Dictionary<string, object?> BuildSummaryStats(
        DailyRecord dailyRecord,
        NarrativeDailyInput? narrativeInput)
    {
        int droppedEvents = dailyRecord.DroppedEventCounts.Values.Sum();
        return new Dictionary<string, object?>
        {
            ["storedEvents"] = dailyRecord.Events.Count,
            ["droppedEvents"] = droppedEvents,
            ["locationStays"] = dailyRecord.LocationStays.Count,
            ["droppedLocationStays"] = dailyRecord.DroppedLocationStays,
            ["observedEvents"] = dailyRecord.Events.Count(gameEvent => gameEvent.Evidence == "Observed"),
            ["unknownEvents"] = dailyRecord.Events.Count(gameEvent => gameEvent.Evidence == "Unknown"),
            ["socialEvents"] = dailyRecord.Events.Count(IsSocialEvent),
            ["combatEvents"] = dailyRecord.Events.Count(IsCombatEvent),
            ["narrativeFacts"] = narrativeInput?.Facts.Count ?? 0,
            ["narrativeBudget"] = config.MaxNarrativeFacts,
            ["complete"] = dailyRecord.IsComplete
        };
    }

    private void RestoreIncompleteCheckpoints()
    {
        if (saveId is null)
            return;

        DailyDate currentDate = new()
        {
            Year = Game1.year,
            Season = NormalizeSeason(Game1.currentSeason),
            Day = Game1.dayOfMonth
        };
        HashSet<string> checkpointPaths = new(StringComparer.Ordinal)
        {
            GetAbsolutePath(GetCheckpointPath(currentDate))
        };
        List<DailyDate> pendingDates = GetPendingDates(ReadCheckpointPointer());
        DailyDate? additionalDate = pendingDates.FirstOrDefault(date => !SameDate(date, currentDate));
        if (additionalDate is not null)
            checkpointPaths.Add(GetAbsolutePath(GetCheckpointPath(additionalDate)));

        foreach (string checkpointPath in checkpointPaths.Where(File.Exists))
        {
            DailyRecord? recovered = TryRecoverCheckpointFile(checkpointPath);
            if (recovered is not null && IsDateForCurrentGameDay(recovered.Date))
                RestoreCurrentDayRecord(recovered);
        }

        CompletePendingNarrativeInput(currentDate);
        if (additionalDate is not null)
            CompletePendingNarrativeInput(additionalDate);
    }

    private DailyRecord? TryRecoverCheckpointFile(string checkpointPath)
    {
        DailyCheckpoint? checkpoint = ReadCheckpoint(checkpointPath);
        if (checkpoint is null)
            return null;
        if (!CheckpointValidator.IsValid(checkpoint))
        {
            monitor.Log($"checkpoint 校验失败，已保留原文件等待人工处理：{checkpointPath}", LogLevel.Warn);
            return null;
        }

        try
        {
            string recordPath = GetRecordPath(saveId!, checkpoint.Date);
            DailyRecord? existing = File.Exists(GetAbsolutePath(recordPath))
                ? helper.Data.ReadJsonFile<DailyRecord>(recordPath)
                : null;
            if (existing?.IsComplete == true)
            {
                if (!EnsureNarrativeInput(existing, saveId!))
                    return null;

                ClearCheckpoints(checkpoint.Date);
                return null;
            }

            DailyRecord recovered = new()
            {
                Date = CopyDate(checkpoint.Date),
                Context = CopyContext(checkpoint.Context),
                StartState = CopyState(existing?.StartState ?? checkpoint.StartState),
                EndState = null,
                DroppedEventCounts = new Dictionary<string, int>(checkpoint.DroppedEventCounts, StringComparer.Ordinal),
                DroppedLocationStays = checkpoint.DroppedLocationStays,
                IsComplete = false
            };
            foreach (GameEvent gameEvent in checkpoint.Events
                         .OrderBy(gameEvent => gameEvent.Time)
                         .ThenBy(gameEvent => gameEvent.Sequence))
            {
                DailyEventBudget.TryAdd(recovered, gameEvent, config.MaxEventsPerDay);
            }
            foreach (LocationStay locationStay in checkpoint.LocationStays.OrderBy(stay => stay.EnterTime))
            {
                if (recovered.LocationStays.Count < config.MaxLocationStaysPerDay)
                    recovered.LocationStays.Add(CopyLocationStay(locationStay));
                else
                    recovered.DroppedLocationStays++;
            }

            recovered.SummaryStats = BuildSummaryStats(recovered, narrativeInput: null);
            helper.Data.WriteJsonFile(recordPath, recovered);
            ClearCheckpoints(checkpoint.Date);
            monitor.Log(
                $"已从 checkpoint 恢复未完成的 Year{recovered.Date.Year}-{recovered.Date.Season}-{recovered.Date.Day:00} 记录：{recovered.Events.Count} 个事件。",
                LogLevel.Info);
            return recovered;
        }
        catch (Exception ex)
        {
            monitor.Log($"恢复 checkpoint 失败（{checkpointPath}）：{ex.GetBaseException().Message}", LogLevel.Error);
            return null;
        }
    }

    private void CompletePendingNarrativeInput(DailyDate date)
    {
        string recordPath = GetRecordPath(saveId!, date);
        if (!File.Exists(GetAbsolutePath(recordPath)))
            return;

        try
        {
            DailyRecord? completedRecord = helper.Data.ReadJsonFile<DailyRecord>(recordPath);
            if (completedRecord?.IsComplete == true && EnsureNarrativeInput(completedRecord, saveId!))
                ClearCheckpoints(date);
        }
        catch (Exception ex)
        {
            monitor.Log($"补写待完成叙事输入失败：{ex.GetBaseException().Message}", LogLevel.Error);
        }
    }

    private bool EnsureNarrativeInput(DailyRecord completedRecord, string currentSaveId)
    {
        string narrativePath = GetNarrativeInputPath(currentSaveId, completedRecord.Date);
        if (File.Exists(GetAbsolutePath(narrativePath)))
            return true;

        try
        {
            NarrativeDailyInput narrativeInput = narrativeProjectionBuilder.Build(completedRecord, config.MaxNarrativeFacts);
            helper.Data.WriteJsonFile(narrativePath, narrativeInput);
            monitor.Log(
                $"已补写 Year{completedRecord.Date.Year}-{completedRecord.Date.Season}-{completedRecord.Date.Day:00} 的叙事输入。",
                LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log($"补写叙事输入失败：{ex.GetBaseException().Message}", LogLevel.Error);
            return false;
        }
    }

    private DailyCheckpoint? ReadCheckpoint(string absolutePath)
    {
        try
        {
            return helper.Data.ReadJsonFile<DailyCheckpoint>(GetRelativePath(absolutePath));
        }
        catch (Exception ex)
        {
            monitor.Log($"读取 checkpoint 失败（{absolutePath}）：{ex.GetBaseException().Message}", LogLevel.Warn);
            return null;
        }
    }

    private void RestoreCurrentDayRecord(DailyRecord recovered)
    {
        record = recovered;
        finalized = false;
        completionLogged = false;
        nextSequence = recovered.Events.Count == 0 ? 0 : recovered.Events.Max(gameEvent => gameEvent.Sequence);
        checkpointedRecordRevision = -1;
        checkpointedLocationRevision = -1;
        recordRevision = 0;
        locationRevision = 0;
        activeLocationStay = recovered.LocationStays.LastOrDefault(stay => !stay.LeaveTime.HasValue);
        activeLocationStayIsStored = activeLocationStay is not null;
        foreach (GameEvent gameEvent in recovered.Events.Where(gameEvent => gameEvent.Type == "NpcTalk" && gameEvent.Target is not null))
        {
            talkCounts.TryGetValue(gameEvent.Target!, out int count);
            talkCounts[gameEvent.Target!] = count + 1;
        }

        CapturePlayerSnapshot();
        EnsureCurrentLocation();
    }

    private void ClearCheckpoints(DailyDate date)
    {
        ClearCheckpointFile(GetAbsolutePath(GetCheckpointPath(date)));
        CheckpointPointer? pending = ReadCheckpointPointer();
        if (pending is null)
            return;

        List<DailyDate> remaining = GetPendingDates(pending)
            .Where(candidate => !SameDate(candidate, date))
            .Select(CopyDate)
            .ToList();
        try
        {
            if (remaining.Count == 0)
                ClearCheckpointFile(GetAbsolutePath(GetCheckpointPointerPath()));
            else
                helper.Data.WriteJsonFile(GetCheckpointPointerPath(), new CheckpointPointer { PendingDates = remaining });
        }
        catch (Exception ex)
        {
            monitor.Log($"更新 checkpoint 指针失败：{ex.GetBaseException().Message}", LogLevel.Warn);
        }
    }

    private void WritePendingCheckpointPointer(DailyDate date)
    {
        string pointerPath = GetAbsolutePath(GetCheckpointPointerPath());
        CheckpointPointer? existing = ReadCheckpointPointer();
        if (existing is null && File.Exists(pointerPath))
            throw new InvalidDataException("无法读取现有 checkpoint 指针，已保留原文件。");

        List<DailyDate> pendingDates = GetPendingDates(existing);
        if (!pendingDates.Any(candidate => SameDate(candidate, date)))
            pendingDates.Add(CopyDate(date));
        helper.Data.WriteJsonFile(GetCheckpointPointerPath(), new CheckpointPointer { PendingDates = pendingDates });
    }

    private CheckpointPointer? ReadCheckpointPointer()
    {
        string pointerPath = GetAbsolutePath(GetCheckpointPointerPath());
        if (!File.Exists(pointerPath))
            return null;

        try
        {
            CheckpointPointer? pointer = helper.Data.ReadJsonFile<CheckpointPointer>(GetCheckpointPointerPath());
            if (pointer is not null && pointer.SchemaVersion == 1 && pointer.PendingDates is not null)
                return pointer;

            monitor.Log("checkpoint 指针格式无效，已保留原文件等待人工处理。", LogLevel.Warn);
            return null;
        }
        catch (Exception ex)
        {
            monitor.Log($"读取 checkpoint 指针失败：{ex.GetBaseException().Message}", LogLevel.Warn);
            return null;
        }
    }

    private static List<DailyDate> GetPendingDates(CheckpointPointer? pointer)
    {
        return pointer?.PendingDates
            .Where(IsValidDate)
            .GroupBy(date => $"{date.Year}|{date.Season}|{date.Day}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(date => date.Year)
            .ThenBy(date => date.Season, StringComparer.Ordinal)
            .ThenBy(date => date.Day)
            .ToList()
            ?? new List<DailyDate>();
    }

    private static void ClearCheckpointFile(string checkpointPath)
    {
        if (File.Exists(checkpointPath))
            File.Delete(checkpointPath);
    }

    private string GetRecordPath()
    {
        return GetRecordPath(saveId!, record!.Date);
    }

    private static string GetRecordPath(string currentSaveId, DailyDate date)
    {
        return Path.Combine("data", currentSaveId, GetDateFileName(date) + ".json");
    }

    private string GetNarrativeInputPath()
    {
        return GetNarrativeInputPath(saveId!, record!.Date);
    }

    private static string GetNarrativeInputPath(string currentSaveId, DailyDate date)
    {
        return Path.Combine("data", currentSaveId, "narrative-input", GetDateFileName(date) + ".json");
    }

    private string GetCheckpointPath()
    {
        return GetCheckpointPath(record!.Date);
    }

    private string GetCheckpointPath(DailyDate date)
    {
        return Path.Combine("data", saveId!, "checkpoints", GetDateFileName(date) + ".json");
    }

    private string GetCheckpointPointerPath()
    {
        return Path.Combine("data", saveId!, "checkpoints", "pending.json");
    }

    private string GetAbsolutePath(string relativePath)
    {
        return Path.Combine(helper.DirectoryPath, relativePath);
    }

    private string GetRelativePath(string absolutePath)
    {
        return Path.GetRelativePath(helper.DirectoryPath, absolutePath);
    }

    private static string GetDateFileName(DailyDate date)
    {
        return $"Year{date.Year}-{ToFileSeason(date.Season)}-{date.Day:00}";
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

    private bool IsRecordForCurrentGameDay()
    {
        return record is not null && IsDateForCurrentGameDay(record.Date);
    }

    private static bool IsDateForCurrentGameDay(DailyDate date)
    {
        return date.Year == Game1.year
            && date.Day == Game1.dayOfMonth
            && string.Equals(date.Season, NormalizeSeason(Game1.currentSeason), StringComparison.Ordinal);
    }

    private static bool IsValidDate(DailyDate? date)
    {
        return date is not null
            && date.Year >= 1
            && date.Day is >= 1 and <= 28
            && !string.IsNullOrWhiteSpace(date.Season);
    }

    private static bool SameDate(DailyDate? left, DailyDate? right)
    {
        return left is not null
            && right is not null
            && left.Year == right.Year
            && left.Day == right.Day
            && string.Equals(left.Season, right.Season, StringComparison.Ordinal);
    }

    private void ResetState()
    {
        record = null;
        activeLocationStay = null;
        activeLocationStayIsStored = false;
        saveId = null;
        finalized = false;
        completionLogged = false;
        finalRecordWritten = false;
        playerSnapshotReady = false;
        knockoutWasRecorded = false;
        lastMoney = 0;
        lastHealth = 0;
        lastTalkTick = -1;
        lastTalkNpc = null;
        lastTalkLocation = null;
        lastPassOutTick = -1;
        checkpointElapsedSeconds = 0;
        locationRevision = 0;
        checkpointedLocationRevision = -1;
        recordRevision = 0;
        checkpointedRecordRevision = -1;
        pendingKnownMoneyDelta = 0;
        pendingKnownMoneyExpiryTick = -1;
        nextSequence = 0;
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

    private static PlayerDayState? CopyState(PlayerDayState? state)
    {
        return state is null
            ? null
            : new PlayerDayState
            {
                Time = state.Time,
                Money = state.Money,
                Health = state.Health,
                Location = state.Location,
                LocationDisplayName = state.LocationDisplayName,
                Inventory = state.Inventory.Select(stack => new InventoryStack
                {
                    ItemId = stack.ItemId,
                    ItemName = stack.ItemName,
                    Quality = stack.Quality,
                    Count = stack.Count
                }).ToList()
            };
    }

    private static LocationStay CopyLocationStay(LocationStay stay)
    {
        return new LocationStay
        {
            Location = stay.Location,
            LocationDisplayName = stay.LocationDisplayName,
            EnterTime = stay.EnterTime,
            LeaveTime = stay.LeaveTime,
            Duration = stay.Duration,
            IsOutdoors = stay.IsOutdoors,
            IsTemporary = stay.IsTemporary,
            Evidence = stay.Evidence
        };
    }

    private readonly record struct LocationInfo(string Id, string DisplayName);
}
