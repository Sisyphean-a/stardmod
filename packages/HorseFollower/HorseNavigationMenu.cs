using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace HorseFollower;

internal sealed class HorseNavigationMenu : IClickableMenu
{
    private const int MenuWidth = 760;
    private const int MenuHeight = 590;
    private const int ColumnCount = 2;
    private const int RowHeight = 70;

    private readonly IReadOnlyList<HorseNavigationDestination> destinations;
    private readonly Action<HorseNavigationDestination> select;
    private int selectedIndex;
    private string hoverText = "";

    internal HorseNavigationMenu(
        IReadOnlyList<HorseNavigationDestination> destinations,
        Action<HorseNavigationDestination> select)
        : base(GetMenuX(), GetMenuY(), MenuWidth, MenuHeight, true)
    {
        this.destinations = destinations;
        this.select = select;
        initializeUpperRightCloseButton();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton.containsPoint(x, y))
        {
            exitThisMenu();
            return;
        }

        for (int index = 0; index < destinations.Count; index++)
        {
            if (!GetRowBounds(index).Contains(x, y))
                continue;

            selectedIndex = index;
            HorseNavigationDestination destination = destinations[index];
            if (!destination.IsAvailable)
            {
                Game1.playSound("cancel");
                return;
            }

            exitThisMenu();
            Game1.playSound("smallSelect");
            select(destination);
            return;
        }
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        exitThisMenu();
    }

    public override void receiveKeyPress(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                exitThisMenu();
                return;
            case Keys.Enter:
                SelectCurrent();
                return;
            case Keys.Left:
                MoveSelection(-1, 0);
                return;
            case Keys.Right:
                MoveSelection(1, 0);
                return;
            case Keys.Up:
                MoveSelection(0, -1);
                return;
            case Keys.Down:
                MoveSelection(0, 1);
                return;
        }
    }

    public override void receiveGamePadButton(Buttons button)
    {
        switch (button)
        {
            case Buttons.B:
                exitThisMenu();
                break;
            case Buttons.A:
                SelectCurrent();
                break;
            case Buttons.DPadLeft:
            case Buttons.LeftThumbstickLeft:
                MoveSelection(-1, 0);
                break;
            case Buttons.DPadRight:
            case Buttons.LeftThumbstickRight:
                MoveSelection(1, 0);
                break;
            case Buttons.DPadUp:
            case Buttons.LeftThumbstickUp:
                MoveSelection(0, -1);
                break;
            case Buttons.DPadDown:
            case Buttons.LeftThumbstickDown:
                MoveSelection(0, 1);
                break;
        }
    }

    public override void performHoverAction(int x, int y)
    {
        hoverText = "";
        for (int index = 0; index < destinations.Count; index++)
        {
            if (!GetRowBounds(index).Contains(x, y))
                continue;

            selectedIndex = index;
            if (!destinations[index].IsAvailable)
                hoverText = "社区中心尚未开放";
            return;
        }
    }

    public override void draw(SpriteBatch batch)
    {
        Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);
        batch.DrawString(
            Game1.dialogueFont,
            "骑马自动寻路",
            new Vector2(xPositionOnScreen + 42, yPositionOnScreen + 28),
            Game1.textColor);
        batch.DrawString(
            Game1.smallFont,
            "选择一个主要地点。马匹会停在室外入口附近，不进入店内。",
            new Vector2(xPositionOnScreen + 46, yPositionOnScreen + 78),
            new Color(96, 64, 32));

        for (int index = 0; index < destinations.Count; index++)
            DrawDestination(batch, index, GetRowBounds(index), destinations[index]);

        upperRightCloseButton.draw(batch);
        if (!string.IsNullOrWhiteSpace(hoverText))
            IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
        drawMouse(batch);
    }

    private void DrawDestination(
        SpriteBatch batch,
        int index,
        Rectangle bounds,
        HorseNavigationDestination destination)
    {
        bool available = destination.IsAvailable;
        bool selected = index == selectedIndex;
        Color background = !available
            ? new Color(160, 160, 160) * 0.55f
            : selected
                ? new Color(88, 132, 172) * 0.9f
                : new Color(246, 235, 202) * 0.92f;
        Color textColor = !available || selected ? Color.White : Game1.textColor;

        IClickableMenu.drawTextureBox(
            batch,
            Game1.mouseCursors,
            new Rectangle(403, 383, 6, 6),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            background,
            4f,
            false);
        batch.DrawString(Game1.smallFont, destination.DisplayName, new Vector2(bounds.X + 18, bounds.Y + 12), textColor);
        string detail = available ? destination.MapName : destination.AvailabilityText;
        batch.DrawString(Game1.smallFont, detail, new Vector2(bounds.X + 18, bounds.Y + 39), available ? textColor * 0.75f : Color.White);
    }

    private void SelectCurrent()
    {
        if (selectedIndex < 0 || selectedIndex >= destinations.Count)
            return;
        if (!destinations[selectedIndex].IsAvailable)
        {
            Game1.playSound("cancel");
            return;
        }

        exitThisMenu();
        Game1.playSound("smallSelect");
        select(destinations[selectedIndex]);
    }

    private void MoveSelection(int columnDelta, int rowDelta)
    {
        if (destinations.Count == 0)
            return;

        int row = selectedIndex / ColumnCount;
        int column = selectedIndex % ColumnCount;
        row += rowDelta;
        column += columnDelta;
        int maxRow = (destinations.Count - 1) / ColumnCount;
        row = Math.Clamp(row, 0, maxRow);
        column = Math.Clamp(column, 0, ColumnCount - 1);
        int next = row * ColumnCount + column;
        if (next >= destinations.Count)
            next = destinations.Count - 1;
        selectedIndex = next;
        Game1.playSound("shiny4");
    }

    private Rectangle GetRowBounds(int index)
    {
        int columnWidth = (width - 96) / ColumnCount;
        int row = index / ColumnCount;
        int column = index % ColumnCount;
        return new Rectangle(
            xPositionOnScreen + 36 + column * (columnWidth + 24),
            yPositionOnScreen + 124 + row * RowHeight,
            columnWidth,
            RowHeight - 8);
    }

    private static int GetMenuX()
    {
        return Math.Max(0, (Game1.uiViewport.Width - MenuWidth) / 2);
    }

    private static int GetMenuY()
    {
        return Math.Max(0, (Game1.uiViewport.Height - MenuHeight) / 2);
    }
}
