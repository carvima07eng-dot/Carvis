using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Carvis.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Carvis.Windows;

/// <summary>
/// Every app in the Start menu, Store apps included, through Get-StartApps. Both kinds start
/// with "explorer shell:AppsFolder\{AppID}". Falls back to scanning .lnk shortcuts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppCatalog(IShell shell, ILogger<WindowsAppCatalog> logger) : DefaultAppCatalog(shell)
{
    protected override IReadOnlyList<AppEntry> LoadApps(CancellationToken cancellationToken)
    {
        var shortcuts = base.LoadApps(cancellationToken);
        try
        {
            var result = PowerShellRunner.RunAsync("Get-StartApps | Select-Object Name, AppID | ConvertTo-Json -Compress",
                TimeSpan.FromSeconds(20), cancellationToken).GetAwaiter().GetResult();
            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                return shortcuts;

            using var json = JsonDocument.Parse(result.Output);
            var items = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToList() : [json.RootElement];
            var startApps = items
                .Select(e => (Name: e.GetProperty("Name").GetString(), Id: e.GetProperty("AppID").GetString()))
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Id))
                .Where(a => !a.Name!.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !a.Name!.Contains("desinstalar", StringComparison.OrdinalIgnoreCase))
                .Select(a => new AppEntry(a.Name!, a.Id!, IsStoreApp: true))
                .ToList();

            var names = startApps.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return startApps.Concat(shortcuts.Where(s => !names.Contains(s.Name))).OrderBy(a => a.Name).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Get-StartApps failed, using shortcuts only");
            return shortcuts;
        }
    }

    public override Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        if (!app.IsStoreApp)
            return base.LaunchAsync(app, cancellationToken);

        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.Target}") { UseShellExecute = false })?.Dispose();
        return Task.CompletedTask;
    }
}
