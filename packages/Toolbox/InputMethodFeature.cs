using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace Toolbox;

/// <summary>
/// Platform gate for the optional input-method integration.
/// Android and other non-Windows platforms intentionally use a no-op implementation.
/// </summary>
internal sealed class InputMethodFeature
{
    private readonly WindowsInputMethodFeature? windowsFeature;

    internal InputMethodFeature(Func<bool> isEnabled, IMonitor monitor)
    {
        if (OperatingSystem.IsWindows())
            windowsFeature = new WindowsInputMethodFeature(isEnabled, monitor);
    }

    internal void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        windowsFeature?.OnUpdateTicked(sender, e);
    }

    internal void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        windowsFeature?.OnReturnedToTitle(sender, e);
    }
}
