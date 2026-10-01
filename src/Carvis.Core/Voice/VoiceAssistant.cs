using System.Threading.Channels;
using Carvis.Core.Configuration;
using Carvis.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Voice;

public enum VoiceState
{
    Off,
    Idle,
    WaitingForWakeWord,
    Listening,
    Transcribing,
    Speaking,
}

/// <summary>
/// Microphone → voice activity → Whisper → command, and answer → sentences → Piper → speakers.
/// Listening starts with the push-to-talk key, the mic button or the wake word; speaking can be
/// interrupted with the same key (or by talking, with headphones).
/// </summary>
public sealed class VoiceAssistant : IDisposable
{
    private static readonly TimeSpan NoSpeechTimeout = TimeSpan.FromSeconds(8);

    private readonly IAudioInput _input;
    private readonly IAudioOutput _output;
    private readonly ISpeechToText _speechToText;
    private readonly ITextToSpeech _textToSpeech;
    private readonly ISessionState _session;
    private readonly VoiceSettings _settings;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private VoiceActivityDetector _vad;
    private CancellationTokenSource? _speaking;
    private CancellationTokenSource? _noSpeechTimer;
    private bool _listeningForCommand;
    private VoiceState _state = VoiceState.Off;

    public VoiceAssistant(IAudioInput input, IAudioOutput output, ISpeechToText speechToText, ITextToSpeech textToSpeech,
        ISessionState session, VoiceSettings settings, ILogger<VoiceAssistant>? logger = null)
    {
        _input = input;
        _output = output;
        _speechToText = speechToText;
        _textToSpeech = textToSpeech;
        _session = session;
        _settings = settings;
        _logger = logger ?? NullLogger<VoiceAssistant>.Instance;
        _vad = CreateDetector();
        _input.SamplesAvailable += OnSamples;
    }

    public VoiceState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    public bool CanListen => _input.IsAvailable && _speechToText.IsReady;
    public bool CanSpeak => _output.IsAvailable && _textToSpeech.IsReady;

    /// <summary>Raised from audio threads: marshal to the UI.</summary>
    public event Action<VoiceState>? StateChanged;

    /// <summary>Microphone level from 0 to 1 while listening for a command (audio thread).</summary>
    public event Action<float>? LevelChanged;

    /// <summary>What the user said (already without the wake word).</summary>
    public event Action<string>? CommandHeard;

    /// <summary>Something went wrong; the text is for the user.</summary>
    public event Action<string>? Problem;

    /// <summary>Applies the settings: starts or stops the always-on microphone for the wake word.</summary>
    public void Apply()
    {
        lock (_lock)
        {
            _vad = CreateDetector();
            if (!_settings.Enabled)
            {
                StopMicrophone();
                State = VoiceState.Off;
                return;
            }

            if (_settings.WakeWord && CanListen)
            {
                StartMicrophone();
                State = VoiceState.WaitingForWakeWord;
            }
            else if (!_listeningForCommand)
            {
                StopMicrophone();
                State = VoiceState.Idle;
            }
        }
    }

    /// <summary>The push-to-talk key or the mic button: listen, stop listening, or interrupt speech.</summary>
    public void Toggle()
    {
        if (State == VoiceState.Speaking)
        {
            StopSpeaking();
            return;
        }
        if (_listeningForCommand)
        {
            // Pressing again ends what you're saying now instead of waiting for the pause.
            lock (_lock)
                _vad.Complete();
            if (State == VoiceState.Listening)
                CancelListening();
            return;
        }
        StartListening();
    }

    public void StartListening()
    {
        if (!_settings.Enabled)
        {
            Problem?.Invoke("La voz está desactivada. Actívala en Ajustes → Voz.");
            return;
        }
        if (!CanListen)
        {
            Problem?.Invoke(_input.IsAvailable
                ? "Falta el modelo de reconocimiento de voz. Descárgalo en Ajustes → Voz."
                : "No encuentro ningún micrófono.");
            return;
        }

        StopSpeaking();
        lock (_lock)
        {
            _vad.Reset();
            _listeningForCommand = true;
            StartMicrophone();
            State = VoiceState.Listening;
        }

        // Nobody spoke: give up quietly.
        _noSpeechTimer?.Cancel();
        _noSpeechTimer = new CancellationTokenSource();
        var token = _noSpeechTimer.Token;
        _ = Task.Delay(NoSpeechTimeout, token).ContinueWith(_ =>
        {
            if (!_vad.InSpeech && _listeningForCommand)
                CancelListening();
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public void CancelListening()
    {
        lock (_lock)
        {
            _listeningForCommand = false;
            _vad.Reset();
        }
        _noSpeechTimer?.Cancel();
        ReturnToRest();
    }

    /// <summary>Starts reading an answer aloud; push the text as it streams in.</summary>
    public SpeechSession BeginSpeaking()
    {
        StopSpeaking();
        var cancellation = new CancellationTokenSource();
        _speaking = cancellation;
        var session = new SpeechSession(this, cancellation);
        _ = session.RunAsync();
        return session;
    }

    public void StopSpeaking()
    {
        var speaking = Interlocked.Exchange(ref _speaking, null);
        if (speaking is null)
            return;
        speaking.Cancel();
        ReturnToRest();
    }

    /// <summary>Says a fixed text (tests in the settings, short confirmations).</summary>
    public async Task SayAsync(string text, CancellationToken cancellationToken = default)
    {
        using var session = BeginSpeaking();
        session.Push(text);
        await session.CompleteAsync().WaitAsync(cancellationToken);
    }

    private void OnSamples(float[] samples)
    {
        if (_listeningForCommand && LevelChanged is { } level)
        {
            // Speech is around 0.02–0.2 RMS; the square root makes quiet voices still visible.
            level(Math.Clamp(MathF.Sqrt(VoiceActivityDetector.Rms(samples) * 6), 0, 1));
        }
        lock (_lock)
        {
            if (State == VoiceState.Speaking && !_settings.InterruptByVoice)
                return;
            _vad.Process(samples);
        }
    }

    private VoiceActivityDetector CreateDetector()
    {
        var vad = new VoiceActivityDetector(_settings.SilenceMilliseconds);
        vad.SpeechStarted += () =>
        {
            if (State == VoiceState.Speaking && _settings.InterruptByVoice)
                StopSpeaking();
        };
        vad.UtteranceCompleted += samples => _ = HandleUtteranceAsync(samples);
        return vad;
    }

    private async Task HandleUtteranceAsync(float[] samples)
    {
        var forCommand = _listeningForCommand;
        if (!forCommand)
        {
            // Wake word: only short utterances, never on the lock screen.
            if (!_settings.WakeWord || samples.Length > IAudioInput.SampleRate * 6 || _session.IsLocked)
                return;
        }
        else
        {
            _noSpeechTimer?.Cancel();
            lock (_lock)
                _listeningForCommand = false;
            State = VoiceState.Transcribing;
        }

        try
        {
            var text = SpeechFilters.CleanTranscript(await _speechToText.TranscribeAsync(samples, quick: !forCommand));
            _logger.LogInformation("Heard {Length} characters ({Kind})", text.Length, forCommand ? "command" : "wake word check");

            if (forCommand)
            {
                ReturnToRest();
                if (text.Length > 0)
                    CommandHeard?.Invoke(text);
                return;
            }

            if (SpeechFilters.TryMatchWakeWord(text, out var command))
            {
                if (command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                    CommandHeard?.Invoke(command);
                else
                    StartListening();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transcription failed");
            ReturnToRest();
            if (forCommand)
                Problem?.Invoke($"No he podido entender el audio: {ex.Message}");
        }
    }

    private void ReturnToRest()
    {
        lock (_lock)
        {
            if (_listeningForCommand || _speaking is not null)
                return;
            if (!_settings.Enabled)
            {
                StopMicrophone();
                State = VoiceState.Off;
            }
            else if (_settings.WakeWord && CanListen)
            {
                StartMicrophone();
                State = VoiceState.WaitingForWakeWord;
            }
            else
            {
                StopMicrophone();
                State = VoiceState.Idle;
            }
        }
    }

    private void StartMicrophone()
    {
        if (_input.IsRecording)
            return;
        try
        {
            _input.Start(string.IsNullOrWhiteSpace(_settings.InputDevice) ? null : _settings.InputDevice);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Microphone failed");
            Problem?.Invoke($"No he podido abrir el micrófono: {ex.Message}");
        }
    }

    private void StopMicrophone()
    {
        if (_input.IsRecording)
            _input.Stop();
    }

    public void Dispose()
    {
        _input.SamplesAvailable -= OnSamples;
        StopSpeaking();
        StopMicrophone();
        _noSpeechTimer?.Cancel();
    }

    /// <summary>One answer being read aloud: synthesis of the next sentence overlaps playback of this one.</summary>
    public sealed class SpeechSession : IDisposable
    {
        private readonly VoiceAssistant _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly SpeechChunker _chunker = new();
        private readonly Channel<string> _sentences = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal SpeechSession(VoiceAssistant owner, CancellationTokenSource cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
        }

        public bool IsCancelled => _cancellation.IsCancellationRequested;

        public void Push(string delta)
        {
            foreach (var sentence in _chunker.Push(delta))
                _sentences.Writer.TryWrite(sentence);
        }

        /// <summary>No more text is coming; resolves when everything has been said (or it was cut).</summary>
        public Task CompleteAsync()
        {
            foreach (var sentence in _chunker.Flush())
                _sentences.Writer.TryWrite(sentence);
            _sentences.Writer.TryComplete();
            return _done.Task;
        }

        internal async Task RunAsync()
        {
            var token = _cancellation.Token;
            Task<AudioClip>? next = null;
            try
            {
                var reader = _sentences.Reader;
                while (true)
                {
                    var current = next ?? (await reader.WaitToReadAsync(token) && reader.TryRead(out var first)
                        ? _owner._textToSpeech.SynthesizeAsync(first, token)
                        : null);
                    if (current is null)
                        break;

                    var clip = await current;
                    // Prepare the next sentence while this one plays.
                    next = reader.TryRead(out var following) ? _owner._textToSpeech.SynthesizeAsync(following, token) : null;

                    _owner.State = VoiceState.Speaking;
                    await _owner._output.PlayAsync(clip, string.IsNullOrWhiteSpace(_owner._settings.OutputDevice) ? null : _owner._settings.OutputDevice, token);

                    if (next is null && !await reader.WaitToReadAsync(token))
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _owner._logger.LogWarning(ex, "Speech failed");
                _owner.Problem?.Invoke($"No he podido hablar: {ex.Message}");
            }
            finally
            {
                if (Interlocked.CompareExchange(ref _owner._speaking, null, _cancellation) == _cancellation)
                {
                    _owner.ReturnToRest();
                    // Hands-free: after answering, listen for the follow-up.
                    if (_owner._settings.ContinuousConversation && !token.IsCancellationRequested)
                        _owner.StartListening();
                }
                _done.TrySetResult();
            }
        }

        public void Dispose() => _sentences.Writer.TryComplete();
    }
}
