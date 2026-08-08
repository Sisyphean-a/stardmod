using StardewModdingAPI.Utilities;

namespace HotkeyViewer;

public sealed class ModConfig
{
    public KeybindList OpenMenuKey { get; set; } = KeybindList.Parse("OemQuestion");
}
