using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace HotkeyViewer;

internal sealed class HotkeyViewerMenu : IClickableMenu
{
    private const int RowHeight = 54;
    private const int ScrollStep = 4;
    private readonly HotkeyCatalog catalog;
    private readonly TextBox searchBox;
    private HotkeyCatalogResult catalogResult = new(Array.Empty<HotkeyEntry>(), new Dictionary<string, int>(), Array.Empty<string>());
    private ViewerFilter filter;
    private int topIndex;
    private string hoverText = "";
    private string lastSearchText = "";

    internal HotkeyViewerMenu(HotkeyCatalog catalog)
        : base(GetMenuX(), GetMenuY(), GetMenuWidth(), GetMenuHeight(), true)
    {
        this.catalog = catalog;
        initializeUpperRightCloseButton();

        searchBox = new TextBox(
            Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
            Game1.staminaRect,
            Game1.smallFont,
            Game1.textColor)
        {
            textLimit = 32
        };

        PositionSearchBox();
        RefreshCatalog();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton.containsPoint(x, y))
        {
            exitThisMenu();
            return;
        }

        if (GetRefreshButtonBounds().Contains(x, y))
        {
            RefreshCatalog();
            Game1.playSound("smallSelect");
            return;
        }

        for (int index = 0; index < 4; index++)
        {
            Rectangle tab = GetFilterButtonBounds(index);
            if (!tab.Contains(x, y))
                continue;

            filter = (ViewerFilter)index;
            topIndex = 0;
            Game1.playSound("smallSelect");
            return;
        }

        Rectangle searchBounds = GetSearchBounds();
        searchBox.Selected = searchBounds.Contains(x, y);
        if (searchBox.Selected)
            return;

        if (GetUpArrowBounds().Contains(x, y))
        {
            Scroll(-ScrollStep);
            return;
        }

        if (GetDownArrowBounds().Contains(x, y))
        {
            Scroll(ScrollStep);
            return;
        }

        Rectangle runner = GetScrollBarRunnerBounds();
        if (runner.Contains(x, y))
        {
            List<HotkeyEntry> entries = GetFilteredEntries();
            int visibleRows = GetVisibleRowCount();
            int maxTopIndex = Math.Max(0, entries.Count - visibleRows);
            if (maxTopIndex > 0)
            {
                float ratio = Math.Clamp(y - runner.Y, 0, runner.Height) / (float)runner.Height;
                topIndex = Math.Clamp((int)MathF.Round(maxTopIndex * ratio), 0, maxTopIndex);
            }
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        Scroll(direction > 0 ? -ScrollStep : ScrollStep);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            if (searchBox.Selected)
            {
                searchBox.Selected = false;
                if (!string.IsNullOrWhiteSpace(searchBox.Text))
                    searchBox.Text = "";
            }
            else
            {
                exitThisMenu();
            }

            return;
        }

        if (key == Keys.Up)
            Scroll(-1);
        else if (key == Keys.Down)
            Scroll(1);
        else if (key == Keys.PageUp)
            Scroll(-GetVisibleRowCount());
        else if (key == Keys.PageDown)
            Scroll(GetVisibleRowCount());
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.B)
            exitThisMenu();
        else if (button == Buttons.DPadUp || button == Buttons.LeftThumbstickUp)
            Scroll(-1);
        else if (button == Buttons.DPadDown || button == Buttons.LeftThumbstickDown)
            Scroll(1);
    }

    public override void update(GameTime time)
    {
        if (!lastSearchText.Equals(searchBox.Text, StringComparison.Ordinal))
        {
            lastSearchText = searchBox.Text;
            topIndex = 0;
        }
    }

    public override void performHoverAction(int x, int y)
    {
        hoverText = "";
        searchBox.Hover(x, y);

        if (GetRefreshButtonBounds().Contains(x, y))
        {
            hoverText = "重新扫描游戏本体、GMCM 注册项和已加载模组 config.json。";
            return;
        }

        List<HotkeyEntry> entries = GetFilteredEntries();
        Rectangle rowsArea = GetRowsAreaBounds();
        int visibleRows = GetVisibleRowCount();
        for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
        {
            int entryIndex = topIndex + rowIndex;
            if (entryIndex >= entries.Count)
                break;

            Rectangle row = GetRowBounds(rowsArea, rowIndex);
            if (!row.Contains(x, y))
                continue;

            HotkeyEntry entry = entries[entryIndex];
            hoverText = $"{entry.Action}\n按键：{entry.BindingText}\n来源：{entry.SourceLabel}\n关联：{GetOwnerDisplay(entry)}\n字段：{entry.Detail}";
            if (catalogResult.IsConflict(entry))
                hoverText += "\n提示：这个按键也被其他功能使用，可能冲突。";
            return;
        }
    }

    public override void draw(SpriteBatch batch)
    {
        drawBackground(batch);
        IClickableMenu.drawTextureBox(batch, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        DrawHeader(batch);
        DrawSummary(batch);
        DrawFilters(batch);
        DrawSearch(batch);
        DrawRows(batch);
        DrawFooter(batch);

        upperRightCloseButton.draw(batch);
        if (!string.IsNullOrWhiteSpace(hoverText))
            IClickableMenu.drawHoverText(batch, hoverText, Game1.smallFont);
        drawMouse(batch);
    }

    private void RefreshCatalog()
    {
        catalogResult = catalog.Build();
        topIndex = 0;
    }

    private void DrawHeader(SpriteBatch batch)
    {
        Vector2 titlePosition = new(xPositionOnScreen + 44, yPositionOnScreen + 30);
        DrawText(batch, "快捷键查看器", Game1.dialogueFont, titlePosition, Game1.textColor, true);
        DrawText(
            batch,
            "看本体和模组按键；红色表示同一按键被多个功能占用。",
            Game1.smallFont,
            new Vector2(xPositionOnScreen + 48, yPositionOnScreen + 76),
            new Color(96, 64, 32),
            false);
    }

    private void DrawSummary(SpriteBatch batch)
    {
        int gameCount = catalogResult.Entries.Count(entry => entry.Source == HotkeySource.Game);
        int modCount = catalogResult.Entries.Count - gameCount;
        int conflictCount = catalogResult.Entries.Count(catalogResult.IsConflict);

        string summary = $"总计 {catalogResult.Entries.Count}  ·  冲突 {conflictCount}  ·  本体 {gameCount}  ·  模组 {modCount}";
        DrawText(batch, summary, Game1.smallFont, new Vector2(xPositionOnScreen + 48, yPositionOnScreen + 112), new Color(96, 64, 32), false);
    }

    private void DrawSummaryCard(SpriteBatch batch, Rectangle bounds, string label, string value, Color color)
    {
        IClickableMenu.drawTextureBox(batch, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 4f, false);
        batch.Draw(Game1.staminaRect, new Rectangle(bounds.X + 8, bounds.Y + 8, 8, bounds.Height - 16), color);
        DrawText(batch, label, Game1.smallFont, new Vector2(bounds.X + 20, bounds.Y + 8), new Color(96, 64, 32), false);
        DrawText(batch, value, Game1.smallFont, new Vector2(bounds.Right - 30, bounds.Y + 8), color, true);
    }

    private void DrawFilters(SpriteBatch batch)
    {
        for (int index = 0; index < 4; index++)
        {
            ViewerFilter target = (ViewerFilter)index;
            DrawButton(batch, GetFilterButtonBounds(index), GetFilterLabel(target), filter == target);
        }

        DrawButton(batch, GetRefreshButtonBounds(), "刷新", false);
    }

    private void DrawSearch(SpriteBatch batch)
    {
        searchBox.X = GetSearchBounds().X;
        searchBox.Y = GetSearchBounds().Y;
        searchBox.Width = GetSearchBounds().Width;
        searchBox.Height = GetSearchBounds().Height;
        searchBox.Draw(batch);

        if (string.IsNullOrWhiteSpace(searchBox.Text) && !searchBox.Selected)
        {
            DrawText(
                batch,
                "搜索按键 / 功能 / 模组",
                Game1.smallFont,
                new Vector2(searchBox.X + 18, searchBox.Y + 12),
                Color.Gray,
                false);
        }
    }

    private void DrawRows(SpriteBatch batch)
    {
        List<HotkeyEntry> entries = GetFilteredEntries();
        ClampTopIndex(entries.Count);

        Rectangle rowsArea = GetRowsAreaBounds();
        Rectangle header = new(rowsArea.X, rowsArea.Y - 36, rowsArea.Width - 44, 30);
        batch.Draw(Game1.staminaRect, header, new Color(112, 84, 55) * 0.9f);
        DrawText(batch, "按键", Game1.smallFont, new Vector2(header.X + 20, header.Y + 4), Color.White, true);
        DrawText(batch, "功能", Game1.smallFont, new Vector2(header.X + 260, header.Y + 4), Color.White, true);
        DrawText(batch, "来源", Game1.smallFont, new Vector2(header.Right - 380, header.Y + 4), Color.White, true);
        DrawText(batch, "关联", Game1.smallFont, new Vector2(header.Right - 250, header.Y + 4), Color.White, true);

        int visibleRows = GetVisibleRowCount();
        if (entries.Count == 0)
        {
            string text = "没有匹配的快捷键";
            Vector2 size = Game1.smallFont.MeasureString(text);
            DrawText(
                batch,
                text,
                Game1.smallFont,
                new Vector2(rowsArea.Center.X - size.X / 2f, rowsArea.Center.Y - size.Y / 2f),
                Color.Gray,
                false);
            return;
        }

        for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
        {
            int entryIndex = topIndex + rowIndex;
            if (entryIndex >= entries.Count)
                break;

            HotkeyEntry entry = entries[entryIndex];
            DrawRow(batch, GetRowBounds(rowsArea, rowIndex), entry, rowIndex % 2 == 0);
        }

        DrawScrollButtons(batch);
        DrawScrollBar(batch, entries.Count, visibleRows);
    }

    private void DrawRow(SpriteBatch batch, Rectangle row, HotkeyEntry entry, bool even)
    {
        bool conflict = catalogResult.IsConflict(entry);
        Color background = even ? new Color(255, 248, 224) : new Color(244, 232, 198);
        batch.Draw(Game1.staminaRect, row, background * 0.88f);
        batch.Draw(Game1.staminaRect, new Rectangle(row.X, row.Y, 6, row.Height), conflict ? new Color(190, 80, 65) : GetSourceColor(entry.Source));

        DrawBindingPills(batch, entry, new Rectangle(row.X + 18, row.Y + 9, 222, row.Height - 18));
        DrawTruncatedText(batch, entry.Action, Game1.smallFont, new Vector2(row.X + 260, row.Y + 10), Game1.textColor, row.Width - 660f);
        DrawSourceBadge(batch, new Rectangle(row.Right - 392, row.Y + 11, 96, 30), entry.SourceLabel, GetSourceColor(entry.Source));
        DrawTruncatedText(batch, GetOwnerDisplay(entry), Game1.smallFont, new Vector2(row.Right - 270, row.Y + 10), Game1.textColor, 222f);

        if (conflict)
        {
            Rectangle marker = new(row.Right - 34, row.Y + 12, 24, 28);
            batch.Draw(Game1.staminaRect, marker, new Color(190, 80, 65));
            DrawCenteredText(batch, "!", Game1.smallFont, marker, Color.White, true);
        }
    }

    private static string GetOwnerDisplay(HotkeyEntry entry)
    {
        return entry.Source == HotkeySource.Game ? "原版设置" : entry.OwnerName;
    }

    private void DrawBindingPills(SpriteBatch batch, HotkeyEntry entry, Rectangle bounds)
    {
        int x = bounds.X;
        foreach (HotkeyBinding binding in entry.Bindings.Take(3))
        {
            string label = GetDisplayBindingLabel(binding);
            int width = Math.Min(190, Math.Max(48, (int)Game1.smallFont.MeasureString(label).X + 24));
            if (x + width > bounds.Right)
                break;

            Rectangle pill = new(x, bounds.Y, width, 32);
            batch.Draw(Game1.staminaRect, pill, catalogResult.BindingUseCounts.TryGetValue(binding.Normalized, out int count) && count > 1 ? new Color(190, 80, 65) : new Color(86, 118, 164));
            DrawTruncatedText(batch, label, Game1.smallFont, new Vector2(pill.X + 8, pill.Y + 4), Color.White, pill.Width - 16f);
            x += width + 8;
        }

        if (entry.Bindings.Count > 3)
            DrawText(batch, $"+{entry.Bindings.Count - 3}", Game1.smallFont, new Vector2(x, bounds.Y + 4), Color.Gray, false);
    }

    private void DrawSourceBadge(SpriteBatch batch, Rectangle bounds, string label, Color color)
    {
        batch.Draw(Game1.staminaRect, bounds, color * 0.9f);
        DrawCenteredText(batch, label, Game1.smallFont, bounds, Color.White, true);
    }

    private void DrawScrollButtons(SpriteBatch batch)
    {
        DrawButton(batch, GetUpArrowBounds(), "▲", false);
        DrawButton(batch, GetDownArrowBounds(), "▼", false);
    }

    private void DrawScrollBar(SpriteBatch batch, int entryCount, int visibleRows)
    {
        if (entryCount <= visibleRows)
            return;

        Rectangle runner = GetScrollBarRunnerBounds();
        IClickableMenu.drawTextureBox(batch, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), runner.X, runner.Y, runner.Width, runner.Height, Color.White, 4f, false);

        int maxTopIndex = Math.Max(1, entryCount - visibleRows);
        int thumbHeight = Math.Max(42, (int)(runner.Height * (visibleRows / (float)entryCount)));
        int thumbY = runner.Y + (int)((runner.Height - thumbHeight) * (topIndex / (float)maxTopIndex));
        Rectangle thumb = new(runner.X + 4, thumbY, runner.Width - 8, thumbHeight);
        batch.Draw(Game1.staminaRect, thumb, new Color(112, 84, 55));
    }

    private void DrawFooter(SpriteBatch batch)
    {
        string warning = "滚轮 / ↑↓ 翻页，Esc 关闭。默认只显示键鼠按键；推测项来自 config.json，准确性低于 GMCM。";
        DrawTruncatedText(
            batch,
            warning,
            Game1.smallFont,
            new Vector2(xPositionOnScreen + 48, yPositionOnScreen + height - 52),
            new Color(96, 64, 32),
            width - 96f);
    }

    private List<HotkeyEntry> GetFilteredEntries()
    {
        string query = searchBox.Text.Trim();
        IEnumerable<HotkeyEntry> entries = catalogResult.Entries;

        entries = filter switch
        {
            ViewerFilter.Conflicts => entries.Where(catalogResult.IsConflict),
            ViewerFilter.Game => entries.Where(entry => entry.Source == HotkeySource.Game),
            ViewerFilter.Mods => entries.Where(entry => entry.Source != HotkeySource.Game),
            _ => entries
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries.Where(entry =>
                Contains(entry.BindingText, query)
                || Contains(entry.Action, query)
                || Contains(entry.OwnerName, query)
                || Contains(entry.OwnerId, query)
                || Contains(entry.Detail, query));
        }

        return entries.ToList();
    }

    private void Scroll(int delta)
    {
        List<HotkeyEntry> entries = GetFilteredEntries();
        int maxTopIndex = Math.Max(0, entries.Count - GetVisibleRowCount());
        topIndex = Math.Clamp(topIndex + delta, 0, maxTopIndex);
        Game1.playSound("shiny4");
    }

    private void ClampTopIndex(int entryCount)
    {
        topIndex = Math.Clamp(topIndex, 0, Math.Max(0, entryCount - GetVisibleRowCount()));
    }

    private int GetVisibleRowCount()
    {
        return Math.Max(1, GetRowsAreaBounds().Height / RowHeight);
    }

    private Rectangle GetRowsAreaBounds()
    {
        return new Rectangle(xPositionOnScreen + 42, yPositionOnScreen + 220, width - 84, height - 284);
    }

    private Rectangle GetRowBounds(Rectangle rowsArea, int rowIndex)
    {
        return new Rectangle(rowsArea.X, rowsArea.Y + rowIndex * RowHeight, rowsArea.Width - 44, RowHeight - 6);
    }

    private Rectangle GetScrollBarRunnerBounds()
    {
        Rectangle rowsArea = GetRowsAreaBounds();
        return new Rectangle(rowsArea.Right - 32, rowsArea.Y, 24, rowsArea.Height - 6);
    }

    private Rectangle GetUpArrowBounds()
    {
        Rectangle runner = GetScrollBarRunnerBounds();
        return new Rectangle(runner.X - 10, runner.Y - 42, 44, 34);
    }

    private Rectangle GetDownArrowBounds()
    {
        Rectangle runner = GetScrollBarRunnerBounds();
        return new Rectangle(runner.X - 10, runner.Bottom + 8, 44, 34);
    }

    private Rectangle GetFilterButtonBounds(int index)
    {
        return new Rectangle(xPositionOnScreen + 42 + index * 112, yPositionOnScreen + 152, 102, 36);
    }

    private Rectangle GetRefreshButtonBounds()
    {
        return new Rectangle(xPositionOnScreen + 42 + 4 * 112 + 12, yPositionOnScreen + 152, 92, 36);
    }

    private Rectangle GetSearchBounds()
    {
        return new Rectangle(xPositionOnScreen + width - 404, yPositionOnScreen + 148, 350, 44);
    }

    private void PositionSearchBox()
    {
        Rectangle bounds = GetSearchBounds();
        searchBox.X = bounds.X;
        searchBox.Y = bounds.Y;
        searchBox.Width = bounds.Width;
        searchBox.Height = bounds.Height;
    }

    private static string GetDisplayBindingLabel(HotkeyBinding binding)
    {
        return string.Join(
            "+",
            binding.Display.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CompactButtonName));
    }

    private static string CompactButtonName(string button)
    {
        return button switch
        {
            "LeftControl" or "RightControl" => "Ctrl",
            "LeftShift" or "RightShift" => "Shift",
            "LeftAlt" or "RightAlt" => "Alt",
            "MouseLeft" => "鼠标左",
            "MouseRight" => "鼠标右",
            "MouseMiddle" => "鼠标中",
            "OemQuestion" => "?",
            "OemTilde" => "~",
            "OemPipe" => "\\",
            "OemPeriod" => ".",
            "OemComma" => ",",
            "PageUp" => "PgUp",
            "PageDown" => "PgDn",
            "Escape" => "Esc",
            "Space" => "空格",
            "Enter" => "回车",
            "Delete" => "Del",
            _ => button
        };
    }

    private static string GetFilterLabel(ViewerFilter target)
    {
        return target switch
        {
            ViewerFilter.All => "全部",
            ViewerFilter.Conflicts => "冲突",
            ViewerFilter.Game => "本体",
            ViewerFilter.Mods => "模组",
            _ => "未知"
        };
    }

    private static Color GetSourceColor(HotkeySource source)
    {
        return source switch
        {
            HotkeySource.Game => new Color(86, 145, 92),
            HotkeySource.GenericModConfigMenu => new Color(96, 128, 170),
            HotkeySource.ConfigGuess => new Color(180, 134, 62),
            _ => Color.Gray
        };
    }

    private static void DrawButton(SpriteBatch batch, Rectangle bounds, string label, bool selected)
    {
        Color color = selected ? new Color(112, 84, 55) : new Color(180, 134, 62);
        IClickableMenu.drawTextureBox(batch, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 4f, false);
        batch.Draw(Game1.staminaRect, new Rectangle(bounds.X + 6, bounds.Y + 6, bounds.Width - 12, bounds.Height - 12), color * 0.88f);
        DrawCenteredText(batch, label, Game1.smallFont, bounds, Color.White, true);
    }

    private static void DrawCenteredText(SpriteBatch batch, string text, SpriteFont font, Rectangle bounds, Color color, bool shadow)
    {
        Vector2 size = font.MeasureString(text);
        DrawText(batch, text, font, new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f), color, shadow);
    }

    private static void DrawTruncatedText(SpriteBatch batch, string text, SpriteFont font, Vector2 position, Color color, float maxWidth)
    {
        if (maxWidth <= 0)
            return;

        string value = text;
        while (value.Length > 1 && font.MeasureString(value).X > maxWidth)
            value = value[..^2] + "…";

        DrawText(batch, value, font, position, color, false);
    }

    private static void DrawText(SpriteBatch batch, string text, SpriteFont font, Vector2 position, Color color, bool shadow)
    {
        if (shadow)
            batch.DrawString(font, text, position + new Vector2(2f, 2f), Color.Black * 0.35f);
        batch.DrawString(font, text, position, color);
    }

    private static Rectangle Offset(Rectangle rectangle, int x, int y)
    {
        return new Rectangle(rectangle.X + x, rectangle.Y + y, rectangle.Width, rectangle.Height);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMenuWidth()
    {
        return Math.Clamp(Game1.uiViewport.Width - 32, 940, 1320);
    }

    private static int GetMenuHeight()
    {
        return Math.Clamp(Game1.uiViewport.Height - 32, 700, 920);
    }

    private static int GetMenuX()
    {
        return (Game1.uiViewport.Width - GetMenuWidth()) / 2;
    }

    private static int GetMenuY()
    {
        return (Game1.uiViewport.Height - GetMenuHeight()) / 2;
    }

    private enum ViewerFilter
    {
        All,
        Conflicts,
        Game,
        Mods
    }
}
