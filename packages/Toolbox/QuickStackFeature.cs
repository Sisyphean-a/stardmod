using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
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
        ClickableComponent? leftRing = page.equipmentIcons
            .FirstOrDefault(component => component.name == "Left Ring");
        Rectangle buttonBounds;
        if (leftRing is not null)
        {
            // Use the empty margin to the left of the equipment column so the button doesn't overlap the portrait.
            int x = leftRing.bounds.X - leftRing.bounds.Width - 8;
            if (x < page.xPositionOnScreen + 8)
                x = leftRing.bounds.Right + 8;

            buttonBounds = new Rectangle(
                x,
                leftRing.bounds.Y,
                leftRing.bounds.Width,
                leftRing.bounds.Height);
        }
        else
        {
            Rectangle organizeBounds = page.organizeButton?.bounds
                ?? new Rectangle(page.xPositionOnScreen + page.width, page.yPositionOnScreen + page.height / 3, 64, 64);
            buttonBounds = new Rectangle(organizeBounds.X, organizeBounds.Bottom + 8, organizeBounds.Width, organizeBounds.Height);
        }

        ClickableTextureComponent button = new(
            "ToolboxQuickStack",
            buttonBounds,
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

        QuickStackItemAnimation animation = new(player, location);
        bool movedAny = false;
        foreach (Chest chest in chests)
        {
            if (chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld())
                continue;

            movedAny |= StackIntoChest(player, chest, animation);
        }

        Game1.playSound(movedAny ? "Ship" : "cancel");
        animation.Complete();
        if (movedAny)
            monitor.Log($"已将背包物品堆叠到范围 {range} 格内的附近箱子。", LogLevel.Trace);
    }

    private static bool StackIntoChest(Farmer player, Chest chest, QuickStackItemAnimation animation)
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
                Item animationItem = playerItem.getOne();
                int beforeStack = playerItem.Stack;
                playerItem.Stack = chestItem.addToStack(playerItem);
                if (playerItem.Stack == beforeStack)
                    continue;

                animation.Add(animationItem, chest);
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
                Item animationItem = playerItem.getOne();
                int beforeStack = playerItem.Stack;
                Item? remaining = chest.addItem(playerItem);
                if (remaining is null)
                {
                    animation.Add(animationItem, chest);
                    player.Items.RemoveButKeepEmptySlot(playerItem);
                    movedAny = true;
                }
                else if (remaining.Stack != beforeStack)
                {
                    animation.Add(animationItem, chest);
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

    private sealed class QuickStackItemAnimation
    {
        private static readonly Random Random = new();
        private readonly Farmer farmer;
        private readonly GameLocation location;
        private readonly List<TemporaryAnimatedSprite> sprites = new();
        private int itemIndex;

        internal QuickStackItemAnimation(Farmer farmer, GameLocation location)
        {
            this.farmer = farmer;
            this.location = location;
        }

        internal void Add(Item item, Chest chest)
        {
            var itemData = ItemRegistry.GetDataOrErrorItem(item.QualifiedItemId);
            Vector2 source = GetSourcePosition();
            Vector2 target = (chest.TileLocation + new Vector2(0f, -1.5f)) * 64f;
            Vector2 displacement = (target - source) * 0.98f;
            float duration = 10f * MathF.Sqrt(Vector2.Distance(source, target))
                + 400f
                - 0.5f * MathF.Min(0f, displacement.Y);
            float verticalLift = 192f - MathF.Min(0f, displacement.Y);
            float horizontalDisplacement = displacement.X;
            Vector2 motion = new(
                displacement.X + horizontalDisplacement,
                displacement.Y - verticalLift);
            Vector2 acceleration = new(
                -2f * horizontalDisplacement / duration,
                2f * verticalLift / duration);
            motion /= duration;
            acceleration /= duration;

            float baseLayerDepth = 1f - (itemIndex * 2 + 1) * 0.000001f;
            AddSprite(
                itemData,
                source,
                sourceRectIndex: 0,
                Color.White,
                motion,
                acceleration,
                duration,
                baseLayerDepth);

            if (item is ColoredObject colored && !colored.ColorSameIndexAsParentSheetIndex)
            {
                AddSprite(
                    itemData,
                    source,
                    sourceRectIndex: 1,
                    colored.color.Value,
                    motion,
                    acceleration,
                    duration,
                    1f - itemIndex * 2 * 0.000001f);
            }

            itemIndex++;
        }

        internal void Complete()
        {
            if (sprites.Count > 0)
                Game1.Multiplayer.broadcastSprites(location, sprites.ToArray());
        }

        private void AddSprite(
            ParsedItemData itemData,
            Vector2 position,
            int sourceRectIndex,
            Color color,
            Vector2 motion,
            Vector2 acceleration,
            float duration,
            float layerDepth)
        {
            TemporaryAnimatedSprite sprite = TemporaryAnimatedSprite.GetTemporaryAnimatedSprite(
                itemData.TextureName,
                itemData.GetSourceRect(sourceRectIndex, null),
                position,
                flipped: false,
                alphaFade: 0f,
                color);
            sprite.scale = 4f;
            sprite.totalNumberOfLoops = 0;
            sprite.interval = duration;
            sprite.motion = motion;
            sprite.acceleration = acceleration;
            sprite.timeBasedMotion = true;
            sprite.layerDepth = layerDepth;
            sprites.Add(sprite);
        }

        private Vector2 GetSourcePosition()
        {
            Vector2 facingOffset = farmer.FacingDirection switch
            {
                0 => new Vector2(0f, -1.5f) * 64f,
                1 => new Vector2(0.5f, -1f) * 64f,
                3 => new Vector2(-0.5f, -1f) * 64f,
                _ => new Vector2(0f, -1f) * 64f
            };
            Vector2 randomOffset = new(
                Random.NextSingle() * 16f - 8f,
                Random.NextSingle() * 16f - 8f);
            return farmer.Position + facingOffset + randomOffset;
        }
    }
}
