using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Carvis.App.Platform;

internal static class WindowsNative
{
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_SYSMENU = 0x00080000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    /// <summary>Settings → Accessibility → Visual effects → Animation effects.</summary>
    public static bool AnimationsEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return true;
        return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0) || enabled != 0;
    }

    /// <summary>The Windows notification sound (reminders).</summary>
    public static void PlayNotificationSound()
    {
        if (OperatingSystem.IsWindows())
            MessageBeep(0x40 /* MB_ICONASTERISK */);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>Mouse position in screen pixels, or null where it can't be read.</summary>
    public static Avalonia.PixelPoint? GetCursorPosition()
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var point))
            return new Avalonia.PixelPoint(point.X, point.Y);
        return null;
    }

    /// <summary>Activate() alone can leave the window behind others when the app is in the background.</summary>
    public static void BringToFront(Window window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle)
            SetForegroundWindow(handle.Handle);
    }

    /// <summary>
    /// A borderless window loses the system menu, so clicking its taskbar button doesn't
    /// minimize or restore it. Adding the styles back fixes that without drawing a title bar.
    /// </summary>
    public static void KeepTaskbarBehaviour(Window window)
    {
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWindowStylesCallback(window, (style, exStyle) => (style | WS_SYSMENU | WS_MINIMIZEBOX, exStyle));
    }
}
