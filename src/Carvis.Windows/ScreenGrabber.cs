using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Carvis.Windows;

/// <summary>Pixels of part of the screen: 32-bit BGRA, top row first.</summary>
public sealed record RawImage(int Width, int Height, byte[] Bgra);

public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>Screenshots with GDI (BitBlt), in physical pixels.</summary>
[SupportedOSPlatform("windows")]
public static class ScreenGrabber
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static RawImage Capture(ScreenRect rect)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("No he podido capturar la pantalla.");

            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = rect.Width,
                biHeight = -rect.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[rect.Width * rect.Height * 4];
            SelectObject(memory, old);
            if (GetDIBits(memory, bitmap, 0, (uint)rect.Height, pixels, ref info, 0) == 0)
                throw new InvalidOperationException("No he podido leer la captura.");

            // GDI leaves alpha at 0.
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return new RawImage(rect.Width, rect.Height, pixels);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The whole monitor the mouse is on.</summary>
    public static ScreenRect MonitorUnderCursor()
    {
        GetCursorPos(out var point);
        var monitor = MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var r = info.rcMonitor;
        return new ScreenRect(r.Left, r.Top, r.Width, r.Height);
    }

    /// <summary>The visible bounds of a window (without the invisible resize border of Windows 10/11).</summary>
    public static ScreenRect? WindowRect(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
            return null;
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<NativeMethods.RECT>()) != 0 &&
            !NativeMethods.GetWindowRect(window, out r))
            return null;
        return r.Width > 0 && r.Height > 0 ? new ScreenRect(r.Left, r.Top, r.Width, r.Height) : null;
    }

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out NativeMethods.RECT value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);
}
