using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Carvis.App.Platform;

internal static class WindowsNative
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Activate() alone can leave the window behind others when the app is in the background.</summary>
    public static void BringToFront(Window window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle)
            SetForegroundWindow(handle.Handle);
    }
}
