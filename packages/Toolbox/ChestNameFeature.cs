using System.Reflection;
using System.Reflection.Emit;
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
    private const int SideButtonGap = 4;
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
            transpiler: new HarmonyMethod(typeof(ChestNameFeature), nameof(DrawTranspiler)));
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

    private static IEnumerable<CodeInstruction> DrawTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = instructions.ToList();
        FieldInfo hoverTextField = AccessTools.Field(typeof(MenuWithInventory), nameof(MenuWithInventory.hoverText))
            ?? throw new MissingFieldException(typeof(MenuWithInventory).FullName, nameof(MenuWithInventory.hoverText));
        MethodInfo drawButtonMethod = AccessTools.Method(
            typeof(ChestNameFeature),
            nameof(DrawRenameButtonInMenu))
            ?? throw new MissingMethodException(typeof(ChestNameFeature).FullName, nameof(DrawRenameButtonInMenu));

        int hoverTextLoadIndex = result.FindIndex(instruction => instruction.LoadsField(hoverTextField));
        if (hoverTextLoadIndex <= 0 || result[hoverTextLoadIndex - 1].opcode != OpCodes.Ldarg_0)
        {
            throw new InvalidOperationException(
                "无法在 ItemGrabMenu.draw 的原版按钮与悬浮提示之间定位箱子改名按钮绘制点。");
        }

        int insertIndex = hoverTextLoadIndex - 1;
        CodeInstruction loadMenu = new(OpCodes.Ldarg_0);
        loadMenu.labels.AddRange(result[insertIndex].labels);
        result[insertIndex].labels.Clear();
        loadMenu.blocks.AddRange(result[insertIndex].blocks);
        result[insertIndex].blocks.Clear();
        result.InsertRange(
            insertIndex,
            new[]
            {
                loadMenu,
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, drawButtonMethod)
            });
        return result;
    }

    private static void DrawRenameButtonInMenu(ItemGrabMenu menu, SpriteBatch batch)
    {
        if (TryGetRenameButton(menu, out _, out RenameButtonState? state) && state is not null)
            DrawRenameButton(batch, state);
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

        if (buttonState.PositionedTick != Game1.ticks)
        {
            PositionRenameButton(menu, buttonState);
            buttonState.PositionedTick = Game1.ticks;
        }
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
        RestoreNavigationLink(state);

        int preferredY = menu.ItemsToGrabMenu.yPositionOnScreen
            + menu.ItemsToGrabMenu.height / 2
            - ButtonSize / 2;
        Rectangle bounds = FindAvailableButtonBounds(menu, state.Button, preferredY);
        state.Button.bounds = bounds;

        bool isLeftOfStorage = bounds.Center.X < menu.ItemsToGrabMenu.xPositionOnScreen;
        InventoryMenu.BorderSide borderSide = isLeftOfStorage
            ? InventoryMenu.BorderSide.Left
            : InventoryMenu.BorderSide.Right;
        ClickableComponent? nearestStorageSlot = menu.ItemsToGrabMenu
            .GetBorder(borderSide)
            .Where(component => component.bounds.Width > 0 && component.bounds.Height > 0)
            .OrderBy(component => Math.Abs(component.bounds.Center.Y - bounds.Center.Y))
            .FirstOrDefault();

        state.Button.leftNeighborID = isLeftOfStorage
            ? -99998
            : nearestStorageSlot?.myID ?? -99998;
        state.Button.rightNeighborID = isLeftOfStorage
            ? nearestStorageSlot?.myID ?? -99998
            : -99998;
        state.Button.upNeighborID = -99998;
        state.Button.downNeighborID = -99998;
        LinkStorageSlotToButton(state, menu.ItemsToGrabMenu, nearestStorageSlot, isLeftOfStorage);
    }

    private static void RestoreNavigationLink(RenameButtonState state)
    {
        if (state.LinkedStorageSlot is null)
            return;

        if (state.LinkedFromLeft && state.LinkedStorageSlot.leftNeighborID == RenameButtonId)
        {
            state.LinkedStorageSlot.leftNeighborID = state.PreviousNeighborId;
            state.LinkedStorageSlot.leftNeighborImmutable = state.PreviousNeighborImmutable;
        }
        else if (!state.LinkedFromLeft && state.LinkedStorageSlot.rightNeighborID == RenameButtonId)
        {
            state.LinkedStorageSlot.rightNeighborID = state.PreviousNeighborId;
            state.LinkedStorageSlot.rightNeighborImmutable = state.PreviousNeighborImmutable;
        }

        state.LinkedStorageSlot = null;
    }

    private static void LinkStorageSlotToButton(
        RenameButtonState state,
        InventoryMenu storageMenu,
        ClickableComponent? storageSlot,
        bool buttonIsLeft)
    {
        if (storageSlot is null)
            return;

        if (buttonIsLeft)
        {
            if (!IsVanillaLeftBoundaryTarget(storageMenu, storageSlot.leftNeighborID))
                return;

            state.LinkedStorageSlot = storageSlot;
            state.LinkedFromLeft = true;
            state.PreviousNeighborId = storageSlot.leftNeighborID;
            state.PreviousNeighborImmutable = storageSlot.leftNeighborImmutable;
            storageSlot.leftNeighborID = RenameButtonId;
            storageSlot.leftNeighborImmutable = true;
            return;
        }

        if (storageSlot.rightNeighborImmutable
            || IsExternalNavigationTarget(storageMenu, storageSlot.rightNeighborID))
        {
            return;
        }

        state.LinkedStorageSlot = storageSlot;
        state.LinkedFromLeft = false;
        state.PreviousNeighborId = storageSlot.rightNeighborID;
        state.PreviousNeighborImmutable = storageSlot.rightNeighborImmutable;
        storageSlot.rightNeighborID = RenameButtonId;
        storageSlot.rightNeighborImmutable = true;
    }

    private static bool IsVanillaLeftBoundaryTarget(InventoryMenu storageMenu, int neighborId)
    {
        int offsetDropButtonId = ItemGrabMenu.region_itemsToGrabMenuModifier
            + InventoryMenu.region_dropButton;
        return neighborId < 0
            || neighborId == offsetDropButtonId
            || storageMenu.inventory.Any(component => component.myID == neighborId);
    }

    private static bool IsExternalNavigationTarget(InventoryMenu storageMenu, int neighborId)
    {
        return neighborId >= 0
            && storageMenu.inventory.All(component => component.myID != neighborId);
    }

    private static Rectangle FindAvailableButtonBounds(
        ItemGrabMenu menu,
        ClickableComponent button,
        int preferredY)
    {
        int viewportWidth = Game1.uiViewport.Width;
        int viewportHeight = Game1.uiViewport.Height;
        int minY = SideButtonGap;
        int maxY = Math.Max(minY, viewportHeight - ButtonSize - SideButtonGap);
        int centeredY = Math.Clamp(preferredY, minY, maxY);
        int columnStep = ButtonSize + SideButtonGap;

        int leftX = menu.ItemsToGrabMenu.xPositionOnScreen
            - IClickableMenu.borderWidth * 2
            - ButtonSize;
        for (int x = leftX; x >= SideButtonGap; x -= columnStep)
        {
            if (TryFindAvailableY(menu, button, x, centeredY, minY, maxY, out Rectangle bounds))
                return bounds;
        }

        int rightX = menu.ItemsToGrabMenu.xPositionOnScreen
            + menu.ItemsToGrabMenu.width
            + IClickableMenu.borderWidth * 2;
        for (int x = rightX; x + ButtonSize <= viewportWidth - SideButtonGap; x += columnStep)
        {
            if (TryFindAvailableY(menu, button, x, centeredY, minY, maxY, out Rectangle bounds))
                return bounds;
        }

        // Guarantee: 极窄窗口没有完整侧栏时，按钮仍保持在屏幕内并优先贴近菜单左侧。
        int fallbackX = Math.Clamp(leftX, SideButtonGap, Math.Max(SideButtonGap, viewportWidth - ButtonSize - SideButtonGap));
        return new Rectangle(fallbackX, centeredY, ButtonSize, ButtonSize);
    }

    private static bool TryFindAvailableY(
        ItemGrabMenu menu,
        ClickableComponent button,
        int x,
        int preferredY,
        int minY,
        int maxY,
        out Rectangle bounds)
    {
        Rectangle preferred = new(x, preferredY, ButtonSize, ButtonSize);
        if (IsButtonPositionAvailable(menu, button, preferred))
        {
            bounds = preferred;
            return true;
        }

        HashSet<int> candidateYs = new() { minY, maxY };
        foreach (ClickableComponent component in menu.allClickableComponents)
        {
            if (ReferenceEquals(component, button)
                || component.bounds.Width <= 0
                || component.bounds.Height <= 0)
            {
                continue;
            }

            Rectangle occupied = component.bounds;
            occupied.Inflate(SideButtonGap, SideButtonGap);
            if (x + ButtonSize <= occupied.Left || x >= occupied.Right)
                continue;

            int above = occupied.Top - ButtonSize;
            int below = occupied.Bottom;
            if (above >= minY && above <= maxY)
                candidateYs.Add(above);
            if (below >= minY && below <= maxY)
                candidateYs.Add(below);
        }

        foreach (int y in candidateYs.OrderBy(value => Math.Abs(value - preferredY)))
        {
            Rectangle candidate = new(x, y, ButtonSize, ButtonSize);
            if (IsButtonPositionAvailable(menu, button, candidate))
            {
                bounds = candidate;
                return true;
            }
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private static bool IsButtonPositionAvailable(
        ItemGrabMenu menu,
        ClickableComponent button,
        Rectangle candidate)
    {
        foreach (ClickableComponent component in menu.allClickableComponents)
        {
            if (ReferenceEquals(component, button)
                || component.bounds.Width <= 0
                || component.bounds.Height <= 0)
            {
                continue;
            }

            Rectangle occupied = component.bounds;
            occupied.Inflate(SideButtonGap, SideButtonGap);
            if (candidate.Intersects(occupied))
                return false;
        }

        return true;
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
            true);

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
        internal int PositionedTick { get; set; } = -1;
        internal ClickableComponent? LinkedStorageSlot { get; set; }
        internal bool LinkedFromLeft { get; set; }
        internal int PreviousNeighborId { get; set; }
        internal bool PreviousNeighborImmutable { get; set; }
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
