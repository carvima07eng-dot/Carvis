using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Carvis.Core.Platform;
using Microsoft.Win32;

namespace Carvis.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemControl : DefaultSystemControl
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(15);
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public override int? GetVolume()
    {
        using var endpoint = AudioEndpoint.Open();
        return endpoint?.Volume is { } level ? (int)Math.Round(level * 100) : null;
    }

    public override void SetVolume(int percent)
    {
        using var endpoint = AudioEndpoint.Open() ?? throw new InvalidOperationException("No encuentro ningún dispositivo de sonido.");
        endpoint.Volume = Math.Clamp(percent, 0, 100) / 100f;
    }

    public override bool? IsMuted()
    {
        using var endpoint = AudioEndpoint.Open();
        return endpoint?.Muted;
    }

    public override void SetMute(bool mute)
    {
        using var endpoint = AudioEndpoint.Open() ?? throw new InvalidOperationException("No encuentro ningún dispositivo de sonido.");
        endpoint.Muted = mute;
    }

    public override void SendMediaKey(MediaKey key)
    {
        byte code = key switch
        {
            MediaKey.Next => 0xB0,
            MediaKey.Previous => 0xB1,
            MediaKey.Stop => 0xB2,
            _ => 0xB3,
        };
        NativeMethods.keybd_event(code, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(code, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public override async Task PowerAsync(PowerAction action, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        switch (action)
        {
            case PowerAction.Lock:
                NativeMethods.LockWorkStation();
                break;
            case PowerAction.Sleep:
                NativeMethods.SetSuspendState(false, false, false);
                break;
            case PowerAction.CancelShutdown:
                await ShutdownExeAsync("/a", cancellationToken);
                break;
            default:
                // shutdown.exe shows Windows' own countdown and can be cancelled with /a.
                var seconds = (int)Math.Clamp(delay.TotalSeconds, 0, 315_360_000);
                await ShutdownExeAsync($"{(action == PowerAction.Restart ? "/r" : "/s")} /t {seconds} /c \"Carvis: {(action == PowerAction.Restart ? "reinicio" : "apagado")} programado\"", cancellationToken);
                break;
        }
    }

    private static async Task ShutdownExeAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("No he podido ejecutar shutdown.exe.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 && !arguments.StartsWith("/a", StringComparison.Ordinal))
            throw new InvalidOperationException($"shutdown.exe ha fallado (código {process.ExitCode}).");
    }

    public override async Task<int?> GetBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var result = await PowerShellRunner.RunAsync(
            "(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop | Select-Object -First 1).CurrentBrightness",
            ScriptTimeout, cancellationToken);
        return result.Success && int.TryParse(result.Output.Trim(), out var value) ? value : null;
    }

    public override async Task SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        var result = await PowerShellRunner.RunAsync(
            $"Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop | Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{{Timeout=1; Brightness=[byte]{Math.Clamp(percent, 0, 100)}}} | Out-Null",
            ScriptTimeout, cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException("No he podido cambiar el brillo de esta pantalla.");
    }

    public override bool? IsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int light ? light == 0 : null;
    }

    public override void SetDarkMode(bool dark)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
        key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        // Tells open programs (Explorer, taskbar) to repaint with the new theme.
        NativeMethods.SendMessageTimeout(new IntPtr(0xFFFF), NativeMethods.WM_SETTINGCHANGE, UIntPtr.Zero, "ImmersiveColorSet", 2, 1000, out _);
    }

    public override async Task<string> NetworkStatusAsync(CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder(await base.NetworkStatusAsync(cancellationToken)).Append('\n');
        var wifi = await PowerShellRunner.RunAsync("netsh wlan show interfaces", ScriptTimeout, cancellationToken);
        var ssid = Field(wifi.Output, "SSID");
        var signal = Field(wifi.Output, "Señal") ?? Field(wifi.Output, "Signal");
        text.Append(ssid is null ? "Wi-Fi: no conectado (o no hay adaptador Wi-Fi)\n" : $"Wi-Fi: conectado a «{ssid}»{(signal is null ? string.Empty : $", señal {signal}")}\n");

        var bluetooth = await PowerShellRunner.RunAsync(
            "$r = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -match 'Radio|Adapter|Bluetooth' } | Select-Object -First 1; if ($r) { $r.Status } else { 'none' }",
            ScriptTimeout, cancellationToken);
        text.Append(bluetooth.Output.Trim() switch
        {
            "OK" => "Bluetooth: activado",
            "none" or "" => "Bluetooth: no hay adaptador",
            _ => "Bluetooth: desactivado",
        });
        return text.ToString();
    }

    private static string? Field(string output, string name)
    {
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return parts[1].Trim() is { Length: > 0 } value ? value : null;
        }
        return null;
    }

    public override async Task<SystemInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var basic = await base.GetInfoAsync(cancellationToken);

        string? cpu;
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim();

        double? total = null, free = null;
        var memory = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            total = memory.ullTotalPhys / 1073741824.0;
            free = memory.ullAvailPhys / 1073741824.0;
        }

        BatteryInfo? battery = null;
        if (NativeMethods.GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryLifePercent <= 100)
        {
            battery = new BatteryInfo(power.BatteryLifePercent, power.ACLineStatus == 1,
                power.BatteryLifeTime is > 0 and < 0xFFFFFFFF ? TimeSpan.FromSeconds(power.BatteryLifeTime) : null);
        }

        return basic with
        {
            OperatingSystem = WindowsVersion(),
            Cpu = cpu,
            RamTotalGb = total,
            RamFreeGb = free,
            Gpu = await GpuAsync(cancellationToken),
            Battery = battery,
        };
    }

    private static string WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = Environment.OSVersion.Version.Build;
        var name = build >= 22000 ? "Windows 11" : "Windows 10";
        return key?.GetValue("DisplayVersion") is string display ? $"{name} {display} (compilación {build})" : $"{name} (compilación {build})";
    }

    private static async Task<string?> GpuAsync(CancellationToken cancellationToken)
    {
        // nvidia-smi gives memory use too; other cards are described by WMI.
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nvidia-smi",
                "--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu --format=csv,noheader,nounits")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is not null)
            {
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var parts = output.Split('\n')[0].Split(',').Select(p => p.Trim()).ToArray();
                if (process.ExitCode == 0 && parts.Length >= 5)
                    return string.Create(CultureInfo.InvariantCulture, $"{parts[0]} — VRAM {parts[1]} / {parts[2]} MB en uso, carga {parts[3]}%, {parts[4]} °C");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        var wmi = await PowerShellRunner.RunAsync("(Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -join ', '", ScriptTimeout, cancellationToken);
        return wmi.Success && wmi.Output.Length > 0 ? wmi.Output.Trim() : null;
    }

    /// <summary>The default playback device's master volume (Core Audio).</summary>
    private sealed class AudioEndpoint : IDisposable
    {
        private readonly IAudioEndpointVolume _volume;
        private Guid _context = Guid.Empty;

        private AudioEndpoint(IAudioEndpointVolume volume) => _volume = volume;

        public static AudioEndpoint? Open()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device) != 0 || device is null)
                    return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var result) != 0)
                    return null;
                Marshal.ReleaseComObject(device);
                return new AudioEndpoint((IAudioEndpointVolume)result);
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        public float? Volume
        {
            get => _volume.GetMasterVolumeLevelScalar(out var level) == 0 ? level : null;
            set => Marshal.ThrowExceptionForHR(_volume.SetMasterVolumeLevelScalar(value ?? 0, ref _context));
        }

        public bool? Muted
        {
            get => _volume.GetMute(out var muted) == 0 ? muted : null;
            set => Marshal.ThrowExceptionForHR(_volume.SetMute(value ?? false, ref _context));
        }

        public void Dispose() => Marshal.ReleaseComObject(_volume);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
