using System.Runtime.InteropServices;
using StardewModdingAPI;

namespace PortableLoadingOptimizer;

internal sealed class PlatformPolicy
{
    private PlatformPolicy(bool isWindows, bool isAndroid, int prefetchRate, int prefetchLimit)
    {
        IsWindows = isWindows;
        IsAndroid = isAndroid;
        PrefetchMegabytesPerSecond = prefetchRate;
        PrefetchMaximumMegabytes = prefetchLimit;
    }

    internal bool IsWindows { get; }
    internal bool IsAndroid { get; }
    internal string Name => IsWindows ? "Windows" : IsAndroid ? "Android" : RuntimeInformation.OSDescription;
    internal int PrefetchMegabytesPerSecond { get; }
    internal int PrefetchMaximumMegabytes { get; }

    // 规则：ScreenFade 私有细节只按桌面 SMAPI 验证；Android 保持原生淡入淡出。
    internal bool SupportsFastWarp => IsWindows;

    internal static PlatformPolicy Create(ModConfig config)
    {
        bool isWindows = Constants.TargetPlatform == GamePlatform.Windows
            || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isAndroid = Constants.TargetPlatform == GamePlatform.Android
            || OperatingSystem.IsAndroid()
            || RuntimeInformation.OSDescription.Contains("Android", StringComparison.OrdinalIgnoreCase);
        int rate = isAndroid ? config.AndroidPrefetchMegabytesPerSecond : config.PrefetchMegabytesPerSecond;
        int limit = isAndroid ? config.AndroidPrefetchMaximumMegabytes : config.PrefetchMaximumMegabytes;
        return new PlatformPolicy(isWindows, isAndroid, rate, limit);
    }
}
