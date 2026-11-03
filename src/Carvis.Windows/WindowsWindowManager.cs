using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Carvis.Core.Platform;
using static Carvis.Windows.NativeMethods;

namespace Carvis.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowManager : DefaultWindowManager
{
    public override IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        var names = new Dictionary<uint, string>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsAppWindow(hWnd))
                return true;

            var length = GetWindowTextLength(hWnd);
            var title = new StringBuilder(length + 1);
            GetWindowText(hWnd, title, title.Capacity);
            GetWindowThreadProcessId(hWnd, out var pid);

            if (!names.TryGetValue(pid, out var processName))
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    processName = process.ProcessName;
                }
                catch (ArgumentException)
                {
                    processName = "?";
                }
                names[pid] = processName;
            }

            windows.Add(new WindowInfo(hWnd, title.ToString(), processName, (int)pid));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public override bool Apply(WindowInfo window, WindowCommand command)
    {
        var hWnd = (IntPtr)window.Handle;
        switch (command)
        {
            case WindowCommand.Activate:
                return Activate(hWnd);
            case WindowCommand.Minimize:
                return ShowWindow(hWnd, SW_MINIMIZE);
            case WindowCommand.Maximize:
                ShowWindow(hWnd, SW_MAXIMIZE);
                return Activate(hWnd);
            case WindowCommand.Restore:
                ShowWindow(hWnd, SW_RESTORE);
                return Activate(hWnd);
            case WindowCommand.SnapLeft or WindowCommand.SnapRight:
                return Snap(hWnd, command == WindowCommand.SnapLeft);
            case WindowCommand.NextMonitor:
                return MoveToNextMonitor(hWnd);
            default:
                return false;
        }
    }

    public override bool Close(WindowInfo window) => PostMessage((IntPtr)window.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    private static bool IsAppWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0 || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
            return false;
        if ((GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0)
            return false;
        // Store apps keep hidden ("cloaked") windows around.
        return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static bool Activate(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);
        // Tapping Alt lifts Windows' foreground lock for SetForegroundWindow.
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return SetForegroundWindow(hWnd);
    }

    private static bool Snap(IntPtr hWnd, bool left)
    {
        var work = WorkArea(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST));
        ShowWindow(hWnd, SW_RESTORE);
        var width = work.Width / 2;
        var ok = SetWindowPos(hWnd, IntPtr.Zero, left ? work.Left : work.Left + width, work.Top, width, work.Height, SWP_NOZORDER);
        Activate(hWnd);
        return ok;
    }

    private static bool MoveToNextMonitor(IntPtr hWnd)
    {
        var monitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            monitors.Add(monitor);
            return true;
        }, IntPtr.Zero);
        if (monitors.Count < 2)
            return false;

        var current = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        var next = monitors[(monitors.IndexOf(current) + 1) % monitors.Count];
        var from = WorkArea(current);
        var to = WorkArea(next);

        var wasMaximized = !IsIconic(hWnd) && GetWindowRect(hWnd, out var rect) && rect.Width >= from.Width && rect.Height >= from.Height;
        ShowWindow(hWnd, SW_RESTORE);
        GetWindowRect(hWnd, out rect);

        // Same relative position, size clamped to the new screen.
        var width = Math.Min(rect.Width, to.Width);
        var height = Math.Min(rect.Height, to.Height);
        var x = to.Left + (int)((rect.Left - from.Left) / (double)Math.Max(1, from.Width) * to.Width);
        var y = to.Top + (int)((rect.Top - from.Top) / (double)Math.Max(1, from.Height) * to.Height);
        x = Math.Clamp(x, to.Left, to.Right - width);
        y = Math.Clamp(y, to.Top, to.Bottom - height);

        var ok = SetWindowPos(hWnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER);
        if (wasMaximized)
            ShowWindow(hWnd, SW_MAXIMIZE);
        Activate(hWnd);
        return ok;
    }

    private static RECT WorkArea(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }
}
