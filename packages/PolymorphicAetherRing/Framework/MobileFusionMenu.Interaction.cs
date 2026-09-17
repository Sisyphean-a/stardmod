using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace PolymorphicAetherRing.Framework;

public partial class MobileFusionMenu
{
    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        _hoveringCloseButton = _closeButtonBounds.Contains(x, y);
        _hoveringWeaponSlot = _weaponSlotBounds.Contains(x, y);
        _hoveringFuseButton = _fuseButtonBounds.Contains(x, y);
        _hoveringWeaponIndex = FindWeaponIndexAt(x, y);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (HandleTopLevelClick(x, y) || TrySelectWeapon(x, y))
            return;

        if (_weaponSlotBounds.Contains(x, y) && _slottedWeapon != null)
        {
            ReturnSlottedWeapon();
            return;
        }

        if (_fuseButtonBounds.Contains(x, y) && _slottedWeapon != null)
            PerformFusion();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (direction > 0 && _scrollOffset > 0)
            ScrollWeapons(-1);
        else if (direction < 0 && CanScrollDown)
            ScrollWeapons(1);
    }

    private bool HandleTopLevelClick(int x, int y)
    {
        if (_closeButtonBounds.Contains(x, y))
        {
            exitThisMenu();
            Game1.playSound("bigDeSelect");
            return true;
        }

        if (_scrollUpBounds.Contains(x, y) && _scrollOffset > 0)
        {
            ScrollWeapons(-1);
            return true;
        }

        if (_scrollDownBounds.Contains(x, y) && CanScrollDown)
        {
            ScrollWeapons(1);
            return true;
        }

        return false;
    }

    private int FindWeaponIndexAt(int x, int y)
    {
        for (int index = 0; index < VisibleWeaponCount && index + _scrollOffset < _availableWeapons.Count; index++)
        {
            if (_weaponListBounds[index].Contains(x, y))
                return index;
        }

        return -1;
    }

    private bool TrySelectWeapon(int x, int y)
    {
        int index = FindWeaponIndexAt(x, y);
        if (index < 0)
            return false;

        MeleeWeapon weapon = _availableWeapons[index + _scrollOffset];
        if (_slottedWeapon == weapon)
        {
            Game1.playSound("cancel");
            return true;
        }

        int itemIndex = Game1.player.Items.IndexOf(weapon);
        if (itemIndex >= 0)
            Game1.player.Items[itemIndex] = _slottedWeapon;
        else if (_slottedWeapon != null)
            Game1.player.addItemToInventory(_slottedWeapon);

        _slottedWeapon = weapon;
        CollectAvailableWeapons();
        ClampScrollOffset();
        Game1.playSound("stoneStep");
        _monitor.Log($"Selected weapon: {weapon.DisplayName}", LogLevel.Debug);
        return true;
    }

    private void ScrollWeapons(int offset)
    {
        _scrollOffset += offset;
        ClampScrollOffset();
        Game1.playSound("shwip");
    }

    private void ReturnSlottedWeapon()
    {
        Game1.player.addItemByMenuIfNecessary(_slottedWeapon!);
        _slottedWeapon = null;
        CollectAvailableWeapons();
        ClampScrollOffset();
        Game1.playSound("coin");
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

        CollectAvailableWeapons();
        ClampScrollOffset();
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
            Game1.showGlobalMessage(_helper.Translation.Get("menu.fusion.returned", new { weaponName = weapon.DisplayName }));
        }
        else
        {
            Game1.createItemDebris(remainder, Game1.player.getStandingPosition(), -1);
            Game1.showGlobalMessage(_helper.Translation.Get("menu.fusion.inventory_full", new { weaponName = weapon.DisplayName }));
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

    protected override void cleanupBeforeExit()
    {
        if (_slottedWeapon != null)
            Game1.player.addItemByMenuIfNecessary(_slottedWeapon);

        _slottedWeapon = null;
        base.cleanupBeforeExit();
    }
}
