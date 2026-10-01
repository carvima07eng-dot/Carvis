namespace Carvis.Core.Voice;

/// <summary>
/// Finds where speech starts and ends in the microphone stream by energy, with a noise floor
/// that adapts to the room. Not as clever as a neural VAD, but needs nothing and costs nothing.
/// </summary>
public sealed class VoiceActivityDetector
{
    private const int FrameSize = IAudioInput.SampleRate * 30 / 1000; // 30 ms
    private const int StartFrames = 3;                                 // 90 ms of voice to start
    private const int PreRollFrames = 10;                              // keep 300 ms before the start
    private const float MinimumLevel = 0.012f;
    private const float NoiseRatio = 3f;

    private readonly int _silenceFrames;
    private readonly int _maxFrames;
    private readonly Queue<float[]> _preRoll = new();
    private readonly List<float> _utterance = [];
    private readonly List<float> _pending = [];
    private float _noiseFloor = 0.004f;
    private int _voiceRun;
    private int _silenceRun;
    private int _frames;

    public VoiceActivityDetector(int silenceMilliseconds = 900, int maxSeconds = 30)
    {
        _silenceFrames = Math.Max(5, silenceMilliseconds / 30);
        _maxFrames = maxSeconds * 1000 / 30;
    }

    public bool InSpeech { get; private set; }

    public event Action? SpeechStarted;

    /// <summary>A complete utterance, from a little before the voice started to the pause.</summary>
    public event Action<float[]>? UtteranceCompleted;

    public void Reset()
    {
        _preRoll.Clear();
        _utterance.Clear();
        _pending.Clear();
        InSpeech = false;
        _voiceRun = _silenceRun = _frames = 0;
    }

    public void Process(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _pending.Add(sample);
            if (_pending.Count == FrameSize)
            {
                ProcessFrame([.. _pending]);
                _pending.Clear();
            }
        }
    }

    private void ProcessFrame(float[] frame)
    {
        var level = Rms(frame);
        var voiced = level > Math.Max(MinimumLevel, _noiseFloor * NoiseRatio);

        if (!InSpeech)
        {
            // The floor follows the quiet frames, slowly, so the room noise doesn't count as speech.
            if (!voiced)
                _noiseFloor = _noiseFloor * 0.95f + level * 0.05f;

            _preRoll.Enqueue(frame);
            if (_preRoll.Count > PreRollFrames)
                _preRoll.Dequeue();

            _voiceRun = voiced ? _voiceRun + 1 : 0;
            if (_voiceRun < StartFrames)
                return;

            InSpeech = true;
            _silenceRun = 0;
            _frames = 0;
            foreach (var previous in _preRoll)
                _utterance.AddRange(previous);
            _preRoll.Clear();
            SpeechStarted?.Invoke();
            return;
        }

        _utterance.AddRange(frame);
        _frames++;
        _silenceRun = voiced ? 0 : _silenceRun + 1;
        if (_silenceRun >= _silenceFrames || _frames >= _maxFrames)
            Complete();
    }

    /// <summary>Ends the utterance now (e.g. the user pressed the key again).</summary>
    public void Complete()
    {
        if (!InSpeech)
            return;
        var samples = _utterance.ToArray();
        _utterance.Clear();
        InSpeech = false;
        _voiceRun = 0;
        UtteranceCompleted?.Invoke(samples);
    }

    public static float Rms(ReadOnlySpan<float> frame)
    {
        double sum = 0;
        foreach (var s in frame)
            sum += s * s;
        return frame.Length == 0 ? 0 : (float)Math.Sqrt(sum / frame.Length);
    }
}
