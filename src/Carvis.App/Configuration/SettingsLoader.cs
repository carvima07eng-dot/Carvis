using Carvis.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Carvis.App.Configuration;

internal static class SettingsLoader
{
    public const string FileName = "appsettings.json";

    /// <summary>Reads appsettings.json next to the executable; missing values fall back to defaults.</summary>
    public static CarvisSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(FileName, optional: true, reloadOnChange: false)
            .Build();

        var settings = new CarvisSettings();
        configuration.Bind(settings);
        return settings;
    }
}
