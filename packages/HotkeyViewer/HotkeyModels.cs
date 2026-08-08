namespace HotkeyViewer;

internal enum HotkeySource
{
    Game,
    GenericModConfigMenu,
    ConfigGuess
}

internal sealed record HotkeyBinding(string Display, string Normalized);

internal sealed record HotkeyEntry(
    string Action,
    string OwnerName,
    string OwnerId,
    HotkeySource Source,
    IReadOnlyList<HotkeyBinding> Bindings,
    string Detail)
{
    internal bool IsGame => Source == HotkeySource.Game;

    internal string SourceLabel => Source switch
    {
        HotkeySource.Game => "本体",
        HotkeySource.GenericModConfigMenu => "GMCM",
        HotkeySource.ConfigGuess => "推测",
        _ => "未知"
    };

    internal string BindingText => string.Join(", ", Bindings.Select(binding => binding.Display));
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
