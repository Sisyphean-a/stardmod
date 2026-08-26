using System;
using System.Runtime.InteropServices;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Toolbox;

internal sealed class WindowsInputMethodFeature
{
    private const uint NotifyCloseCandidate = 0x0011;
    private const uint NotifyCompositionString = 0x0015;
    private const uint CancelComposition = 0x0004;

    private readonly Func<bool> isEnabled;
    private readonly IMonitor monitor;
    private IntPtr gameWindow;
    private IntPtr sdlWindow;
    private IntPtr savedInputContext;
    private bool isInputMethodBlocked;
    private bool hasLoggedInputMethodReassociation;
    private bool hasLoggedInputMethodError;

    internal WindowsInputMethodFeature(Func<bool> isEnabled, IMonitor monitor)
    {
        this.isEnabled = isEnabled;
        this.monitor = monitor;
    }

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        try
        {
            if (!Context.IsWorldReady || !isEnabled() || IsTextInputActive())
            {
                RestoreInputMethod();
                hasLoggedInputMethodError = false;
                return;
            }

            BlockInputMethod();
            hasLoggedInputMethodError = false;
        }
        catch (Exception ex)
        {
            if (!hasLoggedInputMethodError)
            {
                monitor.Log($"输入法控制发生 Windows 原生错误，已暂时跳过本次处理：{ex.GetBaseException().Message}", LogLevel.Error);
                hasLoggedInputMethodError = true;
            }

            try
            {
                RestoreInputMethod();
            }
            catch (Exception restoreException)
            {
                if (!hasLoggedInputMethodError)
                {
                    monitor.Log($"恢复输入法上下文失败：{restoreException.GetBaseException().Message}", LogLevel.Error);
                    hasLoggedInputMethodError = true;
                }
            }
        }
    }

    public void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        try
        {
            RestoreInputMethod();
        }
        catch (Exception ex)
        {
            monitor.Log($"恢复输入法上下文失败：{ex.GetBaseException().Message}", LogLevel.Error);
        }
    }

    private static bool IsTextInputActive()
    {
        return Game1.keyboardDispatcher.Subscriber is not null || Game1.textEntry is not null;
    }

    private void BlockInputMethod()
    {
        if (isInputMethodBlocked)
        {
            // SDL's native window lookup is stable while the SDL window handle is unchanged.
            IntPtr currentSdlWindow = Game1.game1.Window.Handle;
            if (currentSdlWindow == sdlWindow)
            {
                // ImmAssociateContext returns the context that was active before this call.
                // A zero result means the context is already blocked, so avoid the much more
                // expensive composition query on every game tick.
                IntPtr reassociatedContext = ImmAssociateContext(gameWindow, IntPtr.Zero);
                if (reassociatedContext == IntPtr.Zero)
                    return;

                // Restore the context briefly so composition/candidate state can be cancelled
                // before blocking it again. This path runs only after another component reattached IME.
                ImmAssociateContext(gameWindow, reassociatedContext);
                CancelTextComposition(gameWindow);
                ImmAssociateContext(gameWindow, IntPtr.Zero);
                if (!hasLoggedInputMethodReassociation)
                {
                    monitor.Log("检测到输入法上下文被重新关联，已再次屏蔽。", LogLevel.Debug);
                    hasLoggedInputMethodReassociation = true;
                }

                return;
            }

            RestoreInputMethod();
        }

        IntPtr window = GetGameWindowHandle();
        if (window == IntPtr.Zero)
            return;

        CancelTextComposition(window);
        savedInputContext = ImmAssociateContext(window, IntPtr.Zero);
        gameWindow = window;
        sdlWindow = Game1.game1.Window.Handle;
        isInputMethodBlocked = true;
        hasLoggedInputMethodReassociation = false;
    }

    private void RestoreInputMethod()
    {
        if (!isInputMethodBlocked)
            return;

        ImmAssociateContext(gameWindow, savedInputContext);
        gameWindow = IntPtr.Zero;
        sdlWindow = IntPtr.Zero;
        savedInputContext = IntPtr.Zero;
        isInputMethodBlocked = false;
        hasLoggedInputMethodReassociation = false;
    }

    private static bool CancelTextComposition(IntPtr window)
    {
        IntPtr inputContext = ImmGetContext(window);
        if (inputContext == IntPtr.Zero)
            return false;

        try
        {
            ImmNotifyIME(inputContext, NotifyCompositionString, CancelComposition, 0);
            ImmNotifyIME(inputContext, NotifyCloseCandidate, 0, 0);
            return true;
        }
        finally
        {
            ImmReleaseContext(window, inputContext);
        }
    }

    private static IntPtr GetGameWindowHandle()
    {
        IntPtr sdlWindow = Game1.game1.Window.Handle;
        if (sdlWindow == IntPtr.Zero)
            return IntPtr.Zero;

        SdlVersion version = default;
        SdlGetVersion(out version);
        SdlSysWmInfo windowInfo = SdlSysWmInfo.Create(version);
        if (SdlGetWindowWmInfo(sdlWindow, ref windowInfo) == 0)
            throw new InvalidOperationException($"SDL_GetWindowWMInfo 失败：{GetSdlError()}");
        if (windowInfo.Subsystem != SdlWindowsSubsystem || windowInfo.Window == IntPtr.Zero)
            throw new InvalidOperationException("SDL 未返回有效的 Windows 游戏窗口句柄。");

        return windowInfo.Window;
    }

    private static string GetSdlError()
    {
        IntPtr error = SdlGetError();
        return error == IntPtr.Zero
            ? "SDL 未提供错误信息。"
            : Marshal.PtrToStringUTF8(error) ?? "SDL 未提供错误信息。";
    }

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetVersion")]
    private static extern void SdlGetVersion(out SdlVersion version);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetWindowWMInfo")]
    private static extern int SdlGetWindowWmInfo(IntPtr window, ref SdlSysWmInfo info);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetError")]
    private static extern IntPtr SdlGetError();

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmNotifyIME(IntPtr hIMC, uint dwAction, uint dwIndex, uint dwValue);

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlVersion
    {
        public byte Major;
        public byte Minor;
        public byte Patch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlSysWmInfo
    {
        public SdlVersion Version;
        public int Subsystem;
        public IntPtr Window;
        public IntPtr Hdc;
        public IntPtr Hinstance;

        public static SdlSysWmInfo Create(SdlVersion version)
        {
            return new SdlSysWmInfo { Version = version };
        }
    }

    private const int SdlWindowsSubsystem = 1;
}
