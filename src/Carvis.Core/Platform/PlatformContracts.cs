namespace Carvis.Core.Platform;

/// <summary>Opens files, folders and web addresses with the program the user has chosen for them.</summary>
public interface IShell
{
    Task OpenAsync(string target, CancellationToken cancellationToken = default);

    /// <summary>Opens the file explorer with the item selected.</summary>
    Task RevealAsync(string path, CancellationToken cancellationToken = default);
}

public sealed record AppEntry(string Name, string Target, bool IsStoreApp = false);

/// <summary>Programs installed for the user (Start menu shortcuts and Store apps).</summary>
public interface IAppCatalog
{
    Task<IReadOnlyList<AppEntry>> GetAppsAsync(bool refresh = false, CancellationToken cancellationToken = default);
    Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default);
}

public sealed record WindowInfo(long Handle, string Title, string ProcessName, int ProcessId);

public enum WindowCommand
{
    Activate,
    Minimize,
    Maximize,
    Restore,
    SnapLeft,
    SnapRight,
    NextMonitor,
}

/// <summary>Top-level windows of other programs.</summary>
public interface IWindowManager
{
    IReadOnlyList<WindowInfo> ListWindows();
    bool Apply(WindowInfo window, WindowCommand command);

    /// <summary>Asks the program to close (it may show "save changes?").</summary>
    bool Close(WindowInfo window);

    bool Kill(int processId);
}
