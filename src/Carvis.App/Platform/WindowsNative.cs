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
