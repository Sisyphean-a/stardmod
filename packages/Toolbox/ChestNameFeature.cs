using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Toolbox;

internal static class ChestNameFeature
{
    private const string CustomNameKey = "xixifu.Toolbox/chest-name";
    private const string DefaultNameKey = "xixifu.Toolbox/chest-default-name";
    private const string DefaultChestName = "Chest";
    private const string RenameButtonText = "改名";
    private const string RenameButtonHoverText = "设置箱子名称";
    private const string RenameMenuTitle = "设置箱子名称";
    private const int MaxNameLength = 32;
    private const int RenameButtonId = 64001;
    private const int ButtonSize = 64;
    private const float NameLabelTopOffset = 4f;

    private static readonly ConditionalWeakTable<ItemGrabMenu, RenameButtonState> RenameButtons = new();

    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(
                typeof(ItemGrabMenu),
                nameof(ItemGrabMenu.receiveLeftClick),
                new[] { typeof(int), typeof(int), typeof(bool) })!,
            prefix: new HarmonyMethod(typeof(ChestNameFeature), nameof(ReceiveLeftClickPrefix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(ItemGrabMenu),
                nameof(ItemGrabMenu.performHoverAction),
                new[] { typeof(int), typeof(int) })!,
            postfix: new HarmonyMethod(typeof(ChestNameFeature), nameof(PerformHoverActionPostfix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(ItemGrabMenu),
                nameof(ItemGrabMenu.update),
                new[] { typeof(GameTime) })!,
            postfix: new HarmonyMethod(typeof(ChestNameFeature), nameof(UpdatePostfix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(ItemGrabMenu),
                nameof(ItemGrabMenu.draw),
                new[] { typeof(SpriteBatch) })!,
            postfix: new HarmonyMethod(typeof(ChestNameFeature), nameof(DrawPostfix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(Chest),
                nameof(Chest.draw),
                new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) })!,
            postfix: new HarmonyMethod(typeof(ChestNameFeature), nameof(ChestDrawPostfix)));
        harmony.Patch(
            AccessTools.Method(
                typeof(Chest),
                nameof(Chest.performObjectDropInAction),
                new[] { typeof(Item), typeof(bool), typeof(Farmer), typeof(bool) })!,
            postfix: new HarmonyMethod(typeof(ChestNameFeature), nameof(PerformObjectDropInActionPostfix)));
    }

    private static bool ReceiveLeftClickPrefix(ItemGrabMenu __instance, int x, int y, bool playSound)
    {
        if (!TryGetRenameButton(__instance, out Chest? chest, out RenameButtonState? state)
            || chest is null
            || state is null
            || !state.Button.containsPoint(x, y))
        {
            return true;
        }

        OpenNamingMenu(chest, __instance);
        return false;
    }

    private static void PerformHoverActionPostfix(ItemGrabMenu __instance, int x, int y)
    {
        if (!TryGetRenameButton(__instance, out _, out RenameButtonState? state)
            || state is null)
        {
            return;
        }

        state.IsHovered = state.Button.containsPoint(x, y);
        if (state.IsHovered)
        {
            __instance.hoverText = RenameButtonHoverText;
            __instance.hoveredItem = null;
        }
    }

    private static void UpdatePostfix(ItemGrabMenu __instance)
    {
        TryGetRenameButton(__instance, out _, out _);
    }

    private static void DrawPostfix(ItemGrabMenu __instance, SpriteBatch b)
    {
        if (!TryGetRenameButton(__instance, out Chest? chest, out RenameButtonState? state)
            || chest is null
            || state is null)
        {
            return;
        }

        DrawRenameButton(b, state);
    }

    private static void PerformObjectDropInActionPostfix(Chest __instance, bool __result)
    {
        if (!__result || __instance.Location is null)
            return;

        if (__instance.Location.Objects.TryGetValue(__instance.TileLocation, out StardewValley.Object? replacement)
            && replacement is Chest replacementChest
            && !ReferenceEquals(replacementChest, __instance))
        {
            ApplyStoredName(replacementChest);
        }
    }

    private static bool TryGetRenameButton(
        ItemGrabMenu menu,
        out Chest? chest,
        out RenameButtonState? state)
    {
        if (menu.source != ItemGrabMenu.source_chest
            || menu.sourceItem is not Chest sourceChest
            || !sourceChest.playerChest.Value)
        {
            chest = null;
            state = null;
            return false;
        }

        chest = sourceChest;
        ApplyStoredName(sourceChest);
        RenameButtonState buttonState = RenameButtons.GetValue(menu, CreateRenameButtonState);
        state = buttonState;
        if (!menu.allClickableComponents.Contains(buttonState.Button))
            menu.allClickableComponents.Add(buttonState.Button);

        PositionRenameButton(menu, buttonState);
        return true;
    }

    private static RenameButtonState CreateRenameButtonState(ItemGrabMenu menu)
    {
        ClickableComponent button = new(Rectangle.Empty, "ToolboxRenameChest")
        {
            myID = RenameButtonId,
            region = ItemGrabMenu.region_organizationButtons,
            rightNeighborID = -99998
        };
        return new RenameButtonState(button);
    }

    private static void PositionRenameButton(ItemGrabMenu menu, RenameButtonState state)
    {
        int x = menu.ItemsToGrabMenu.xPositionOnScreen
            + menu.ItemsToGrabMenu.width
            + IClickableMenu.borderWidth * 2;
        ClickableComponent? bottomSideButton = GetBottomSideButton(menu);
        int y = bottomSideButton is null
            ? menu.ItemsToGrabMenu.yPositionOnScreen + menu.ItemsToGrabMenu.height / 2 - ButtonSize / 2
            : bottomSideButton.bounds.Bottom + 16;
        state.Button.bounds = new Rectangle(x, y, ButtonSize, ButtonSize);

        int storageColumnCount = menu.ItemsToGrabMenu.capacity / menu.ItemsToGrabMenu.rows;
        state.Button.leftNeighborID = ItemGrabMenu.region_itemsToGrabMenuModifier + storageColumnCount - 1;
        state.Button.upNeighborID = bottomSideButton?.myID ?? -1;
        state.Button.downNeighborID = menu.trashCan?.myID ?? -1;
        if (bottomSideButton is not null)
            bottomSideButton.downNeighborID = state.Button.myID;
        if (menu.trashCan is not null)
            menu.trashCan.upNeighborID = state.Button.myID;
    }

    private static ClickableComponent? GetBottomSideButton(ItemGrabMenu menu)
    {
        ClickableComponent? result = null;
        Consider(menu.organizeButton);
        Consider(menu.fillStacksButton);
        Consider(menu.colorPickerToggleButton);
        Consider(menu.specialButton);
        Consider(menu.junimoNoteIcon);
        return result;

        void Consider(ClickableComponent? candidate)
        {
            if (candidate is not null && (result is null || candidate.bounds.Bottom > result.bounds.Bottom))
                result = candidate;
        }
    }

    private static void DrawRenameButton(SpriteBatch batch, RenameButtonState state)
    {
        Color background = state.IsHovered ? new Color(255, 246, 205) : Color.White;
        IClickableMenu.drawTextureBox(
            batch,
            Game1.mouseCursors,
            new Rectangle(403, 383, 6, 6),
            state.Button.bounds.X,
            state.Button.bounds.Y,
            state.Button.bounds.Width,
            state.Button.bounds.Height,
            background,
            4f,
            false);

        Vector2 textSize = Game1.smallFont.MeasureString(RenameButtonText);
        Vector2 textPosition = new(
            state.Button.bounds.Center.X - textSize.X / 2f,
            state.Button.bounds.Center.Y - textSize.Y / 2f);
        batch.DrawString(Game1.smallFont, RenameButtonText, textPosition, Game1.textColor);
    }

    private static void ChestDrawPostfix(Chest __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (alpha <= 0f
            || !__instance.playerChest.Value
            || !HasCustomName(__instance)
            || Game1.activeClickableMenu is not null
            || __instance.GetMutex().IsLocked())
        {
            return;
        }

        string displayName = TruncateText(
            GetChestName(__instance),
            Game1.smallFont,
            Math.Min(260f, Math.Max(96f, Game1.uiViewport.Width - 32f)));
        Vector2 textSize = Game1.smallFont.MeasureString(displayName);
        Vector2 chestTop = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(x * 64f + 32f, (y - 1) * 64f));
        int labelWidth = (int)Math.Ceiling(textSize.X) + 12;
        int labelHeight = (int)Math.Ceiling(textSize.Y) + 8;
        Rectangle labelBounds = new(
            (int)Math.Round(chestTop.X - labelWidth / 2f),
            (int)Math.Round(chestTop.Y + NameLabelTopOffset),
            labelWidth,
            labelHeight);
        float layerDepth = Math.Max(0f, ((y + 1) * 64f - 24f) / 10000f)
            + x * 0.00001f
            + 0.00003f;

        spriteBatch.Draw(
            Game1.staminaRect,
            labelBounds,
            null,
            Color.Black * (0.65f * alpha),
            0f,
            Vector2.Zero,
            SpriteEffects.None,
            layerDepth);
        Vector2 textPosition = new(
            labelBounds.X + (labelWidth - textSize.X) / 2f,
            labelBounds.Y + (labelHeight - textSize.Y) / 2f);
        spriteBatch.DrawString(
            Game1.smallFont,
            displayName,
            textPosition + new Vector2(2f, 2f),
            Color.Black * (0.8f * alpha),
            0f,
            Vector2.Zero,
            1f,
            SpriteEffects.None,
            layerDepth + 0.00001f);
        spriteBatch.DrawString(
            Game1.smallFont,
            displayName,
            textPosition,
            new Color(255, 245, 220) * alpha,
            0f,
            Vector2.Zero,
            1f,
            SpriteEffects.None,
            layerDepth + 0.00002f);
    }

    private static string TruncateText(string text, SpriteFont font, float maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
            return text;

        string value = text;
        while (value.Length > 1 && font.MeasureString(value + "…").X > maxWidth)
            value = value[..^1];
        return value + "…";
    }

    private static void OpenNamingMenu(Chest chest, ItemGrabMenu parentMenu)
    {
        if (chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld())
        {
            Game1.playSound("cancel");
            return;
        }

        ChestNamingMenu namingMenu = new(chest, parentMenu);
        parentMenu.SetChildMenu(namingMenu);
        Game1.activeClickableMenu = namingMenu;
    }

    private static void ApplyName(Chest chest, string rawName)
    {
        string name = rawName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            chest.modData[CustomNameKey] = string.Empty;
            chest.Name = GetDefaultChestName(chest);
            return;
        }

        if (!chest.modData.ContainsKey(DefaultNameKey))
        {
            string originalName = string.IsNullOrWhiteSpace(chest.Name) ? DefaultChestName : chest.Name;
            chest.modData[DefaultNameKey] = originalName;
        }

        chest.modData[CustomNameKey] = name;
        chest.Name = name;
    }

    private static string GetChestName(Chest chest)
    {
        if (HasCustomName(chest))
            return chest.modData[CustomNameKey];

        return string.IsNullOrWhiteSpace(chest.Name) ? DefaultChestName : chest.Name;
    }

    private static bool HasCustomName(Chest chest)
    {
        return chest.modData.ContainsKey(CustomNameKey)
            && !string.IsNullOrWhiteSpace(chest.modData[CustomNameKey]);
    }

    private static string GetDefaultChestName(Chest chest)
    {
        if (chest.modData.ContainsKey(DefaultNameKey)
            && !string.IsNullOrWhiteSpace(chest.modData[DefaultNameKey]))
        {
            return chest.modData[DefaultNameKey];
        }

        return DefaultChestName;
    }

    private static void ApplyStoredName(Chest chest)
    {
        if (HasCustomName(chest))
            chest.Name = chest.modData[CustomNameKey];
    }

    private sealed class RenameButtonState
    {
        internal RenameButtonState(ClickableComponent button)
        {
            Button = button;
        }

        internal ClickableComponent Button { get; }
        internal bool IsHovered { get; set; }
    }

    private sealed class ChestNamingMenu : NamingMenu
    {
        private readonly Chest chest;
        private readonly ItemGrabMenu parentMenu;

        internal ChestNamingMenu(Chest chest, ItemGrabMenu parentMenu)
            : base(null, RenameMenuTitle, GetChestName(chest))
        {
            this.chest = chest;
            this.parentMenu = parentMenu;
            minLength = 0;
            textBox.textLimit = MaxNameLength;
            doneNaming = OnDoneNaming;
            exitFunction = () => Game1.activeClickableMenu = this.parentMenu;
        }

        private void OnDoneNaming(string name)
        {
            ApplyName(chest, name);
            textBox.Selected = false;
            Game1.playSound("smallSelect");
            exitThisMenu(playSound: false);
        }

        protected override void cleanupBeforeExit()
        {
            textBox.Selected = false;
            base.cleanupBeforeExit();
        }
    }
}
