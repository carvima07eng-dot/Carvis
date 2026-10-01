namespace Carvis.Core.Voice;

// Phase 4: voice input (Whisper.net), voice output (Piper) and the "Carvis" wake word.

public interface ISpeechToText
{
    /// <summary>Transcribes 16 kHz mono WAV audio.</summary>
    Task<string> TranscribeAsync(Stream wavAudio, CancellationToken cancellationToken = default);
}

public interface ITextToSpeech
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}

public interface IWakeWordDetector : IDisposable
{
    event EventHandler? WakeWordDetected;
    void Start();
    void Stop();
}
