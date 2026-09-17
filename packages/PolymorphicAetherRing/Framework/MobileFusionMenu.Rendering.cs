using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace PolymorphicAetherRing.Framework;

public partial class MobileFusionMenu
{
    public override void draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);
        Game1.drawDialogueBox(
            xPositionOnScreen - 16,
            yPositionOnScreen - 16,
            width + 32,
            height + 32,
            speaker: false,
            drawOnlyBox: true);

        DrawLeftPanel(spriteBatch);
        DrawRightPanel(spriteBatch);
        drawMouse(spriteBatch);
    }

    private void DrawLeftPanel(SpriteBatch spriteBatch)
    {
        DrawCloseButton(spriteBatch);
        DrawCurrentFusion(spriteBatch);
        DrawWeaponSlot(spriteBatch);
        DrawFuseButton(spriteBatch);
    }

    private void DrawCloseButton(SpriteBatch spriteBatch)
    {
        IClickableMenu.drawTextureBox(
            spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            _closeButtonBounds.X, _closeButtonBounds.Y, _closeButtonBounds.Width, _closeButtonBounds.Height,
            _hoveringCloseButton ? Color.Red : Color.White, 1f, false);

        string label = "X";
        Vector2 labelSize = Game1.dialogueFont.MeasureString(label);
        Vector2 position = new(
            _closeButtonBounds.X + (_closeButtonBounds.Width - labelSize.X) / 2,
            _closeButtonBounds.Y + (_closeButtonBounds.Height - labelSize.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, label, Game1.dialogueFont, position, Game1.textColor);
    }

    private void DrawCurrentFusion(SpriteBatch spriteBatch)
    {
        if (_currentFusion is not { IsValid: true })
            return;

        int textX = _closeButtonBounds.Right + 8;
        int availableWidth = _leftPanelBounds.Right - textX - 8;
        string text = TruncateText(_currentFusion.WeaponName, availableWidth);
        if (string.IsNullOrEmpty(text))
            return;

        Vector2 size = Game1.smallFont.MeasureString(text);
        Vector2 position = new(textX, _leftPanelBounds.Y + (_closeButtonBounds.Height - size.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, text, Game1.smallFont, position, Color.LimeGreen);
    }

    private void DrawWeaponSlot(SpriteBatch spriteBatch)
    {
        IClickableMenu.drawTextureBox(
            spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            _weaponSlotBounds.X, _weaponSlotBounds.Y, _weaponSlotBounds.Width, _weaponSlotBounds.Height,
            _hoveringWeaponSlot ? Color.LightGoldenrodYellow : Color.White, 1f, false);

        if (_slottedWeapon != null)
        {
            float scale = Math.Min(1f, (_weaponSlotBounds.Height - 8) / 64f);
            _slottedWeapon.drawInMenu(spriteBatch, new Vector2(_weaponSlotBounds.X + 4, _weaponSlotBounds.Y + 4), scale);
            return;
        }

        string hint = "+";
        Vector2 hintSize = Game1.dialogueFont.MeasureString(hint);
        Vector2 hintPosition = new(
            _weaponSlotBounds.X + (_weaponSlotBounds.Width - hintSize.X) / 2,
            _weaponSlotBounds.Y + (_weaponSlotBounds.Height - hintSize.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, hint, Game1.dialogueFont, hintPosition, Color.Gray);
    }

    private void DrawFuseButton(SpriteBatch spriteBatch)
    {
        bool canFuse = _slottedWeapon != null && !_hasCorruptFusionData;
        Color color = canFuse ? (_hoveringFuseButton ? Color.LightGreen : Color.White) : Color.Gray;
        IClickableMenu.drawTextureBox(
            spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            _fuseButtonBounds.X, _fuseButtonBounds.Y, _fuseButtonBounds.Width, _fuseButtonBounds.Height,
            color, 1f, true);

        string label = TruncateText(
            _helper.Translation.Get("menu.fusion.fuse_button"),
            Game1.smallFont,
            _fuseButtonBounds.Width - 16);
        Vector2 labelSize = Game1.smallFont.MeasureString(label);
        Vector2 position = new(
            _fuseButtonBounds.X + (_fuseButtonBounds.Width - labelSize.X) / 2,
            _fuseButtonBounds.Y + (_fuseButtonBounds.Height - labelSize.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, label, Game1.smallFont, position, canFuse ? Game1.textColor : Color.DarkGray);
    }

    private void DrawRightPanel(SpriteBatch spriteBatch)
    {
        DrawScrollButton(spriteBatch, _scrollUpBounds, "▲", _scrollOffset > 0);
        DrawScrollButton(spriteBatch, _scrollDownBounds, "▼", CanScrollDown);

        if (_availableWeapons.Count == 0)
            DrawNoWeaponsMessage(spriteBatch);
        else
            DrawWeaponList(spriteBatch);
    }

    private static void DrawScrollButton(SpriteBatch spriteBatch, Rectangle bounds, string label, bool visible)
    {
        if (!visible)
            return;

        IClickableMenu.drawTextureBox(
            spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 1f, false);
        Vector2 labelSize = Game1.smallFont.MeasureString(label);
        Vector2 position = new(
            bounds.X + (bounds.Width - labelSize.X) / 2,
            bounds.Y + (bounds.Height - labelSize.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, label, Game1.smallFont, position, Game1.textColor);
    }

    private void DrawWeaponList(SpriteBatch spriteBatch)
    {
        for (int index = 0; index < VisibleWeaponCount && index + _scrollOffset < _availableWeapons.Count; index++)
        {
            MeleeWeapon weapon = _availableWeapons[index + _scrollOffset];
            Rectangle bounds = _weaponListBounds[index];
            bool isSlotted = _slottedWeapon == weapon;
            Color color = isSlotted ? Color.DarkGray : _hoveringWeaponIndex == index ? Color.LightGoldenrodYellow : Color.White;

            IClickableMenu.drawTextureBox(
                spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                bounds.X, bounds.Y, bounds.Width, bounds.Height, color, 1f, false);

            float iconScale = Math.Min(1f, Math.Max(0.25f, (bounds.Height - 8) / 64f));
            weapon.drawInMenu(spriteBatch, new Vector2(bounds.X + 4, bounds.Y + 4), iconScale);
            DrawWeaponName(spriteBatch, weapon.DisplayName, bounds, iconScale, isSlotted);
        }
    }

    private void DrawWeaponName(SpriteBatch spriteBatch, string name, Rectangle bounds, float iconScale, bool isSlotted)
    {
        int textX = bounds.X + 8 + (int)(64 * iconScale);
        int availableWidth = bounds.Right - textX - 8;
        string text = TruncateText(name, availableWidth);
        Vector2 textSize = Game1.smallFont.MeasureString(text);
        Vector2 position = new(textX, bounds.Y + (bounds.Height - textSize.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, text, Game1.smallFont, position, isSlotted ? Color.Gray : Game1.textColor);
    }

    private void DrawNoWeaponsMessage(SpriteBatch spriteBatch)
    {
        string text = TruncateText(_helper.Translation.Get("menu.fusion.no_weapons"), _weaponListViewport.Width);
        Vector2 size = Game1.smallFont.MeasureString(text);
        Vector2 position = new(
            _weaponListViewport.X + (_weaponListViewport.Width - size.X) / 2,
            _weaponListViewport.Y + (_weaponListViewport.Height - size.Y) / 2);
        Utility.drawTextWithShadow(spriteBatch, text, Game1.smallFont, position, Color.Gray);
    }

    private static string TruncateText(string text, int availableWidth)
    {
        return TruncateText(text, Game1.smallFont, availableWidth);
    }

    private static string TruncateText(string text, SpriteFont font, int availableWidth)
    {
        if (availableWidth <= 0 || string.IsNullOrEmpty(text))
            return string.Empty;

        const string ellipsis = "...";
        if (font.MeasureString(text).X <= availableWidth)
            return text;

        while (text.Length > 0 && font.MeasureString(text + ellipsis).X > availableWidth)
            text = text[..^1];

        return text.Length == 0 ? string.Empty : text + ellipsis;
    }
}
