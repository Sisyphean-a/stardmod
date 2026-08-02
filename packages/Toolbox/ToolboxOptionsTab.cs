using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Toolbox;

internal sealed class ToolboxOptionsTab
{
    private readonly IModHelper helper;
    private readonly Func<ModConfig> getConfig;
    private readonly Action<bool, bool> persistConfig;
    private GameMenu? gameMenu;
    private ToolboxOptionsPage? optionsPage;
    private int optionsPageIndex = -1;

    internal ToolboxOptionsTab(IModHelper helper, Func<ModConfig> getConfig, Action<bool, bool> persistConfig)
    {
        this.helper = helper;
        this.getConfig = getConfig;
        this.persistConfig = persistConfig;
    }

    internal void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.OldMenu is GameMenu oldMenu && ReferenceEquals(oldMenu, gameMenu))
        {
            if (optionsPage is not null)
                oldMenu.pages.Remove(optionsPage);

            gameMenu = null;
            optionsPage = null;
            optionsPageIndex = -1;
        }

        if (e.NewMenu is not GameMenu newMenu)
            return;

        gameMenu = newMenu;
        optionsPage = new ToolboxOptionsPage(newMenu, getConfig, persistConfig);
        optionsPageIndex = newMenu.pages.Count;
        newMenu.pages.Add(optionsPage);
    }

    internal void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.Button != SButton.MouseLeft
            || e.IsSuppressed()
            || Game1.activeClickableMenu is not GameMenu menu
            || !ReferenceEquals(menu, gameMenu)
            || menu.currentTab == optionsPageIndex
            || !menu.readyToClose())
        {
            return;
        }

        Rectangle bounds = GetTabBounds(menu);
        if (!bounds.Contains(Game1.getMouseX(true), Game1.getMouseY(true)))
            return;

        ChangeToOptionsTab(menu);
        helper.Input.Suppress(e.Button);
    }

    internal void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (Game1.activeClickableMenu is not GameMenu menu || !ReferenceEquals(menu, gameMenu))
            return;

        Rectangle bounds = GetTabBounds(menu);
        Color color = menu.currentTab == optionsPageIndex ? Color.White : Color.LightGray;
        IClickableMenu.drawTextureBox(
            Game1.spriteBatch,
            Game1.mouseCursors,
            new Rectangle(403, 383, 6, 6),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            color,
            4f,
            false,
            -1f);

        Vector2 size = Game1.smallFont.MeasureString("设");
        Game1.spriteBatch.DrawString(
            Game1.smallFont,
            "设",
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            Color.Black);
    }

    private void ChangeToOptionsTab(GameMenu menu)
    {
        menu.width = 800 + IClickableMenu.borderWidth * 2;
        menu.currentTab = optionsPageIndex;
        menu.lastOpenedNonMapTab = optionsPageIndex;
        menu.initializeUpperRightCloseButton();
        menu.invisible = false;
        Game1.playSound("smallSelect");
        menu.GetCurrentPage().populateClickableComponentList();
        menu.setTabNeighborsForCurrentPage();
    }

    private static Rectangle GetTabBounds(GameMenu menu)
    {
        ClickableComponent exitTab = menu.tabs[GameMenu.exitTab];
        return new Rectangle(exitTab.bounds.Right, exitTab.bounds.Y, 64, 64);
    }
}
