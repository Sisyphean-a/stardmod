using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Toolbox;

internal static class QuickStackFeature
{
    private const string ButtonHoverText = "快速堆叠到附近箱子";
    private static readonly ConditionalWeakTable<InventoryPage, ClickableTextureComponent> Buttons = new();
    private static Func<ModConfig> getConfig = null!;
    private static IMonitor monitor = null!;
    private static Texture2D icon = null!;

    internal static bool IsAvailable { get; private set; }

    internal static void Initialize(IModHelper helper, Func<ModConfig> getConfig, IMonitor monitor)
    {
        QuickStackFeature.getConfig = getConfig;
        QuickStackFeature.monitor = monitor;
        icon = helper.ModContent.Load<Texture2D>("assets/quickStackIcon.png");
        IsAvailable = true;
    }

    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(
                typeof(InventoryPage),
                nameof(InventoryPage.receiveLeftClick),
                new[] { typeof(int), typeof(int), typeof(bool) })!,
            prefix: new HarmonyMethod(typeof(QuickStackFeature), nameof(ReceiveLeftClickPrefix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(InventoryPage),
                nameof(InventoryPage.performHoverAction),
                new[] { typeof(int), typeof(int) })!,
            postfix: new HarmonyMethod(typeof(QuickStackFeature), nameof(PerformHoverActionPostfix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(InventoryPage),
                nameof(InventoryPage.draw),
                new[] { typeof(SpriteBatch) })!,
            postfix: new HarmonyMethod(typeof(QuickStackFeature), nameof(DrawPostfix)));
    }

    private static bool IsEnabled()
    {
        return IsAvailable && getConfig().EnableQuickStack && Context.IsWorldReady;
    }

    private static bool ReceiveLeftClickPrefix(InventoryPage __instance, int x, int y, bool playSound)
    {
        if (!IsEnabled()
            || !__instance.readyToClose()
            || !GetButton(__instance).containsPoint(x, y))
        {
            return true;
        }

        StackToNearbyChests();
        return false;
    }

    private static void PerformHoverActionPostfix(InventoryPage __instance, int x, int y)
    {
        if (!IsEnabled())
            return;

        ClickableTextureComponent button = GetButton(__instance);
        button.tryHover(x, y, 0.1f);
        if (button.containsPoint(x, y))
        {
            __instance.hoverText = ButtonHoverText;
            __instance.hoverTitle = string.Empty;
            __instance.hoveredItem = null;
        }
    }

    private static void DrawPostfix(InventoryPage __instance, SpriteBatch b)
    {
        if (!IsEnabled())
            return;

        GetButton(__instance).draw(b);
    }

    private static ClickableTextureComponent GetButton(InventoryPage page)
    {
        return Buttons.GetValue(page, CreateButton);
    }

    private static ClickableTextureComponent CreateButton(InventoryPage page)
    {
        Rectangle organizeBounds = page.organizeButton?.bounds
            ?? new Rectangle(page.xPositionOnScreen + page.width, page.yPositionOnScreen + page.height / 3, 64, 64);
        ClickableTextureComponent button = new(
            "ToolboxQuickStack",
            new Rectangle(organizeBounds.X, organizeBounds.Bottom + 8, organizeBounds.Width, organizeBounds.Height),
            string.Empty,
            ButtonHoverText,
            icon,
            icon.Bounds,
            4f)
        {
            myID = 107,
            upNeighborID = 106,
            downNeighborID = 105,
            leftNeighborID = 11
        };
        return button;
    }

    private static void StackToNearbyChests()
    {
        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        if (location is null)
            return;

        int range = Math.Clamp(getConfig().QuickStackRange, 1, 64);
        Point origin = player.TilePoint;
        List<Chest> chests = location.Objects.Values
            .OfType<Chest>()
            .Where(IsSupportedChest)
            .Where(chest => IsWithinRange(origin, chest.TileLocation, range))
            .OrderBy(chest => DistanceSquared(origin, chest.TileLocation))
            .ToList();

        bool movedAny = false;
        foreach (Chest chest in chests)
        {
            if (chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld())
                continue;

            movedAny |= StackIntoChest(player, chest);
        }

        Game1.playSound(movedAny ? "Ship" : "cancel");
        if (movedAny)
            monitor.Log($"已将背包物品堆叠到范围 {range} 格内的附近箱子。", LogLevel.Trace);
    }

    private static bool StackIntoChest(Farmer player, Chest chest)
    {
        bool movedAny = false;
        Inventory chestItems = chest.Items;
        for (int playerIndex = 0; playerIndex < player.Items.Count; playerIndex++)
        {
            Item? playerItem = player.Items[playerIndex];
            if (playerItem is null || playerItem.Stack <= 0)
                continue;

            bool foundMatchingStack = false;
            for (int chestIndex = 0; chestIndex < chestItems.Count; chestIndex++)
            {
                Item? chestItem = chestItems[chestIndex];
                if (chestItem is null || !chestItem.canStackWith(playerItem))
                    continue;

                foundMatchingStack = true;
                int beforeStack = playerItem.Stack;
                playerItem.Stack = chestItem.addToStack(playerItem);
                if (playerItem.Stack == beforeStack)
                    continue;

                movedAny = true;
                if (playerItem.Stack == 0)
                {
                    player.Items.RemoveButKeepEmptySlot(playerItem);
                    break;
                }
            }

            if (playerItem.Stack > 0
                && foundMatchingStack
                && chestItems.Count < chest.GetActualCapacity())
            {
                int beforeStack = playerItem.Stack;
                Item? remaining = chest.addItem(playerItem);
                if (remaining is null)
                {
                    player.Items.RemoveButKeepEmptySlot(playerItem);
                    movedAny = true;
                }
                else if (remaining.Stack != beforeStack)
                {
                    movedAny = true;
                }
            }
        }

        return movedAny;
    }

    private static bool IsSupportedChest(Chest chest)
    {
        return chest.playerChest.Value
            && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest;
    }

    private static bool IsWithinRange(Point origin, Vector2 target, int range)
    {
        return Math.Abs(origin.X - (int)target.X) <= range
            && Math.Abs(origin.Y - (int)target.Y) <= range;
    }

    private static int DistanceSquared(Point origin, Vector2 target)
    {
        int dx = origin.X - (int)target.X;
        int dy = origin.Y - (int)target.Y;
        return dx * dx + dy * dy;
    }
}
