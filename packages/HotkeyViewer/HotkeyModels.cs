namespace HotkeyViewer;

internal enum HotkeySource
{
    Game,
    GenericModConfigMenu,
    ConfigGuess
}

internal sealed record HotkeyBinding(string Display, string Normalized)
{
    internal string CompactDisplay { get; } = string.Join(
        "+",
        Display.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CompactButtonName));

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
}

internal sealed record HotkeyEntry(
    string Action,
    string OwnerName,
    string OwnerId,
    HotkeySource Source,
    IReadOnlyList<HotkeyBinding> Bindings,
    string Detail)
{
    internal bool IsGame => Source == HotkeySource.Game;

    internal string SourceLabel { get; } = Source switch
    {
        HotkeySource.Game => "本体",
        HotkeySource.GenericModConfigMenu => "GMCM",
        HotkeySource.ConfigGuess => "推测",
        _ => "未知"
    };

    internal string OwnerDisplay { get; } = Source == HotkeySource.Game ? "原版设置" : OwnerName;
    internal string BindingText { get; } = string.Join(", ", Bindings.Select(binding => binding.Display));
    internal string SearchText { get; } = string.Join("\n", BindingText, Action, OwnerName, OwnerId, Detail);
}

internal sealed record HotkeyCatalogResult(
    IReadOnlyList<HotkeyEntry> Entries,
    IReadOnlyDictionary<string, int> BindingUseCounts,
    IReadOnlyList<string> Warnings)
{
    internal bool IsConflict(HotkeyEntry entry)
    {
        return entry.Bindings.Any(binding => BindingUseCounts.TryGetValue(binding.Normalized, out int count) && count > 1);
    }
}
