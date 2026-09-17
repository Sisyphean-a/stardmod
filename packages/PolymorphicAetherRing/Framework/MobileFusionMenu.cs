using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace PolymorphicAetherRing.Framework;

/// <summary>紧凑屏幕使用的熔铸面板。</summary>
public partial class MobileFusionMenu : IClickableMenu
{
    private const int MaximumMenuWidth = 800;
    private const int MaximumMenuHeight = 480;
    private const int MaximumVisibleWeapons = 5;
    private const int PreferredWeaponItemHeight = 72;

    private readonly Item _trinket;
    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly ModConfig _config;
    private readonly List<MeleeWeapon> _availableWeapons = new();
    private readonly List<Rectangle> _weaponListBounds = new();

    private MeleeWeapon? _slottedWeapon;
    private FusedWeaponData? _currentFusion;
    private bool _hasCorruptFusionData;
    private int _scrollOffset;
    private int _hoveringWeaponIndex = -1;
    private bool _hoveringFuseButton;
    private bool _hoveringWeaponSlot;
    private bool _hoveringCloseButton;

    private Rectangle _leftPanelBounds;
    private Rectangle _rightPanelBounds;
    private Rectangle _weaponSlotBounds;
    private Rectangle _fuseButtonBounds;
    private Rectangle _closeButtonBounds;
    private Rectangle _scrollUpBounds;
    private Rectangle _scrollDownBounds;
    private Rectangle _weaponListViewport;

    private int VisibleWeaponCount => _weaponListBounds.Count;
    private bool CanScrollDown => _scrollOffset + VisibleWeaponCount < _availableWeapons.Count;

    public MobileFusionMenu(Item trinket, IModHelper helper, IMonitor monitor, ModConfig config)
        : base(0, 0, 0, 0, showUpperRightCloseButton: false)
    {
        _trinket = trinket;
        _helper = helper;
        _monitor = monitor;
        _config = config;
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

        CollectAvailableWeapons();
        InitializeLayout();
    }

    private void CollectAvailableWeapons()
    {
        _availableWeapons.Clear();

        foreach (var item in Game1.player.Items)
        {
            if (item is MeleeWeapon weapon)
                _availableWeapons.Add(weapon);
        }
    }

    private void InitializeLayout()
    {
        SetMenuBounds();
        LayoutPanels();
        LayoutFusionControls();
        LayoutWeaponList();
        ClampScrollOffset();
    }

    private void SetMenuBounds()
    {
        int screenWidth = Game1.uiViewport.Width;
        int screenHeight = Game1.uiViewport.Height;
        int horizontalMargin = Math.Min(32, Math.Max(8, screenWidth / 16));
        int verticalMargin = Math.Min(32, Math.Max(8, screenHeight / 16));

        width = Math.Min(MaximumMenuWidth, screenWidth - horizontalMargin * 2);
        height = Math.Min(MaximumMenuHeight, screenHeight - verticalMargin * 2);
        xPositionOnScreen = (screenWidth - width) / 2;
        yPositionOnScreen = (screenHeight - height) / 2;
    }

    private void LayoutPanels()
    {
        int padding = Math.Min(16, Math.Max(6, height / 25));
        int gap = Math.Min(16, Math.Max(6, width / 40));
        int panelWidth = (width - padding * 2 - gap) / 2;
        int panelHeight = height - padding * 2;

        _leftPanelBounds = new Rectangle(xPositionOnScreen + padding, yPositionOnScreen + padding, panelWidth, panelHeight);
        _rightPanelBounds = new Rectangle(_leftPanelBounds.Right + gap, _leftPanelBounds.Y, panelWidth, panelHeight);
    }

    private void LayoutFusionControls()
    {
        int padding = Math.Max(8, _leftPanelBounds.Height / 24);
        int closeSize = Math.Clamp(_leftPanelBounds.Height / 6, 40, 56);
        int buttonHeight = Math.Clamp(_leftPanelBounds.Height / 5, 44, 56);
        int buttonWidth = Math.Min(160, _leftPanelBounds.Width - padding * 2);
        int buttonTop = _leftPanelBounds.Bottom - buttonHeight - padding;
        int contentTop = _leftPanelBounds.Top + closeSize + padding;
        int slotSize = Math.Max(24, Math.Min(72, buttonTop - contentTop - padding));

        _closeButtonBounds = new Rectangle(_leftPanelBounds.X, _leftPanelBounds.Y, closeSize, closeSize);
        _weaponSlotBounds = new Rectangle(
            _leftPanelBounds.X + (_leftPanelBounds.Width - slotSize) / 2,
            contentTop + (buttonTop - contentTop - slotSize) / 2,
            slotSize,
            slotSize);
        _fuseButtonBounds = new Rectangle(
            _leftPanelBounds.X + (_leftPanelBounds.Width - buttonWidth) / 2,
            buttonTop,
            buttonWidth,
            buttonHeight);
    }

    private void LayoutWeaponList()
    {
        int padding = Math.Max(6, _rightPanelBounds.Height / 32);
        int scrollSize = Math.Clamp(_rightPanelBounds.Height / 8, 32, 40);
        int listTop = _rightPanelBounds.Top + scrollSize + padding * 2;
        int listBottom = _rightPanelBounds.Bottom - scrollSize - padding * 2;
        int availableHeight = Math.Max(1, listBottom - listTop);
        int itemSpacing = Math.Min(8, Math.Max(4, availableHeight / 24));
        int rowCount = Math.Clamp((availableHeight + itemSpacing) / (PreferredWeaponItemHeight + itemSpacing), 1, MaximumVisibleWeapons);
        int itemHeight = Math.Max(1, Math.Min(PreferredWeaponItemHeight, (availableHeight - itemSpacing * (rowCount - 1)) / rowCount));

        _scrollUpBounds = new Rectangle(
            _rightPanelBounds.X + (_rightPanelBounds.Width - scrollSize) / 2,
            _rightPanelBounds.Top + padding,
            scrollSize,
            scrollSize);
        _scrollDownBounds = new Rectangle(
            _rightPanelBounds.X + (_rightPanelBounds.Width - scrollSize) / 2,
            _rightPanelBounds.Bottom - scrollSize - padding,
            scrollSize,
            scrollSize);
        _weaponListViewport = new Rectangle(
            _rightPanelBounds.X + padding,
            listTop,
            _rightPanelBounds.Width - padding * 2,
            itemHeight * rowCount + itemSpacing * (rowCount - 1));

        _weaponListBounds.Clear();
        for (int index = 0; index < rowCount; index++)
        {
            _weaponListBounds.Add(new Rectangle(
                _weaponListViewport.X,
                listTop + index * (itemHeight + itemSpacing),
                _weaponListViewport.Width,
                itemHeight));
        }
    }

    private void ClampScrollOffset()
    {
        int maximumOffset = Math.Max(0, _availableWeapons.Count - VisibleWeaponCount);
        _scrollOffset = Math.Min(_scrollOffset, maximumOffset);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        InitializeLayout();
    }
}
