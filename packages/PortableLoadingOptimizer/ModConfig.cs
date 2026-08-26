namespace PortableLoadingOptimizer;

internal sealed class ModConfig
{
    public bool RemoveSaveSelectionDelay { get; set; } = true;
    public bool EnableBackgroundFilePrefetch { get; set; } = true;
    public int PrefetchStartDelaySeconds { get; set; } = 2;
    public int PrefetchMegabytesPerSecond { get; set; } = 24;
    public int PrefetchMaximumMegabytes { get; set; } = 256;
    public int AndroidPrefetchMegabytesPerSecond { get; set; } = 8;
    public int AndroidPrefetchMaximumMegabytes { get; set; } = 96;
    public bool PrefetchRecentSaveFiles { get; set; } = true;
    public int RecentSaveCount { get; set; } = 2;
    public string[] PrefetchExtensions { get; set; } = { ".tmx", ".tbin", ".png", ".json" };
    public string[] ExcludedDirectoryNames { get; set; } = { ".git", "Backups", "Backup", "cache", "bin", "obj" };
    public bool EnableFastWarpTransitions { get; set; } = true;
    public bool EnableFastWarpTransitionsInMultiplayer { get; set; }
    public double FastWarpTransitionMultiplier { get; set; } = 6.5;

    internal bool Normalize()
    {
        bool changed = false;
        changed |= Clamp(PrefetchStartDelaySeconds, 0, 120, value => PrefetchStartDelaySeconds = value);
        changed |= Clamp(PrefetchMegabytesPerSecond, 1, 512, value => PrefetchMegabytesPerSecond = value);
        changed |= Clamp(PrefetchMaximumMegabytes, 16, 8192, value => PrefetchMaximumMegabytes = value);
        changed |= Clamp(AndroidPrefetchMegabytesPerSecond, 1, 64, value => AndroidPrefetchMegabytesPerSecond = value);
        changed |= Clamp(AndroidPrefetchMaximumMegabytes, 16, 512, value => AndroidPrefetchMaximumMegabytes = value);
        changed |= Clamp(RecentSaveCount, 0, 10, value => RecentSaveCount = value);

        double multiplier = Math.Clamp(FastWarpTransitionMultiplier, 1d, 12d);
        if (multiplier != FastWarpTransitionMultiplier)
        {
            FastWarpTransitionMultiplier = multiplier;
            changed = true;
        }

        if (PrefetchExtensions is null)
        {
            PrefetchExtensions = Array.Empty<string>();
            changed = true;
        }
        if (ExcludedDirectoryNames is null)
        {
            ExcludedDirectoryNames = Array.Empty<string>();
            changed = true;
        }

        return changed;
    }

    private static bool Clamp(int value, int minimum, int maximum, Action<int> setValue)
    {
        int clamped = Math.Clamp(value, minimum, maximum);
        if (value == clamped)
            return false;

        setValue(clamped);
        return true;
    }
}
