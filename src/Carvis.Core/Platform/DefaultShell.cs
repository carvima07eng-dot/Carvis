using System.Diagnostics;

namespace Carvis.Core.Platform;

public sealed class DefaultShell : IShell
{
    public Task OpenAsync(string target, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        else
            Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", Quote(target)) { UseShellExecute = false })?.Dispose();
        return Task.CompletedTask;
    }

    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
            return Task.CompletedTask;
        }
        return OpenAsync(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path, cancellationToken);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
