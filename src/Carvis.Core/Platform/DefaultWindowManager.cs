using System.Diagnostics;

namespace Carvis.Core.Platform;

/// <summary>Portable fallback: lists windows through processes; moving them needs the Windows implementation.</summary>
public class DefaultWindowManager : IWindowManager
{
    public virtual IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                        windows.Add(new WindowInfo(process.MainWindowHandle, process.MainWindowTitle, process.ProcessName, process.Id));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        return windows;
    }

    public virtual bool Apply(WindowInfo window, WindowCommand command) => false;

    public virtual bool Close(WindowInfo window)
    {
        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            return process.CloseMainWindow();
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
