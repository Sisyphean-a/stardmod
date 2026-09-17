using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using System;
using PolymorphicAetherRing;

namespace PolymorphicAetherRing.Framework;

/// <summary>熔铸面板UI - 允许玩家将武器熔铸进饰品</summary>
public class FusionMenu : IClickableMenu
{
    private readonly Item _trinket;
    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly ModConfig _config;

    // UI Components
    private InventoryMenu _inventory = null!; // Suppress null warning as it is initialized in InitializeLayout

    // Layout
    private Rectangle _weaponSlotBounds;
    private Rectangle _fuseButtonBounds;

    // State
    private MeleeWeapon? _slottedWeapon;
    private FusedWeaponData? _currentFusion;
    private bool _hasCorruptFusionData;
    private bool _hoveringFuseButton;
    private bool _hoveringWeaponSlot;

    private readonly string _hoverText = "";

    public FusionMenu(Item trinket, IModHelper helper, IMonitor monitor, ModConfig config)
        : base(
            (Game1.uiViewport.Width - Math.Min(1000, Game1.uiViewport.Width - 64)) / 2,
            (Game1.uiViewport.Height - Math.Min(704, Game1.uiViewport.Height - 64)) / 2,
            Math.Min(1000, Game1.uiViewport.Width - 64),
            Math.Min(704, Game1.uiViewport.Height - 64),
            showUpperRightCloseButton: true
        )
    {
        _trinket = trinket;
        _helper = helper;
        _monitor = monitor;
        _config = config;

        // 读取当前熔铸状态。损坏数据必须保留，不能被下一次熔铸静默覆盖。
        try
        {
            _currentFusion = FusedWeaponData.FromModData(trinket);
        }
        catch (InvalidDataException exception)
        {
            _hasCorruptFusionData = true;
            _monitor.Log($"Failed to load fused weapon data: {exception}", LogLevel.Error);
            Game1.showRedMessage(_helper.Translation.Get("menu.fusion.error.corrupt_data"));
        }

        // 初始化UI区域
        InitializeLayout();
    }

    private void InitializeLayout()
    {
        // ... (layout initialization code)
        // 1. 初始化库存菜单 (放置在窗口下半部分)
        // InventoryMenu 通常宽为 12 * 64 + 边距
        _inventory = new InventoryMenu(
            this.xPositionOnScreen + (this.width - (12 * 64)) / 2, // 居中
            this.yPositionOnScreen + this.height - (3 * 64) - 80,  // 底部留出空间
            playerInventory: true,
            highlightMethod: HighlightWeapons, // 只高亮武器
            capacity: 36,
            rows: 3
        );

        // 2. 武器插槽 (上半部分居中)
        const int slotSize = 96;
        int slotX = this.xPositionOnScreen + (this.width - slotSize) / 2;
        int slotY = this.yPositionOnScreen + 170;
        _weaponSlotBounds = new Rectangle(slotX, slotY, slotSize, slotSize);

        // 3. 熔铸按钮 (插槽下方)
        int btnWidth = 220;
        int btnHeight = 64;
        _fuseButtonBounds = new Rectangle(
            this.xPositionOnScreen + (this.width - btnWidth) / 2,
            slotY + slotSize + 16,
            btnWidth,
            btnHeight
        );
    }

    /// <summary>高亮过滤：只允许近战武器</summary>
    private bool HighlightWeapons(Item item)
    {
        return item is MeleeWeapon;
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);

        // 重新计算窗口大小 position
        this.width = Math.Min(1000, newBounds.Width - 64);
        this.height = Math.Min(704, newBounds.Height - 64);

        this.xPositionOnScreen = (newBounds.Width - this.width) / 2;
        this.yPositionOnScreen = (newBounds.Height - this.height) / 2;

        // 刷新布局
        InitializeLayout();

        // 刷新关闭按钮位置
        initializeUpperRightCloseButton();
    }

    public override void draw(SpriteBatch b)
    {
        // 1. 绘制黑色遮罩
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);

        // 2. 绘制主窗口背景
        Game1.drawDialogueBox(
            xPositionOnScreen,
            yPositionOnScreen,
            width,
            height,
            speaker: false,
            drawOnlyBox: true
        );

        // 3. 绘制当前熔铸信息
        if (_currentFusion != null && _currentFusion.IsValid)
        {
            string info = _helper.Translation.Get("menu.fusion.current_fusion", new { weaponName = _currentFusion.WeaponName });
            Utility.drawTextWithShadow(
                b,
                info,
                Game1.smallFont,
                new Vector2(xPositionOnScreen + (width - Game1.smallFont.MeasureString(info).X) / 2, yPositionOnScreen + 120),
                Color.White
            );
        }

        // 4. 绘制武器插槽
        // 背景 (使用 drawTextureBox 替代 drawKibbleRect)
        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            _weaponSlotBounds.X,
            _weaponSlotBounds.Y,
            _weaponSlotBounds.Width,
            _weaponSlotBounds.Height,
            Color.White,
            1f,
            false
        );

        // 如果有物品，绘制物品
        if (_slottedWeapon != null)
        {
            float weaponScale = Math.Min(1.25f, (_weaponSlotBounds.Width - 16) / 64f);
            Vector2 weaponPosition = new(
                _weaponSlotBounds.X + (_weaponSlotBounds.Width - 64 * weaponScale) / 2,
                _weaponSlotBounds.Y + (_weaponSlotBounds.Height - 64 * weaponScale) / 2);
            _slottedWeapon.drawInMenu(b, weaponPosition, weaponScale);
        }
        else
        {
            string hint = "+";
            Vector2 hintSize = Game1.dialogueFont.MeasureString(hint);
            Vector2 hintPosition = new(
                _weaponSlotBounds.X + (_weaponSlotBounds.Width - hintSize.X) / 2,
                _weaponSlotBounds.Y + (_weaponSlotBounds.Height - hintSize.Y) / 2);
            Utility.drawTextWithShadow(b, hint, Game1.dialogueFont, hintPosition, Color.Gray);
        }

        // 插槽高亮
        if (_hoveringWeaponSlot)
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(375, 357, 3, 3), _weaponSlotBounds.X, _weaponSlotBounds.Y, _weaponSlotBounds.Width, _weaponSlotBounds.Height, Color.White, 4f, false);
        }

        // 5. 绘制熔铸按钮
        bool canFuse = _slottedWeapon != null && !_hasCorruptFusionData;
        var btnColor = canFuse ? (_hoveringFuseButton ? Color.Wheat : Color.White) : Color.Gray;

        // FIX: 使用标准菜单纹理 (Game1.menuTexture) 而不是不可靠的 mouseCursors 区域
        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60), // 标准褐色背景
            _fuseButtonBounds.X,
            _fuseButtonBounds.Y,
            _fuseButtonBounds.Width,
            _fuseButtonBounds.Height,
            btnColor,
            1f,
            true
        );

        string btnText = _helper.Translation.Get("menu.fusion.fuse_button");
        Vector2 buttonTextPosition = new(
            _fuseButtonBounds.X + (_fuseButtonBounds.Width - Game1.dialogueFont.MeasureString(btnText).X) / 2,
            _fuseButtonBounds.Y + (_fuseButtonBounds.Height - Game1.dialogueFont.MeasureString(btnText).Y) / 2 + 4);
        b.DrawString(Game1.dialogueFont, btnText, buttonTextPosition + new Vector2(2f, 2f), Color.Black);
        b.DrawString(Game1.dialogueFont, btnText, buttonTextPosition, Color.White);

        // 6. 绘制玩家库存
        _inventory.draw(b);

        // 7. 绘制鼠标拖拽的物品
        base.draw(b); // 绘制关闭按钮
        drawMouse(b);

        // 8. 悬浮提示 (Tooltip)
        if (_hoverText.Length > 0)
        {
            IClickableMenu.drawHoverText(b, _hoverText, Game1.smallFont);
        }
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y); // 处理关闭按钮高亮

        // 更新鼠标状态
        _hoveringWeaponSlot = _weaponSlotBounds.Contains(x, y);
        _hoveringFuseButton = _fuseButtonBounds.Contains(x, y);

        // 将悬停事件传递给库存菜单
        // InventoryMenu.hover 会返回高亮的物品，我们这里暂时不需要用到返回值
        _inventory.hover(x, y, Game1.player.CursorSlotItem);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);

        bool handledInventoryClick = false;

        // 1. 检查是否点击了库存中的物品 (拦截逻辑)
        // 遍历库存槽位，看是否点击了某个位置
        int clickedSlot = -1;
        foreach (var c in _inventory.inventory)
        {
            if (c != null && c.containsPoint(x, y))
            {
                if (int.TryParse(c.name, out int slotNumber))
                {
                    clickedSlot = slotNumber;
                    break;
                }
            }
        }

        // 如果点击了有效槽位，并且该位置有物品
        if (clickedSlot != -1 && clickedSlot < Game1.player.Items.Count)
        {
            Item clickedItem = Game1.player.Items[clickedSlot];

            // 如果点击的是近战武器 -> 执行"一键装备/交换"逻辑
            if (clickedItem is MeleeWeapon weapon)
            {
                // 1. 取出新武器
                Game1.player.Items[clickedSlot] = null;

                // 2. 如果当前槽里已有武器，把它放回被点击的格子里
                if (_slottedWeapon != null)
                {
                    Game1.player.Items[clickedSlot] = _slottedWeapon;
                }

                // 3. 装备新武器
                _slottedWeapon = weapon;

                Game1.playSound("stoneStep");
                handledInventoryClick = true;

                _monitor.Log($"Quick-equipped weapon: {weapon.DisplayName}", LogLevel.Debug);
            }
        }

        // 如果没有触发"一键装备" (例如点击了非武器物品，或者空位)，则执行默认库存行为
        if (!handledInventoryClick)
        {
            Game1.player.CursorSlotItem = _inventory.leftClick(x, y, Game1.player.CursorSlotItem, playSound);
        }

        // 2. 检查是否点击了武器插槽
        if (_weaponSlotBounds.Contains(x, y))
        {
            HandleWeaponSlotInteract();
        }

        // 3. 检查熔铸按钮
        if (_fuseButtonBounds.Contains(x, y) && _slottedWeapon != null)
        {
            PerformFusion();
        }
    }

    /// <summary>处理武器槽的交互 (放入/取出)</summary>
    private void HandleWeaponSlotInteract()
    {
        var heldItem = Game1.player.CursorSlotItem;

        // 情况A: 手上有物品
        if (heldItem != null)
        {
            // 必须是近战武器
            if (heldItem is MeleeWeapon weapon)
            {
                // 如果插槽已有武器，交换
                if (_slottedWeapon != null)
                {
                    Game1.player.CursorSlotItem = _slottedWeapon;
                    _slottedWeapon = weapon;
                }
                else
                {
                    // 放入插槽
                    _slottedWeapon = weapon;
                    Game1.player.CursorSlotItem = null;
                }
                Game1.playSound("stoneStep");
                _monitor.Log($"Placed weapon in slot: {weapon.DisplayName}", LogLevel.Debug);
            }
            else
            {
                // 不是武器，播放错误音效或提示
                Game1.playSound("cancel");
                Game1.showRedMessage(_helper.Translation.Get("menu.fusion.error.only_melee"));
            }
        }
        // 情况B: 手上没物品，插槽有物品
        else if (_slottedWeapon != null)
        {
            // 尝试直接放回背包
            var remainder = Game1.player.addItemToInventory(_slottedWeapon);
            if (remainder == null)
            {
                // 成功放回
                _slottedWeapon = null;
                Game1.playSound("coin");
            }
            else
            {
                // 背包满了，原来的逻辑 (拿起)
                Game1.player.CursorSlotItem = _slottedWeapon;
                _slottedWeapon = null;
                Game1.playSound("dwop");
                // 简单提示一下，或者不提示也行，原版行为就是拿起
            }
        }
    }

    private void PerformFusion()
    {
        if (_slottedWeapon == null)
            return;

        if (_hasCorruptFusionData)
        {
            Game1.showRedMessage(_helper.Translation.Get("menu.fusion.error.corrupt_data"));
            return;
        }

        FusedWeaponData fusionData;
        FusedWeaponModDataUpdate pendingUpdate;
        try
        {
            fusionData = FusedWeaponData.FromWeapon(_slottedWeapon);
            pendingUpdate = fusionData.PrepareSave();
        }
        catch (Exception exception)
        {
            _monitor.Log($"Failed to prepare fused weapon data ({_slottedWeapon.DisplayName}): {exception}", LogLevel.Error);
            Game1.showRedMessage(_helper.Translation.Get("menu.fusion.error.save_failed"));
            return;
        }

        if (!TryCreateCurrentFusionWeapon(out MeleeWeapon? oldWeapon, out FusedWeaponData? oldFusion))
            return;

        try
        {
            pendingUpdate.ApplyTo(_trinket);
        }
        catch (Exception exception)
        {
            _monitor.Log($"Failed to save fused weapon data ({fusionData.WeaponName}): {exception}", LogLevel.Error);
            Game1.showRedMessage(_helper.Translation.Get("menu.fusion.error.save_failed"));
            return;
        }

        _currentFusion = fusionData;
        _slottedWeapon = null;
        if (oldWeapon != null && oldFusion != null)
            DeliverReturnedWeapon(oldWeapon, oldFusion);

        Game1.playSound("furnace");
        Game1.playSound("powerup");
        _monitor.Log($"Fused: {fusionData.WeaponName}", LogLevel.Info);
        Game1.showGlobalMessage(_helper.Translation.Get("menu.fusion.success"));
    }

    /// <summary>
    /// Guarantee: 旧武器未能完整重建时返回 false，调用方不会覆盖饰品中的旧熔铸数据。
    /// </summary>
    private bool TryCreateCurrentFusionWeapon(
        out MeleeWeapon? oldWeapon,
        out FusedWeaponData? oldFusion)
    {
        oldWeapon = null;
        oldFusion = null;
        if (!_config.ReturnFusedWeapon || _currentFusion is not { IsValid: true } currentFusion)
            return true;

        try
        {
            oldWeapon = FusedWeaponRestorer.CreateWeapon(currentFusion);
            oldFusion = currentFusion;
            return true;
        }
        catch (Exception exception)
        {
            _monitor.Log($"Failed to return old weapon ({currentFusion.WeaponName}): {exception}", LogLevel.Error);
            Game1.showRedMessage(_helper.Translation.Get("menu.fusion.error.return_failed"));
            return false;
        }
    }

    private void DeliverReturnedWeapon(MeleeWeapon weapon, FusedWeaponData sourceData)
    {
        Item? remainder = Game1.player.addItemToInventory(weapon);
        if (remainder == null)
        {
            Game1.showGlobalMessage(_helper.Translation.Get(
                "menu.fusion.returned",
                new { weaponName = sourceData.WeaponName }));
        }
        else
        {
            Game1.createItemDebris(remainder, Game1.player.getStandingPosition(), -1);
            Game1.showGlobalMessage(_helper.Translation.Get(
                "menu.fusion.inventory_full",
                new { weaponName = sourceData.WeaponName }));
        }

        ShowLegacyEnchantmentWarningIfNeeded(sourceData);
    }

    private void ShowLegacyEnchantmentWarningIfNeeded(FusedWeaponData sourceData)
    {
        if (!sourceData.HasLegacyEnchantmentData)
            return;

        _monitor.Log(
            $"Returned legacy fused weapon '{sourceData.WeaponName}' at known enchantment level 1; the old data did not store original levels.",
            LogLevel.Warn);
        Game1.showRedMessage(_helper.Translation.Get("menu.fusion.warning.legacy_enchantment_levels"));
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        // 允许右键快速取出武器
        if (_weaponSlotBounds.Contains(x, y) && _slottedWeapon != null && Game1.player.CursorSlotItem == null)
        {
            Game1.player.CursorSlotItem = _slottedWeapon;
            _slottedWeapon = null;
            Game1.playSound("dwop");
            return;
        }

        // 传递给库存 (Corrected: Assign result back to CursorSlotItem)
        Game1.player.CursorSlotItem = _inventory.rightClick(x, y, Game1.player.CursorSlotItem, playSound);
    }

    protected override void cleanupBeforeExit()
    {
        // 菜单关闭时，如果插槽有武器，返还给玩家
        if (_slottedWeapon != null)
        {
            Game1.player.addItemByMenuIfNecessary(_slottedWeapon);
            _slottedWeapon = null;
        }

        // 如果鼠标上还拿着东西，也返还
        if (Game1.player.CursorSlotItem != null)
        {
             Game1.player.addItemByMenuIfNecessary(Game1.player.CursorSlotItem);
             Game1.player.CursorSlotItem = null;
        }

        base.cleanupBeforeExit();
    }
}
