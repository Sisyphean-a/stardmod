using System;
using System.Runtime.InteropServices;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Toolbox;

internal sealed class InputMethodFeature
{
    private const uint NotifyCloseCandidate = 0x0011;
    private const uint NotifyCompositionString = 0x0015;
    private const uint CancelComposition = 0x0004;

    private readonly Func<bool> isEnabled;
    private IntPtr gameWindow;
    private IntPtr savedInputContext;
    private bool isInputMethodBlocked;

    public InputMethodFeature(Func<bool> isEnabled)
    {
        this.isEnabled = isEnabled;
    }

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || !Context.IsWorldReady || !isEnabled() || IsTextInputActive())
        {
            RestoreInputMethod();
            return;
        }

        BlockInputMethod();
    }

    public void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        RestoreInputMethod();
    }

    private static bool IsTextInputActive()
    {
        return Game1.keyboardDispatcher.Subscriber is not null || Game1.textEntry is not null;
    }

    private void BlockInputMethod()
    {
        IntPtr window = GetGameWindowHandle();
        if (window == IntPtr.Zero)
            return;

        if (isInputMethodBlocked)
        {
            if (gameWindow == window)
                return;

            RestoreInputMethod();
        }

        CancelTextComposition(window);
        savedInputContext = ImmAssociateContext(window, IntPtr.Zero);
        gameWindow = window;
        isInputMethodBlocked = true;
    }

    private void RestoreInputMethod()
    {
        if (!isInputMethodBlocked)
            return;

        ImmAssociateContext(gameWindow, savedInputContext);
        gameWindow = IntPtr.Zero;
        savedInputContext = IntPtr.Zero;
        isInputMethodBlocked = false;
    }

    private static void CancelTextComposition(IntPtr window)
    {
        IntPtr inputContext = ImmGetContext(window);
        if (inputContext == IntPtr.Zero)
            return;

        try
        {
            ImmNotifyIME(inputContext, NotifyCompositionString, CancelComposition, 0);
            ImmNotifyIME(inputContext, NotifyCloseCandidate, 0, 0);
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
