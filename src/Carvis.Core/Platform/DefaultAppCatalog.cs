namespace Carvis.Core.Platform;

/// <summary>
/// Start menu shortcuts (.lnk) on Windows and .desktop entries on Linux. The Windows project
/// adds Store apps on top of this.
/// </summary>
public class DefaultAppCatalog(IShell shell) : IAppCatalog
{
    private IReadOnlyList<AppEntry>? _cache;

    public async Task<IReadOnlyList<AppEntry>> GetAppsAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (_cache is null || refresh)
            _cache = await Task.Run(() => LoadApps(cancellationToken), cancellationToken);
        return _cache;
    }

    public virtual Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default) =>
        shell.OpenAsync(app.Target, cancellationToken);

    protected virtual IReadOnlyList<AppEntry> LoadApps(CancellationToken cancellationToken)
    {
        var apps = new List<AppEntry>();
        foreach (var folder in ShortcutFolders())
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in SafeEnumerate(folder, OperatingSystem.IsWindows() ? "*.lnk" : "*.desktop"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = OperatingSystem.IsWindows() ? Path.GetFileNameWithoutExtension(file) : DesktopEntryName(file);
                if (name is not null && !IsUninstaller(name))
                    apps.Add(new AppEntry(name, file));
            }
        }

        return apps.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(a => a.Name).ToList();
    }

    private static IEnumerable<string> ShortcutFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        }
        else
        {
            yield return "/usr/share/applications";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications");
        }
    }

    private static IEnumerable<string> SafeEnumerate(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? DesktopEntryName(string file)
    {
        try
        {
            return File.ReadLines(file).FirstOrDefault(l => l.StartsWith("Name=", StringComparison.Ordinal))?[5..];
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool IsUninstaller(string name) =>
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("desinstalar", StringComparison.OrdinalIgnoreCase);
}
