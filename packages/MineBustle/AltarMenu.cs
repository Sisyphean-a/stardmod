using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using System;

namespace MineBustle
{
    public class AltarMenu : IClickableMenu
    {
        // 保持窗口尺寸
        private const int MenuWidth = 800;
        private const int MenuHeight = 500;

        // 布局常量
        private const int SliderBarWidth = 500;
        private const int SliderBarHeight = 24;
        private const int ButtonGap = 16;

        private Rectangle sliderBounds;
        private Rectangle sliderKnobBounds;
        private bool isDraggingSlider = false;
        private float sliderPosition = 0f;

        private ClickableTextureComponent confirmButton;
        private ClickableTextureComponent cancelButton;

        private double currentMultiplier = 1.0;
        private int currentCost = 0;

        // 缓存标题位置
        private Vector2 titlePosition;
        private Vector2 descriptionPosition;

        public AltarMenu() : base(
            (Game1.uiViewport.Width - MenuWidth) / 2,
            (Game1.uiViewport.Height - MenuHeight) / 2,
            MenuWidth,
            MenuHeight)
        {
            // === 核心修复 1: 大幅增加顶部边距 ===
            // 之前的 +96 还不够，对话框顶部边框很厚，这里推到 +138 开始画内容
            int contentTop = yPositionOnScreen + 138;

            // 1. 标题位置 (从内容顶部开始)
            string title = ModEntry.ModHelper.Translation.Get("ui.altar.title");
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title);
            // 标题稍微往上提一点点，作为大标题
            titlePosition = new Vector2(xPositionOnScreen + (MenuWidth - titleSize.X) / 2, contentTop - 40);

            // 2. 描述位置 (放在标题下方)
            string description = ModEntry.ModHelper.Translation.Get("ui.altar.description");
            Vector2 descSize = Game1.smallFont.MeasureString(description);
            descriptionPosition = new Vector2(xPositionOnScreen + (MenuWidth - descSize.X) / 2, contentTop + 20);

            // 3. 滑块位置 (位于描述下方 50px)
            sliderBounds = new Rectangle(
                xPositionOnScreen + (MenuWidth - SliderBarWidth) / 2,
                contentTop + 80,
                SliderBarWidth,
                SliderBarHeight
            );

            UpdateSliderKnob();

            // 4. 按钮位置 (底部留出空间)
            int buttonY = yPositionOnScreen + MenuHeight - 90;
            int buttonTotalWidth = 64 + 64 + ButtonGap;
            int buttonStartX = xPositionOnScreen + (MenuWidth - buttonTotalWidth) / 2;

            confirmButton = new ClickableTextureComponent(
                new Rectangle(buttonStartX, buttonY, 64, 64),
                Game1.mouseCursors,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46),
                1f
            ) { myID = 101, rightNeighborID = 102 };

            cancelButton = new ClickableTextureComponent(
                new Rectangle(buttonStartX + 64 + ButtonGap, buttonY, 64, 64),
                Game1.mouseCursors,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 47),
                1f
            ) { myID = 102, leftNeighborID = 101 };

            // 初始化计算
            UpdateMultiplierAndCost();

            if (Game1.options.SnappyMenus)
            {
                populateClickableComponentList();
                snapToDefaultClickableComponent();
            }
        }

        private void UpdateSliderKnob()
        {
            // 视觉大小 32x32，看起来更精致
            int knobSize = 32;
            int knobX = sliderBounds.X + (int)(sliderPosition * (SliderBarWidth - knobSize));
            int knobY = sliderBounds.Y + (sliderBounds.Height - knobSize) / 2;

            sliderKnobBounds = new Rectangle(knobX, knobY, knobSize, knobSize);
        }

        private void UpdateMultiplierAndCost()
        {
            // 范围：1.0 到 10.0 (增量 9.0)
            currentMultiplier = 1.0 + (sliderPosition * 9.0);
            currentCost = CalculateCost(currentMultiplier);
        }

        // === 核心修复 3: 实装真实的配置算法 ===
        private int CalculateCost(double multiplier)
        {
            try
            {
                 // 从 ModEntry 获取配置
                 var config = ModEntry.Config;

                 double baseFee = config.BaseFee;
                 double totalEarnings = Game1.player.totalMoneyEarned;
                 double alpha = config.InflationCoefficient;
                 double beta = config.PenaltyExponent;

                 // 公式: (基础费 + 通胀系数 * 总收入) * (倍率增量 ^ 惩罚指数)
                 // 注意：multiplier - 1.0 是因为 1.0 倍率（无加成）应该是 0 费或者基础费，
                 // 但根据你的公式，如果 multiplier 是 1.0，Pow 结果是 0，费用为 0。这是合理的（不献祭不花钱）。
                 // 如果你想让 1.0 倍率也有基础费，公式可能需要调整。这里按你的公式原样实现。

                 double cost = (baseFee + alpha * totalEarnings) * Math.Pow(multiplier - 1.0, beta);

                 // 确保最小为0
                 return Math.Max(0, (int)Math.Round(cost));
            }
            catch
            {
                // 如果出错（比如开发环境下没加载ModEntry），返回默认值防止崩溃
                return (int)(100 * multiplier);
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            Rectangle extendedKnobBounds = sliderKnobBounds;
            extendedKnobBounds.Inflate(10, 10);

            if (sliderBounds.Contains(x,y) || extendedKnobBounds.Contains(x, y))
            {
                isDraggingSlider = true;
                Game1.playSound("drumkit6");
                PerformSlide(x);
            }
            else if (confirmButton.containsPoint(x, y))
            {
                ConfirmOffering();
            }
            else if (cancelButton.containsPoint(x, y))
            {
                exitThisMenu();
                Game1.playSound("bigDeSelect");
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            isDraggingSlider = false;
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            confirmButton.tryHover(x, y);
            cancelButton.tryHover(x, y);

            if (isDraggingSlider)
            {
                PerformSlide(x);
            }
        }

        private void PerformSlide(int mouseX)
        {
            float newPosition = (float)(mouseX - sliderBounds.X - sliderKnobBounds.Width / 2) / (SliderBarWidth - sliderKnobBounds.Width);
            sliderPosition = Math.Clamp(newPosition, 0f, 1f);
            UpdateSliderKnob();
            UpdateMultiplierAndCost();
        }

        private void ConfirmOffering()
        {
            if (Game1.player.Money < currentCost)
            {
                Game1.playSound("cancel");
                Game1.showRedMessage(ModEntry.ModHelper.Translation.Get("ui.altar.not_enough_money"));
                return;
            }

            // 扣钱
            Game1.player.Money -= currentCost;

            // 保存配置
            ModEntry.Config.CurrentMultiplier = currentMultiplier;
            ModEntry.SaveConfig();

            Game1.playSound("yoba");

            // 特效
            Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite(
                "LooseSprites\\Cursors",
                new Rectangle(432, 1664, 16, 16),
                80f, 8, 0, Game1.player.Position, false, false, 1f, 0f, Color.Gold, 4f, 0f, 0f, 0f
            ));

            string message = ModEntry.ModHelper.Translation.Get("ui.altar.success_hud", new { val = $"{currentMultiplier:F1}" });
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.achievement_type));

            ModEntry.ModMonitor.Log($"玩家献祭 {currentCost}g，设置倍率为 {currentMultiplier:F1}x", StardewModdingAPI.LogLevel.Info);

            exitThisMenu();
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);

            // 画主背景
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, MenuWidth, MenuHeight, false, true);

            // 1. 标题
            Utility.drawTextWithShadow(b, ModEntry.ModHelper.Translation.Get("ui.altar.title"), Game1.dialogueFont,
                titlePosition, Game1.textColor);

            // 2. 描述 (=== 核心修复 2: 使用 Game1.textColor 提高对比度 ===)
            Utility.drawTextWithShadow(b, ModEntry.ModHelper.Translation.Get("ui.altar.description"), Game1.smallFont,
                descriptionPosition, Game1.textColor); // 这里改为了 textColor，深褐色

            // 3. 滑块组件
            DrawSliderTrack(b);
            DrawSliderKnob(b);

            // 计算信息显示的垂直位置 (滑块下方 60px)
            float infoStartHeight = sliderBounds.Y + 60;

            // 4. 倍率显示
            string multiplierText = ModEntry.ModHelper.Translation.Get("ui.altar.multiplier_label", new { val = $"{currentMultiplier:F1}" });
            Vector2 multiplierSize = Game1.dialogueFont.MeasureString(multiplierText);
            Utility.drawTextWithShadow(b, multiplierText, Game1.dialogueFont,
                new Vector2(xPositionOnScreen + (MenuWidth - multiplierSize.X) / 2, infoStartHeight), Game1.textColor);

            // 5. 费用显示
            bool canAfford = Game1.player.Money >= currentCost;
            // 自定义一个深红色，比纯红看起来舒服一点
            Color costColor = canAfford ? new Color(50, 150, 50) : new Color(200, 60, 60);

            string costText = ModEntry.ModHelper.Translation.Get("ui.altar.cost_label", new { val = currentCost });
            Vector2 costSize = Game1.dialogueFont.MeasureString(costText);
            Utility.drawTextWithShadow(b, costText, Game1.dialogueFont,
                new Vector2(xPositionOnScreen + (MenuWidth - costSize.X) / 2, infoStartHeight + 50), costColor);

            // 6. 按钮
            confirmButton.draw(b);
            cancelButton.draw(b);

            drawMouse(b);
        }

        private void DrawSliderTrack(SpriteBatch b)
        {
            // 轨道底色 (深灰)
            Rectangle trackRect = new Rectangle(sliderBounds.X, sliderBounds.Y + (sliderBounds.Height - 6) / 2, sliderBounds.Width, 6);
            b.Draw(Game1.staminaRect, trackRect, Color.DarkGray);

            // 轨道进度 (金色)
            int highlightWidth = (int)(sliderPosition * sliderBounds.Width);
            highlightWidth = Math.Max(0, Math.Min(highlightWidth, sliderBounds.Width));

            Rectangle fillRect = new Rectangle(sliderBounds.X, trackRect.Y, highlightWidth, 6);
            b.Draw(Game1.staminaRect, fillRect, Color.Orange); // 用 Orange 或 Gold
        }

        private void DrawSliderKnob(SpriteBatch b)
        {
            // === 核心修复 4: 手绘滑块，不再依赖贴图 ===
            // 之前的贴图坐标可能在你的版本里对不上，导致出现杂色。
            // 这里我们用代码画一个带边框的方块，类似于游戏里的复选框风格。

            // 1. 画边框 (深棕色)
            b.Draw(Game1.staminaRect, sliderKnobBounds, new Color(139, 69, 19));

            // 2. 画内部 (金色/米色)，稍微缩小2像素露出边框
            Rectangle inner = sliderKnobBounds;
            inner.Inflate(-2, -2);
            b.Draw(Game1.staminaRect, inner, Color.Gold);

            // 3. 画个高光点 (可选，让它看起来立体点)
            b.Draw(Game1.staminaRect, new Rectangle(inner.X + 2, inner.Y + 2, 8, 8), Color.White * 0.4f);
        }
    }
}