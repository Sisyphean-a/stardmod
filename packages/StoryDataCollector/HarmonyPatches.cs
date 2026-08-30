using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StoryDataCollector;

internal static class HarmonyPatches
{
    private static TimelineDataCollector? collector;
    private static IMonitor? monitor;
    private static readonly List<string> patchStatuses = new();

    internal static void Apply(Harmony harmony, TimelineDataCollector dataCollector, IMonitor modMonitor)
    {
        collector = dataCollector;
        monitor = modMonitor;
        patchStatuses.Clear();

        TryPatch(
            harmony,
            "NPC.checkAction",
            AccessTools.Method(
                typeof(NPC),
                nameof(NPC.checkAction),
                new[] { typeof(Farmer), typeof(GameLocation) }),
            prefixName: nameof(NpcCheckActionPrefix),
            postfixName: nameof(NpcCheckActionPostfix));
        TryPatch(
            harmony,
            "NPC.receiveGift",
            AccessTools.Method(
                typeof(NPC),
                nameof(NPC.receiveGift),
                new[]
                {
                    typeof(StardewValley.Object),
                    typeof(Farmer),
                    typeof(bool),
                    typeof(float),
                    typeof(bool)
                }),
            prefixName: nameof(ReceiveGiftPrefix),
            postfixName: nameof(ReceiveGiftPostfix));
        TryPatch(
            harmony,
            "GameLocation.doSleep",
            AccessTools.Method(typeof(GameLocation), "doSleep", Type.EmptyTypes),
            prefixName: nameof(DoSleepPrefix));
        TryPatch(
            harmony,
            "Farmer.passOutFromTired",
            AccessTools.Method(
                typeof(Farmer),
                nameof(Farmer.passOutFromTired),
                new[] { typeof(Farmer) }),
            prefixName: nameof(PassOutFromTiredPrefix));
        TryPatch(
            harmony,
            "ShopMenu.tryToPurchaseItem",
            AccessTools.Method(
                typeof(ShopMenu),
                "tryToPurchaseItem",
                new[] { typeof(ISalable), typeof(ISalable), typeof(int), typeof(int), typeof(int) }),
            prefixName: nameof(ShopPurchasePrefix),
            postfixName: nameof(ShopPurchasePostfix));
        TryPatch(
            harmony,
            "Event.tryEventCommand",
            AccessTools.Method(
                typeof(Event),
                nameof(Event.tryEventCommand),
                new[] { typeof(GameLocation), typeof(GameTime), typeof(string[]) }),
            postfixName: nameof(StoryEventCommandPostfix));
        TryPatch(
            harmony,
            "Event.skipEvent",
            AccessTools.Method(typeof(Event), nameof(Event.skipEvent), Type.EmptyTypes),
            postfixName: nameof(StoryEventSkipPostfix));
        TryPatch(
            harmony,
            "Event.exitEvent",
            AccessTools.Method(typeof(Event), nameof(Event.exitEvent), Type.EmptyTypes),
            postfixName: nameof(StoryEventExitPostfix));
        TryPatch(
            harmony,
            "Event.answerDialogue",
            AccessTools.Method(
                typeof(Event),
                nameof(Event.answerDialogue),
                new[] { typeof(string), typeof(int) }),
            prefixName: nameof(StoryEventAnswerPrefix),
            postfixName: nameof(StoryEventAnswerPostfix));
    }

    private static void TryPatch(
        Harmony harmony,
        string description,
        MethodBase? target,
        string? prefixName = null,
        string? postfixName = null)
    {
        if (target is null)
        {
            patchStatuses.Add($"{description}=Unsupported");
            monitor?.Log($"无法找到 {description}；对应采集项标记为 Unsupported。", LogLevel.Warn);
            return;
        }

        try
        {
            harmony.Patch(
                target,
                prefix: prefixName is null ? null : new HarmonyMethod(typeof(HarmonyPatches), prefixName),
                postfix: postfixName is null ? null : new HarmonyMethod(typeof(HarmonyPatches), postfixName));
            patchStatuses.Add($"{description}=Supported");
            monitor?.Log($"[Harmony] 已注册 {description} 采集入口。", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            patchStatuses.Add($"{description}=Unsupported");
            monitor?.Log(
                $"注册 {description} 补丁失败；对应采集项标记为 Unsupported：{ex.GetBaseException().Message}",
                LogLevel.Warn);
        }
    }

    internal static void LogStatus(IMonitor destination)
    {
        destination.Log(
            patchStatuses.Count == 0
                ? "[StoryDataCollector] 尚未建立 Harmony 入口状态。"
                : $"[StoryDataCollector] Harmony 入口状态：{string.Join(", ", patchStatuses)}",
            LogLevel.Info);
    }

    private static void NpcCheckActionPrefix(
        NPC __instance,
        Farmer who,
        GameLocation l,
        out NpcInteractionState __state)
    {
        __state = new NpcInteractionState();
        try
        {
            __state.ShouldConsider = Context.IsWorldReady
                && ReferenceEquals(who, Game1.player)
                && who.ActiveObject is null
                && !Game1.dialogueUp;
            __state.DialogueWasUp = Game1.dialogueUp;
            __state.Location = l;
            __state.InvocationTick = Game1.ticks;
        }
        catch (Exception ex)
        {
            __state.ShouldConsider = false;
            LogCallbackFailure("NPC.checkAction 前置回调", ex);
        }
    }

    private static void NpcCheckActionPostfix(
        NPC __instance,
        bool __result,
        bool __runOriginal,
        NpcInteractionState __state)
    {
        try
        {
            if (!__state.ShouldConsider
                || !__runOriginal
                || __state.DialogueWasUp
                || !Game1.dialogueUp)
            {
                return;
            }

            collector?.RecordNpcTalk(__instance, __state.Location, __state.InvocationTick);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("NPC.checkAction 后置回调", ex);
        }
    }

    private static void ReceiveGiftPrefix(
        NPC __instance,
        StardewValley.Object o,
        Farmer giver,
        out GiftPatchState __state)
    {
        __state = new GiftPatchState();
        try
        {
            if (!Context.IsWorldReady
                || collector is null
                || !ReferenceEquals(giver, Game1.player)
                || !__instance.CanReceiveGifts())
            {
                return;
            }

            __state.ShouldRecord = true;
            __state.Taste = __instance.getGiftTasteForThisItem(o);
            __state.Birthday = __instance.isBirthday();
        }
        catch (Exception ex)
        {
            __state.ShouldRecord = false;
            LogCallbackFailure("NPC.receiveGift 前置回调", ex);
        }
    }

    private static void ReceiveGiftPostfix(
        NPC __instance,
        StardewValley.Object o,
        float friendshipChangeMultiplier,
        bool __runOriginal,
        GiftPatchState __state)
    {
        try
        {
            if (!__state.ShouldRecord || !__runOriginal)
                return;

            collector?.RecordGift(
                __instance,
                o,
                __state.Taste,
                __state.Birthday,
                friendshipChangeMultiplier);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("NPC.receiveGift 后置回调", ex);
        }
    }

    private static void DoSleepPrefix(GameLocation __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal
                && Context.IsWorldReady
                && ReferenceEquals(Game1.currentLocation, __instance))
            {
                collector?.RecordSleep(__instance);
            }
        }
        catch (Exception ex)
        {
            LogCallbackFailure("GameLocation.doSleep 回调", ex);
        }
    }

    private static void PassOutFromTiredPrefix(Farmer who, bool __runOriginal)
    {
        try
        {
            if (__runOriginal && Context.IsWorldReady && ReferenceEquals(who, Game1.player))
                collector?.RecordPassedOut("Farmer.passOutFromTired");
        }
        catch (Exception ex)
        {
            LogCallbackFailure("Farmer.passOutFromTired 回调", ex);
        }
    }

    private static void StoryEventCommandPostfix(
        Event __instance,
        string[] args,
        bool __runOriginal)
    {
        try
        {
            if (__runOriginal && Context.IsWorldReady)
                collector?.RecordStoryEventCommand(__instance, args);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("Event.tryEventCommand 回调", ex);
        }
    }

    private static void StoryEventSkipPostfix(Event __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal && Context.IsWorldReady)
                collector?.MarkStoryEventEnding(__instance, skipped: true);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("Event.skipEvent 回调", ex);
        }
    }

    private static void StoryEventExitPostfix(Event __instance, bool __runOriginal)
    {
        try
        {
            if (__runOriginal && Context.IsWorldReady)
                collector?.MarkStoryEventEnding(__instance, skipped: false);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("Event.exitEvent 回调", ex);
        }
    }

    private static void StoryEventAnswerPrefix(Event __instance, out string? __state)
    {
        try
        {
            __state = __instance.GetCurrentCommand();
        }
        catch (Exception ex)
        {
            __state = null;
            LogCallbackFailure("Event.answerDialogue 前置回调", ex);
        }
    }

    private static void StoryEventAnswerPostfix(
        Event __instance,
        string questionKey,
        int answerChoice,
        bool __runOriginal,
        string? __state)
    {
        try
        {
            if (__runOriginal && Context.IsWorldReady)
            {
                collector?.RecordStoryEventChoice(
                    __instance,
                    questionKey,
                    answerChoice,
                    __state);
            }
        }
        catch (Exception ex)
        {
            LogCallbackFailure("Event.answerDialogue 后置回调", ex);
        }
    }

    private static void ShopPurchasePrefix(
        ShopMenu __instance,
        ISalable item,
        ISalable? held_item,
        int stockToBuy,
        out PurchasePatchState __state)
    {
        __state = new PurchasePatchState();
        try
        {
            if (!Context.IsWorldReady || collector is null || Game1.player is null)
                return;

            __state.ShouldConsider = true;
            __state.Currency = __instance.currency;
            __state.MoneyBefore = Game1.player.Money;
            __state.RequestedStock = Math.Max(1, stockToBuy);
            __state.ItemStack = Math.Max(1, item.Stack);
            __state.HeldItemBeforeQualifiedId = __instance.heldItem?.QualifiedItemId;
            __state.HeldItemBeforeName = __instance.heldItem?.Name;
            __state.HeldItemBeforeStack = __instance.heldItem?.Stack;
            __state.ShopId = __instance.ShopId;

            if (__instance.itemPriceAndStock.TryGetValue(item, out var stock))
            {
                __state.StockBefore = stock.Stock;
                __state.Price = stock.Price;
            }
        }
        catch (Exception ex)
        {
            __state.ShouldConsider = false;
            LogCallbackFailure("ShopMenu.tryToPurchaseItem 前置回调", ex);
        }
    }

    private static void ShopPurchasePostfix(
        ShopMenu __instance,
        ISalable item,
        bool __runOriginal,
        PurchasePatchState __state)
    {
        try
        {
            if (!__state.ShouldConsider
                || !__runOriginal
                || collector is null
                || Game1.player is null)
            {
                return;
            }

            int moneyDelta = Game1.player.Money - __state.MoneyBefore;
            bool succeeded = __state.Currency == 0 && moneyDelta < 0;
            if (!succeeded
                && __state.StockBefore.HasValue
                && __instance.itemPriceAndStock.TryGetValue(item, out var stockAfter)
                && stockAfter.Stock < __state.StockBefore.Value)
            {
                succeeded = true;
            }

            if (!succeeded && __state.HeldItemBeforeStack.HasValue && __instance.heldItem is not null)
            {
                succeeded = IsSameSalable(
                    __state.HeldItemBeforeQualifiedId,
                    __state.HeldItemBeforeName,
                    __instance.heldItem)
                    && __state.HeldItemBeforeStack.HasValue
                    && __instance.heldItem.Stack > __state.HeldItemBeforeStack.Value;
            }
            else if (!succeeded
                && !__state.HeldItemBeforeStack.HasValue
                && __instance.heldItem is not null)
            {
                succeeded = IsSameSalable(item, __instance.heldItem);
            }

            if (!succeeded)
                return;

            int count = GetPurchasedCount(__instance, item, __state, moneyDelta);
            int purchasedUnits = GetPurchasedUnits(count, __state.ItemStack);
            int cost = __state.Currency == 0
                ? Math.Max(0, -moneyDelta)
                : Math.Max(0, __state.Price * purchasedUnits);
            string? shopTarget = (__instance.source as NPC)?.Name;
            collector.RecordPurchase(
                item,
                __state.ShopId,
                shopTarget,
                count,
                cost,
                __state.Currency,
                __state.Price,
                moneyDelta);
            if (moneyDelta < 0)
                collector.MarkKnownMoneyChange(moneyDelta);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("ShopMenu.tryToPurchaseItem 后置回调", ex);
        }
    }

    private static int GetPurchasedCount(
        ShopMenu shop,
        ISalable item,
        PurchasePatchState state,
        int moneyDelta)
    {
        if (state.HeldItemBeforeStack.HasValue
            && shop.heldItem is not null
            && IsSameSalable(
                state.HeldItemBeforeQualifiedId,
                state.HeldItemBeforeName,
                shop.heldItem))
        {
            int difference = shop.heldItem.Stack - state.HeldItemBeforeStack.Value;
            if (difference > 0)
                return difference;
        }

        if (!state.HeldItemBeforeStack.HasValue
            && shop.heldItem is not null
            && IsSameSalable(item, shop.heldItem))
        {
            return Math.Max(1, shop.heldItem.Stack);
        }

        if (state.StockBefore.HasValue
            && shop.itemPriceAndStock.TryGetValue(item, out var stockAfter)
            && stockAfter.Stock < state.StockBefore.Value)
        {
            int purchasedUnits = state.StockBefore.Value - stockAfter.Stock;
            return Math.Max(1, state.ItemStack * purchasedUnits);
        }

        if (state.Currency == 0
            && state.Price > 0
            && moneyDelta < 0
            && moneyDelta % state.Price == 0)
        {
            int purchasedUnits = -moneyDelta / state.Price;
            return Math.Max(1, state.ItemStack * purchasedUnits);
        }

        return Math.Max(1, state.ItemStack * state.RequestedStock);
    }

    private static int GetPurchasedUnits(int count, int itemStack)
    {
        return Math.Max(1, (int)(((long)count + itemStack - 1) / itemStack));
    }

    private static bool IsSameSalable(ISalable first, ISalable second)
    {
        return string.Equals(first.QualifiedItemId, second.QualifiedItemId, StringComparison.Ordinal)
            && string.Equals(first.Name, second.Name, StringComparison.Ordinal);
    }

    private static bool IsSameSalable(
        string? qualifiedItemId,
        string? name,
        ISalable candidate)
    {
        return string.Equals(qualifiedItemId, candidate.QualifiedItemId, StringComparison.Ordinal)
            && string.Equals(name, candidate.Name, StringComparison.Ordinal);
    }

    private static void LogCallbackFailure(string callback, Exception exception)
    {
        monitor?.Log(
            $"{callback}失败，已跳过本次采集：{exception.GetBaseException().Message}",
            LogLevel.Warn);
    }

    private sealed class NpcInteractionState
    {
        internal bool ShouldConsider { get; set; }

        internal bool DialogueWasUp { get; set; }

        internal GameLocation? Location { get; set; }

        internal int InvocationTick { get; set; }
    }

    private sealed class GiftPatchState
    {
        internal bool ShouldRecord { get; set; }

        internal int Taste { get; set; }

        internal bool Birthday { get; set; }
    }

    private sealed class PurchasePatchState
    {
        internal bool ShouldConsider { get; set; }

        internal int Currency { get; set; }

        internal int MoneyBefore { get; set; }

        internal int RequestedStock { get; set; }

        internal int ItemStack { get; set; }

        internal int? StockBefore { get; set; }

        internal int Price { get; set; }

        internal string ShopId { get; set; } = "Unknown";

        internal string? HeldItemBeforeQualifiedId { get; set; }

        internal string? HeldItemBeforeName { get; set; }

        internal int? HeldItemBeforeStack { get; set; }
    }
}
