using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Tools;

namespace FishingBarGrowth;

/// <summary>
/// 钓鱼统计HUD显示类
/// </summary>
public class FishingHUD
{
    private ModConfig _config;
    private readonly Func<string, string> _getTranslation;

    public FishingHUD(ModConfig config, Func<string, string> getTranslation)
    {
        _config = config;
        _getTranslation = getTranslation;
    }

    public void UpdateConfig(ModConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 绘制HUD
    /// </summary>
    public void Draw(SpriteBatch spriteBatch)
    {
        // 检查是否启用HUD
        if (!_config.ShowFishingHUD || !_config.EnableMod)
            return;

        // 检查玩家是否手持鱼竿
        if (Game1.player?.CurrentTool is not FishingRod)
            return;

        // 获取统计数据
        int totalFish = FishCounter.GetTotalFishCount(_config.ExcludeAlgae, false);

        // 计算显示位置
        int x = _config.HudXOffset;
        int y = Game1.uiViewport.Height - _config.HudYOffset;

        // 检查是否有钓鱼数据
        if (!BobberBarPatch.HasFishingData)
        {
            // 没有数据时显示提示信息
            DrawBackground(spriteBatch, x - 10, y - 10, 380, 110);

            int lineHeight = 32;
            int currentY = y;

            DrawText(spriteBatch, _getTranslation("hud.title"), x, currentY, Color.Gold);
            currentY += lineHeight;

            DrawText(spriteBatch, string.Format(_getTranslation("hud.fishCount"), totalFish), x, currentY, Color.White);
            currentY += lineHeight;

            DrawText(spriteBatch, _getTranslation("hud.waiting"), x, currentY, Color.Orange);
            return;
        }

        // 从BobberBarPatch获取最后一次钓鱼的实际数据
        int baseBarHeight = BobberBarPatch.LastBaseHeight;      // 游戏计算的基础高度(等级+装备)
        int bonusPixels = BobberBarPatch.LastBonusPixels;       // 我们添加的奖励
        int currentBarHeight = BobberBarPatch.LastFinalHeight;  // 最终高度

        // 绘制半透明背景 (4行文本,每行32px,加上边距)
        DrawBackground(spriteBatch, x - 10, y - 10, 380, 140);

        // 绘制文本
        int lineHeight2 = 32;
        int currentY2 = y;

        // 标题
        DrawText(spriteBatch, _getTranslation("hud.title"), x, currentY2, Color.Gold);
        currentY2 += lineHeight2;

        // 总鱼数
        DrawText(spriteBatch, string.Format(_getTranslation("hud.fishCount"), totalFish), x, currentY2, Color.White);
        currentY2 += lineHeight2;

        // 钓鱼条长度
        DrawText(spriteBatch, string.Format(_getTranslation("hud.barHeight"), currentBarHeight), x, currentY2, Color.LightGreen);
        currentY2 += lineHeight2;

        // 额外增益
        string bonusText = bonusPixels > 0
            ? string.Format(_getTranslation("hud.breakdown"), baseBarHeight, bonusPixels)
            : string.Format(_getTranslation("hud.baseOnly"), baseBarHeight);
        DrawText(spriteBatch, bonusText, x, currentY2, Color.LightBlue);
    }

    /// <summary>
    /// 绘制半透明背景
    /// </summary>
    private void DrawBackground(SpriteBatch spriteBatch, int x, int y, int width, int height)
    {
        // 创建1x1的纯色纹理
        Texture2D pixel = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.Black });

        // 绘制半透明黑色背景
        spriteBatch.Draw(
            pixel,
            new Rectangle(x, y, width, height),
            Color.Black * 0.7f
        );
    }

    /// <summary>
    /// 绘制文本
    /// </summary>
    private void DrawText(SpriteBatch spriteBatch, string text, int x, int y, Color color)
    {
        // 使用游戏的小字体
        SpriteFont font = Game1.smallFont;

        // 绘制阴影
        spriteBatch.DrawString(
            font,
            text,
            new Vector2(x + 2, y + 2),
            Color.Black * 0.8f
        );

        // 绘制文本
        spriteBatch.DrawString(
            font,
            text,
            new Vector2(x, y),
            color
        );
    }
}

