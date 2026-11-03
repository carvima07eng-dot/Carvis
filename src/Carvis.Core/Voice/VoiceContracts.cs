namespace Carvis.Core.Voice;

public sealed record AudioDevice(string Id, string Name);

/// <summary>Mono 16-bit audio.</summary>
public sealed record AudioClip(short[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

/// <summary>The microphone, delivering 16 kHz mono samples in [-1, 1].</summary>
public interface IAudioInput : IDisposable
{
    public const int SampleRate = 16000;

    bool IsAvailable { get; }
    IReadOnlyList<AudioDevice> Devices();
    bool IsRecording { get; }
    void Start(string? deviceName = null);
    void Stop();

    /// <summary>Raised on an audio thread.</summary>
    event Action<float[]>? SamplesAvailable;
}

public interface IAudioOutput
{
    bool IsAvailable { get; }
    IReadOnlyList<AudioDevice> Devices();
    Task PlayAsync(AudioClip clip, string? deviceName = null, CancellationToken cancellationToken = default);
}

public interface ISpeechToText
{
    /// <summary>The model is on disk (or the engine needs none).</summary>
    bool IsReady { get; }

    /// <summary>Transcribes 16 kHz mono samples. <paramref name="quick"/> uses the small wake-word model.</summary>
    Task<string> TranscribeAsync(float[] samples, bool quick = false, CancellationToken cancellationToken = default);
}

public interface ITextToSpeech
{
    bool IsReady { get; }
    Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class NoAudioInput : IAudioInput
{
    public bool IsAvailable => false;
    public bool IsRecording => false;
    public event Action<float[]>? SamplesAvailable { add { } remove { } }
    public IReadOnlyList<AudioDevice> Devices() => [];
    public void Start(string? deviceName = null) => throw new PlatformNotSupportedException("No hay micrófono disponible.");
    public void Stop()
    {
    }
    public void Dispose()
    {
    }
}

public sealed class NoAudioOutput : IAudioOutput
{
    public bool IsAvailable => false;
    public IReadOnlyList<AudioDevice> Devices() => [];
    public Task PlayAsync(AudioClip clip, string? deviceName = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class NoSpeechToText : ISpeechToText
{
    public bool IsReady => false;
    public Task<string> TranscribeAsync(float[] samples, bool quick = false, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("El reconocimiento de voz no está instalado.");
}
