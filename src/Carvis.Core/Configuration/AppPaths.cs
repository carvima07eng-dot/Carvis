namespace Carvis.Core.Configuration;

/// <summary>Where Carvis keeps its data. Nothing is written next to the executable.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null, string? settingsRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(KnownFolder(Environment.SpecialFolder.LocalApplicationData), "Carvis");
        SettingsRoot = settingsRoot ?? Path.Combine(KnownFolder(Environment.SpecialFolder.ApplicationData), "Carvis");
    }

    // Without DoNotVerify the path is empty when the folder doesn't exist yet (fresh Linux profiles).
    private static string KnownFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".carvis")
            : path;
    }

    /// <summary>%LocalAppData%\Carvis: database, logs, models.</summary>
    public string DataRoot { get; }

    /// <summary>%AppData%\Carvis: user settings, survives updates.</summary>
    public string SettingsRoot { get; }

    public string UserSettingsFile => Path.Combine(SettingsRoot, "settings.json");
    public string WindowStateFile => Path.Combine(SettingsRoot, "window.json");
    public string LogsDirectory => Path.Combine(DataRoot, "logs");
    public string DatabaseFile => Path.Combine(DataRoot, "carvis.db");
    public string ModelsDirectory => Path.Combine(DataRoot, "models");
    public string TrashDirectory => Path.Combine(DataRoot, "trash");
    public string PluginsDirectory => Path.Combine(SettingsRoot, "plugins");
    public string TempDirectory => Path.Combine(DataRoot, "temp");

    public void EnsureCreated()
    {
        foreach (var directory in new[] { DataRoot, SettingsRoot, LogsDirectory, ModelsDirectory, TempDirectory })
            Directory.CreateDirectory(directory);
    }
}
