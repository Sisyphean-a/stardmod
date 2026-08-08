using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace Toolbox;

internal sealed class ToolboxOptionsPage : IClickableMenu
{
    private readonly Func<ModConfig> getConfig;
    private readonly Action<bool, bool> persistConfig;
    private SettingsSection section;

    internal ToolboxOptionsPage(GameMenu menu, Func<ModConfig> getConfig, Action<bool, bool> persistConfig)
        : base(menu.xPositionOnScreen, menu.yPositionOnScreen + 10, menu.width, menu.height, false)
    {
        this.getConfig = getConfig;
        this.persistConfig = persistConfig;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (GameMenu.forcePreventClose)
            return;

        if (GetSectionBounds(SettingsSection.Features).Contains(x, y))
        {
            section = SettingsSection.Features;
            Game1.playSound("smallSelect");
            return;
        }

        if (GetSectionBounds(SettingsSection.Values).Contains(x, y))
        {
            section = SettingsSection.Values;
            Game1.playSound("smallSelect");
            return;
        }

        for (int index = 0; index < GetRowCount(); index++)
        {
            Rectangle row = GetRowBounds(index);
            if (!row.Contains(x, y))
                continue;

            if (section == SettingsSection.Features && GetToggleBounds(row).Contains(x, y))
                ToggleFeature(index);
            else if (section == SettingsSection.Values)
                AdjustValue(index, GetDecreaseBounds(row).Contains(x, y), GetIncreaseBounds(row).Contains(x, y));

            return;
        }
    }

    public override void draw(SpriteBatch batch)
    {
        Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen - 10, width, height, false, true, null, false, true, -1, -1, -1);
        DrawCenteredText(batch, "工具箱", new Rectangle(xPositionOnScreen + 32, yPositionOnScreen + 24, 180, 42), Color.Black);
        DrawSectionButton(batch, SettingsSection.Features, "功能");
        DrawSectionButton(batch, SettingsSection.Values, "参数");

        for (int index = 0; index < GetRowCount(); index++)
        {
            Rectangle row = GetRowBounds(index);
            batch.Draw(Game1.staminaRect, row, new Color(0, 0, 0, 28));
            if (section == SettingsSection.Features)
                DrawFeatureRow(batch, row, index);
            else
                DrawValueRow(batch, row, index);
        }
    }

    private void ToggleFeature(int index)
    {
        ModConfig config = getConfig();
        bool refreshLights = false;
        bool petAnimals = false;

        switch (index)
        {
            case 0:
                config.EnableAutoPet = !config.EnableAutoPet;
                petAnimals = true;
                break;
            case 1:
                config.EnableFurnitureLightRadius = !config.EnableFurnitureLightRadius;
                refreshLights = true;
                break;
            case 2:
                config.EnableObjectLightRadius = !config.EnableObjectLightRadius;
                refreshLights = true;
                break;
            case 3:
                config.EnableFarmMusic = !config.EnableFarmMusic;
                break;
            case 4:
                config.EnableFenceDecay = !config.EnableFenceDecay;
                break;
            case 5:
                config.EnableAutomaticGates = !config.EnableAutomaticGates;
                break;
            case 6:
                config.EnableInputMethodControl = !config.EnableInputMethodControl;
                break;
            case 7:
                config.EnableHarvestWithScythe = !config.EnableHarvestWithScythe;
                break;
            default:
                return;
        }

        persistConfig(refreshLights, petAnimals);
        Game1.playSound("smallSelect");
    }

    private void AdjustValue(int index, bool decrease, bool increase)
    {
        if (!decrease && !increase)
            return;

        int direction = increase ? 1 : -1;
        ModConfig config = getConfig();
        bool refreshLights = false;
        bool petAnimals = false;

        switch (index)
        {
            case 0:
                config.CheckInterval = Math.Clamp(config.CheckInterval + direction * 5, 5, 60);
                petAnimals = true;
                break;
            case 1:
                config.ScanRange = Math.Clamp(config.ScanRange + direction, 1, 5);
                petAnimals = true;
                break;
            case 2:
                config.FurnitureLightRadius = MathF.Round(Math.Max(0.1f, config.FurnitureLightRadius + direction * 0.1f), 1);
                refreshLights = true;
                break;
            case 3:
                config.ObjectLightRadius = MathF.Round(Math.Max(0.1f, config.ObjectLightRadius + direction * 0.1f), 1);
                refreshLights = true;
                break;
            case 4:
                config.AutomaticGateCloseDelay = Math.Max(0, config.AutomaticGateCloseDelay + direction * 100);
                break;
            default:
                return;
        }

        persistConfig(refreshLights, petAnimals);
        Game1.playSound("smallSelect");
    }

    private void DrawFeatureRow(SpriteBatch batch, Rectangle row, int index)
    {
        (string label, bool enabled) = index switch
        {
            0 => ("自动抚摸", getConfig().EnableAutoPet),
            1 => ("家具光源半径", getConfig().EnableFurnitureLightRadius),
            2 => ("物体光源半径", getConfig().EnableObjectLightRadius),
            3 => ("农场音乐保持", getConfig().EnableFarmMusic),
            4 => ("栅栏防腐朽", getConfig().EnableFenceDecay),
            5 => ("自动开关门", getConfig().EnableAutomaticGates),
            6 => ("自动输入法控制", getConfig().EnableInputMethodControl),
            7 => ("镰刀收割", getConfig().EnableHarvestWithScythe),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        DrawText(batch, label, new Vector2(row.X + 20, row.Y + 18), Color.Black);
        DrawButton(batch, GetToggleBounds(row), enabled ? "开" : "关", enabled ? Color.ForestGreen : Color.Firebrick);
    }

    private void DrawValueRow(SpriteBatch batch, Rectangle row, int index)
    {
        (string label, string value) = index switch
        {
            0 => ("自动抚摸检查间隔", $"{getConfig().CheckInterval} 帧"),
            1 => ("自动抚摸扫描范围", $"{getConfig().ScanRange} 格"),
            2 => ("家具光源半径倍率", getConfig().FurnitureLightRadius.ToString("0.0")),
            3 => ("物体光源半径倍率", getConfig().ObjectLightRadius.ToString("0.0")),
            4 => ("自动关门延迟", $"{getConfig().AutomaticGateCloseDelay} 毫秒"),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        DrawText(batch, label, new Vector2(row.X + 20, row.Y + 18), Color.Black);
        DrawButton(batch, GetDecreaseBounds(row), "−", Color.SlateGray);
        DrawCenteredText(batch, value, GetValueBounds(row), Color.Black);
        DrawButton(batch, GetIncreaseBounds(row), "+", Color.SlateGray);
    }

    private void DrawSectionButton(SpriteBatch batch, SettingsSection target, string label)
    {
        DrawButton(
            batch,
            GetSectionBounds(target),
            label,
            section == target ? Color.SteelBlue : Color.SlateGray);
    }

    private static void DrawButton(SpriteBatch batch, Rectangle bounds, string label, Color color)
    {
        batch.Draw(Game1.staminaRect, bounds, color);
        DrawCenteredText(batch, label, bounds, Color.White);
    }

    private static void DrawText(SpriteBatch batch, string text, Vector2 position, Color color)
    {
        batch.DrawString(Game1.smallFont, text, position, color);
    }

    private static void DrawCenteredText(SpriteBatch batch, string text, Rectangle bounds, Color color)
    {
        Vector2 size = Game1.smallFont.MeasureString(text);
        Vector2 position = new(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f);
        batch.DrawString(Game1.smallFont, text, position, color);
    }

    private int GetRowCount() => section == SettingsSection.Features ? 8 : 5;

    private Rectangle GetRowBounds(int index)
    {
        int top = yPositionOnScreen + 112;
        int availableHeight = height - 144;
        int rowHeight = availableHeight / GetRowCount();
        return new Rectangle(xPositionOnScreen + 36, top + index * rowHeight, width - 72, rowHeight - 6);
    }

    private Rectangle GetSectionBounds(SettingsSection target)
    {
        int x = xPositionOnScreen + width - (target == SettingsSection.Features ? 244 : 132);
        return new Rectangle(x, yPositionOnScreen + 28, 100, 40);
    }

    private static Rectangle GetToggleBounds(Rectangle row)
    {
        return new Rectangle(row.Right - 96, row.Y + 12, 76, row.Height - 24);
    }

    private static Rectangle GetDecreaseBounds(Rectangle row)
    {
        return new Rectangle(row.Right - 228, row.Y + 12, 40, row.Height - 24);
    }

    private static Rectangle GetValueBounds(Rectangle row)
    {
        return new Rectangle(row.Right - 182, row.Y + 8, 128, row.Height - 16);
    }

    private static Rectangle GetIncreaseBounds(Rectangle row)
    {
        return new Rectangle(row.Right - 48, row.Y + 12, 40, row.Height - 24);
    }

    private enum SettingsSection
    {
        Features,
        Values
    }
}
