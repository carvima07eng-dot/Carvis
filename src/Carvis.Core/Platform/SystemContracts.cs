using System.Runtime.InteropServices;

namespace Carvis.Core.Platform;

public sealed record DiskInfo(string Name, string Label, double TotalGb, double FreeGb);

public sealed record BatteryInfo(int Percent, bool Charging, TimeSpan? Remaining);

public sealed record SystemInfo(
    string OperatingSystem, string? Cpu, int Cores, double? RamTotalGb, double? RamFreeGb,
    string? Gpu, IReadOnlyList<DiskInfo> Disks, BatteryInfo? Battery, TimeSpan Uptime);

public enum MediaKey
{
    PlayPause,
    Next,
    Previous,
    Stop,
}

public enum PowerAction
{
    Lock,
    Sleep,
    Restart,
    Shutdown,
    CancelShutdown,
}

/// <summary>Volume, media, power, brightness, theme and hardware information.</summary>
public interface ISystemControl
{
    int? GetVolume();
    void SetVolume(int percent);
    void SetMute(bool mute);
    bool? IsMuted();
    void SendMediaKey(MediaKey key);
    Task<SystemInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task PowerAsync(PowerAction action, TimeSpan delay, CancellationToken cancellationToken = default);
    Task<int?> GetBrightnessAsync(CancellationToken cancellationToken = default);
    Task SetBrightnessAsync(int percent, CancellationToken cancellationToken = default);
    bool? IsDarkMode();
    void SetDarkMode(bool dark);
    Task<string> NetworkStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reading and writing the clipboard (implemented by the UI).</summary>
public interface IClipboardService
{
    Task<string?> GetTextAsync();
    Task SetTextAsync(string text);
}

/// <summary>Portable fallback: hardware information only; the rest needs the Windows implementation.</summary>
public class DefaultSystemControl : ISystemControl
{
    protected static PlatformNotSupportedException NotHere() => new("Esto solo está disponible en Windows.");

    public virtual int? GetVolume() => null;
    public virtual void SetVolume(int percent) => throw NotHere();
    public virtual void SetMute(bool mute) => throw NotHere();
    public virtual bool? IsMuted() => null;
    public virtual void SendMediaKey(MediaKey key) => throw NotHere();
    public virtual Task PowerAsync(PowerAction action, TimeSpan delay, CancellationToken cancellationToken = default) => throw NotHere();
    public virtual Task<int?> GetBrightnessAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);
    public virtual Task SetBrightnessAsync(int percent, CancellationToken cancellationToken = default) => throw NotHere();
    public virtual bool? IsDarkMode() => null;
    public virtual void SetDarkMode(bool dark) => throw NotHere();
    public virtual Task<string> NetworkStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "Hay conexión de red." : "Sin conexión de red.");

    public virtual Task<SystemInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var disks = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.TotalSize > 1_000_000_000)
            .Select(d => new DiskInfo(d.Name, d.VolumeLabel, d.TotalSize / 1e9, d.AvailableFreeSpace / 1e9))
            .ToList();
        var (total, free) = LinuxMemory();
        return Task.FromResult(new SystemInfo(RuntimeInformation.OSDescription, null, Environment.ProcessorCount, total, free, null, disks, null,
            TimeSpan.FromMilliseconds(Environment.TickCount64)));
    }

    private static (double?, double?) LinuxMemory()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
                return (null, null);
            var values = File.ReadLines("/proc/meminfo")
                .Select(l => l.Split(':'))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => double.TryParse(p[1].Replace("kB", string.Empty).Trim(), out var kb) ? kb / 1024 / 1024 : 0);
            return (values.GetValueOrDefault("MemTotal"), values.GetValueOrDefault("MemAvailable"));
        }
        catch (IOException)
        {
            return (null, null);
        }
    }
}

public sealed record ScriptResult(int ExitCode, string Output, string Error, bool TimedOut);

/// <summary>Runs PowerShell scripts (Windows only).</summary>
public interface IScriptRunner
{
    bool IsAvailable { get; }
    Task<ScriptResult> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class UnavailableScriptRunner : IScriptRunner
{
    public bool IsAvailable => false;

    public Task<ScriptResult> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("PowerShell solo está disponible en Windows.");
}

/// <summary>Fallback clipboard kept in memory (tests, or before the UI starts).</summary>
public sealed class MemoryClipboard : IClipboardService
{
    private string? _text;

    public Task<string?> GetTextAsync() => Task.FromResult(_text);

    public Task SetTextAsync(string text)
    {
        _text = text;
        return Task.CompletedTask;
    }
}
