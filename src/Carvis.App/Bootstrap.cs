using Carvis.App.Logging;
using Carvis.Core.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace Carvis.App;

/// <summary>What has to exist before Avalonia starts: data folders, settings and the log.</summary>
public sealed class Bootstrap
{
    private Bootstrap(AppPaths paths, SettingsStore store, CarvisSettings settings, IReadOnlyList<string> warnings, SerilogLoggerProvider log)
    {
        Paths = paths;
        SettingsStore = store;
        Settings = settings;
        Warnings = warnings;
        Log = log;
    }

    public AppPaths Paths { get; }
    public SettingsStore SettingsStore { get; }
    public CarvisSettings Settings { get; }
    public IReadOnlyList<string> Warnings { get; }
    public SerilogLoggerProvider Log { get; }

    public static Bootstrap Create()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        var store = new SettingsStore(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), paths.UserSettingsFile);
        var settings = store.Load();

        var warnings = new List<string>();
        if (store.LoadError is { } loadError)
            warnings.Add(loadError);
        warnings.AddRange(SettingsValidator.Validate(settings));
        if (SettingsValidator.PrivacyWarning(settings) is { } privacy)
            warnings.Add(privacy);

        var level = Enum.TryParse<LogLevel>(settings.Logging.Level, ignoreCase: true, out var parsed) ? parsed : LogLevel.Information;
        PrivacyScrubber.AddName(settings.Assistant.UserName);
        var log = CarvisLog.Create(paths.LogsDirectory, level, settings.Logging.RetainDays);

        return new Bootstrap(paths, store, settings, warnings, log);
    }
}
