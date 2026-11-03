using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Carvis.App.Services;

/// <summary>
/// New versions from the GitHub releases (Velopack). Only the release list and the update are
/// downloaded; nothing about the user is sent. Does nothing when running from the source code.
/// </summary>
public sealed class UpdateService(ILogger<UpdateService> logger)
{
    public const string RepositoryUrl = "https://github.com/carvima07eng-dot/Carvis";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;
    public string? CurrentVersion => _manager.CurrentVersion?.ToString();
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Checks and downloads in the background. Returns the new version, or null if there is none.</summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
            return null;
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
                return null;
            await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
            _pending = update;
            logger.LogInformation("Update {Version} downloaded", PendingVersion);
            return PendingVersion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No Internet, GitHub down, private repository...: try again another day.
            logger.LogInformation("Update check failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Closes Carvis, installs the downloaded version and opens it again.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is not null)
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
