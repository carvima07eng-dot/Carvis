using System.Runtime.Versioning;
using Carvis.Core.Voice;
using NAudio.Wave;

namespace Carvis.Windows;

/// <summary>Microphone through WinMM at 16 kHz mono, ready for Whisper.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioInput : IAudioInput
{
    private WaveInEvent? _wave;

    public bool IsAvailable => WaveInEvent.DeviceCount > 0;
    public bool IsRecording => _wave is not null;
    public event Action<float[]>? SamplesAvailable;

    public IReadOnlyList<AudioDevice> Devices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount)
            .Select(i => new AudioDevice(i.ToString(), WaveInEvent.GetCapabilities(i).ProductName))
            .ToList();

    public void Start(string? deviceName = null)
    {
        if (_wave is not null)
            return;
        var wave = new WaveInEvent
        {
            DeviceNumber = Find(deviceName),
            WaveFormat = new WaveFormat(IAudioInput.SampleRate, 16, 1),
            BufferMilliseconds = 60,
        };
        wave.DataAvailable += (_, e) =>
        {
            var samples = new float[e.BytesRecorded / 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            SamplesAvailable?.Invoke(samples);
        };
        wave.StartRecording();
        _wave = wave;
    }

    public void Stop()
    {
        var wave = Interlocked.Exchange(ref _wave, null);
        if (wave is null)
            return;
        wave.StopRecording();
        wave.Dispose();
    }

    public void Dispose() => Stop();

    private int Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0; // the Windows default (WAVE_MAPPER would be -1; 0 is the preferred device)
        var device = Devices().FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase));
        return device is null ? 0 : int.Parse(device.Id);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAudioOutput : IAudioOutput
{
    public bool IsAvailable => waveOutGetNumDevs() > 0;

    public IReadOnlyList<AudioDevice> Devices() =>
        Enumerable.Range(0, waveOutGetNumDevs())
            .Select(i => waveOutGetDevCaps((IntPtr)i, out var caps, System.Runtime.InteropServices.Marshal.SizeOf<WaveOutCaps>()) == 0
                ? new AudioDevice(i.ToString(), caps.Name)
                : null)
            .OfType<AudioDevice>()
            .ToList();

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct WaveOutCaps
    {
        public short ManufacturerId;
        public short ProductId;
        public int DriverVersion;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        public int Formats;
        public short Channels;
        public short Reserved;
        public int Support;
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern int waveOutGetNumDevs();

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")]
    private static extern int waveOutGetDevCaps(IntPtr deviceId, out WaveOutCaps caps, int size);

    public async Task PlayAsync(AudioClip clip, string? deviceName = null, CancellationToken cancellationToken = default)
    {
        if (clip.Samples.Length == 0)
            return;
        var bytes = new byte[clip.Samples.Length * 2];
        Buffer.BlockCopy(clip.Samples, 0, bytes, 0, bytes.Length);

        using var stream = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(clip.SampleRate, 16, 1));
        using var output = new WaveOutEvent { DeviceNumber = Find(deviceName), DesiredLatency = 120 };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
                finished.TrySetException(e.Exception);
            else
                finished.TrySetResult();
        };
        output.Init(stream);
        output.Play();
        await using (cancellationToken.Register(() => output.Stop()))
            await finished.Task;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private int Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1; // WAVE_MAPPER: whatever Windows uses by default
        var device = Devices().FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase));
        return device is null ? -1 : int.Parse(device.Id);
    }
}

/// <summary>Windows shows LogonUI while the session is locked.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSessionState : Carvis.Core.Platform.ISessionState
{
    public bool IsLocked
    {
        get
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("LogonUI");
            foreach (var process in processes)
                process.Dispose();
            return processes.Length > 0;
        }
    }
}
