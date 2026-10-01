using Carvis.Core.Configuration;
using Carvis.Core.Platform;
using Carvis.Core.Voice;

namespace Carvis.Tests.Voice;

public sealed class VoiceTests
{
    [Fact]
    public void Chunker_EmitsSentencesAsTheyComplete()
    {
        var chunker = new SpeechChunker();
        Assert.Empty(chunker.Push("Hola, soy **Carvis**"));
        Assert.Equal(["Hola, soy Carvis."], chunker.Push(". Ya he creado la car"));
        Assert.Empty(chunker.Push("peta"));
        Assert.Equal(["Ya he creado la carpeta"], chunker.Flush());
    }

    [Fact]
    public void Chunker_SkipsCodeAndCleansMarkdown()
    {
        var chunker = new SpeechChunker();
        var said = chunker.Push("Aquí tienes:\n```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n- Mira [la documentación](https://learn.microsoft.com) y el 50% de https://x.com/y\n")
            .Concat(chunker.Flush()).ToList();

        Assert.Contains(said, s => s.Contains("Te dejo el código en pantalla"));
        Assert.DoesNotContain(said, s => s.Contains("Console") || s.Contains("var x"));
        Assert.Contains("Mira la documentación y el 50 por ciento de el enlace", said);
    }

    [Fact]
    public void Chunker_DoesNotSplitDecimals()
    {
        var chunker = new SpeechChunker();
        var said = chunker.Push("El resultado es 3.14 aproximadamente. ").Concat(chunker.Flush()).ToList();
        Assert.Equal(["El resultado es 3.14 aproximadamente."], said);
    }

    [Theory]
    [InlineData("Subtítulos realizados por la comunidad de Amara.org", "")]
    [InlineData(" ¡Gracias por ver el video! ", "")]
    [InlineData("Abre Spotify.", "Abre Spotify.")]
    public void Transcripts_LoseWhisperHallucinations(string text, string expected) =>
        Assert.Equal(expected, SpeechFilters.CleanTranscript(text));

    [Theory]
    [InlineData("Carvis, abre Spotify.", true, "abre Spotify")]
    [InlineData("Oye Jarvis, ¿qué hora es?", true, "¿qué hora es")]
    [InlineData("Carbis", true, "")]
    [InlineData("Carlos, ven a cenar", false, "")]
    [InlineData("abre el navegador", false, "")]
    public void WakeWord_IsFoundAtTheStart(string transcript, bool found, string command)
    {
        Assert.Equal(found, SpeechFilters.TryMatchWakeWord(transcript, out var rest));
        Assert.Equal(command, rest);
    }

    [Fact]
    public void Vad_FindsOneUtteranceBetweenSilences()
    {
        var vad = new VoiceActivityDetector(silenceMilliseconds: 600);
        var utterances = new List<float[]>();
        var started = 0;
        vad.SpeechStarted += () => started++;
        vad.UtteranceCompleted += utterances.Add;

        vad.Process(Noise(1.0, 0.002f));
        vad.Process(Tone(1.5, 0.2f));
        vad.Process(Noise(0.2, 0.002f)); // a short pause inside the sentence
        vad.Process(Tone(0.5, 0.2f));
        vad.Process(Noise(1.0, 0.002f));

        Assert.Equal(1, started);
        var utterance = Assert.Single(utterances);
        Assert.InRange(utterance.Length / 16000.0, 2.2, 3.2);
    }

    [Fact]
    public void Vad_IgnoresSteadyBackgroundNoise()
    {
        var vad = new VoiceActivityDetector();
        var count = 0;
        vad.UtteranceCompleted += _ => count++;
        vad.Process(Noise(5, 0.006f));
        Assert.Equal(0, count);
        Assert.False(vad.InSpeech);
    }

    [Fact]
    public async Task Assistant_ListensTranscribesAndReportsTheCommand()
    {
        var input = new FakeInput();
        var stt = new FakeStt("Abre Spotify");
        using var assistant = new VoiceAssistant(input, new FakeOutput(), stt, new FakeTts(), new UnlockedSession(),
            new VoiceSettings { Enabled = true, SilenceMilliseconds = 300 });
        var heard = new TaskCompletionSource<string>();
        assistant.CommandHeard += text => heard.TrySetResult(text);
        assistant.Apply();

        assistant.Toggle();
        Assert.Equal(VoiceState.Listening, assistant.State);
        Assert.True(input.IsRecording);

        input.Feed(Noise(0.3, 0.002f));
        input.Feed(Tone(1, 0.3f));
        input.Feed(Noise(0.6, 0.002f));

        Assert.Equal("Abre Spotify", await heard.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(stt.LastQuick);
        await WaitUntil(() => assistant.State == VoiceState.Idle);
        Assert.False(input.IsRecording);
    }

    [Fact]
    public async Task Assistant_WakeWordStartsACommandOnlyWhenUnlocked()
    {
        var input = new FakeInput();
        var session = new FakeSession();
        using var assistant = new VoiceAssistant(input, new FakeOutput(), new FakeStt("Carvis, pon música"), new FakeTts(), session,
            new VoiceSettings { Enabled = true, WakeWord = true, SilenceMilliseconds = 300 });
        var heard = new List<string>();
        assistant.CommandHeard += heard.Add;
        assistant.Apply();
        Assert.Equal(VoiceState.WaitingForWakeWord, assistant.State);

        session.Locked = true;
        Speak(input);
        await Task.Delay(200);
        Assert.Empty(heard);

        session.Locked = false;
        Speak(input);
        await WaitUntil(() => heard.Count == 1);
        Assert.Equal("pon música", heard[0]);
    }

    [Fact]
    public async Task Assistant_SpeaksSentencesInOrderAndCanBeInterrupted()
    {
        var output = new FakeOutput();
        using var assistant = new VoiceAssistant(new FakeInput(), output, new FakeStt(""), new FakeTts(), new UnlockedSession(),
            new VoiceSettings { Enabled = true });
        assistant.Apply();

        var session = assistant.BeginSpeaking();
        session.Push("Primera frase completa. Segunda fra");
        session.Push("se también. Tercera");
        await session.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["Primera frase completa.", "Segunda frase también.", "Tercera"], output.Played);

        output.Played.Clear();
        output.Delay = TimeSpan.FromSeconds(5);
        var long_ = assistant.BeginSpeaking();
        long_.Push("Una frase muy larga que nunca acaba. Y otra más que no llega.");
        _ = long_.CompleteAsync();
        await WaitUntil(() => assistant.State == VoiceState.Speaking);
        assistant.Toggle(); // the key interrupts
        await long_.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(VoiceState.Idle, assistant.State);
        Assert.DoesNotContain("Y otra más que no llega.", output.Played);
    }

    [Fact]
    public void Models_KnowWhatIsMissingAndWhereToGetIt()
    {
        var dir = Directory.CreateTempSubdirectory("carvis-voice").FullName;
        try
        {
            var models = new VoiceModels(new AppPaths(dir, dir), new VoiceSettings { WhisperModel = "small", WakeWord = true });
            var missing = models.Missing();
            Assert.Contains(missing, m => m.Url.ToString().EndsWith("ggml-small.bin"));
            Assert.Contains(missing, m => m.Url.ToString().EndsWith("ggml-tiny.bin"));
            Assert.Contains(missing, m => m.Url.ToString() == "https://huggingface.co/rhasspy/piper-voices/resolve/main/es/es_ES/davefx/medium/es_ES-davefx-medium.onnx");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Wav_RoundTrips()
    {
        var clip = new AudioClip([0, 1000, -1000, short.MaxValue], 22050);
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
                WavFile.Write(stream, clip);
            var read = WavFile.Read(path);
            Assert.Equal(clip.Samples, read.Samples);
            Assert.Equal(22050, read.SampleRate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Speak(FakeInput input)
    {
        input.Feed(Noise(0.3, 0.002f));
        input.Feed(Tone(0.8, 0.3f));
        input.Feed(Noise(0.6, 0.002f));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition());
    }

    private static float[] Tone(double seconds, float amplitude) =>
        Enumerable.Range(0, (int)(seconds * 16000)).Select(i => amplitude * MathF.Sin(i * 2 * MathF.PI * 220 / 16000)).ToArray();

    private static float[] Noise(double seconds, float amplitude)
    {
        var random = new Random(42);
        return Enumerable.Range(0, (int)(seconds * 16000)).Select(_ => amplitude * (float)(random.NextDouble() * 2 - 1)).ToArray();
    }

    private sealed class FakeInput : IAudioInput
    {
        public bool IsAvailable => true;
        public bool IsRecording { get; private set; }
        public event Action<float[]>? SamplesAvailable;
        public IReadOnlyList<AudioDevice> Devices() => [new("0", "Micro de prueba")];
        public void Start(string? deviceName = null) => IsRecording = true;
        public void Stop() => IsRecording = false;
        public void Dispose()
        {
        }

        public void Feed(float[] samples)
        {
            if (IsRecording)
                SamplesAvailable?.Invoke(samples);
        }
    }

    private sealed class FakeOutput : IAudioOutput
    {
        public List<string> Played { get; } = [];
        public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(10);
        public bool IsAvailable => true;
        public IReadOnlyList<AudioDevice> Devices() => [];

        public async Task PlayAsync(AudioClip clip, string? deviceName = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Delay, cancellationToken);
            lock (Played)
                Played.Add(FakeTts.TextOf(clip));
        }
    }

    // Encodes the text in the clip so the output can tell what was "said".
    private sealed class FakeTts : ITextToSpeech
    {
        private static readonly Dictionary<int, string> Texts = [];
        private static int _next;

        public bool IsReady => true;

        public Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
        {
            lock (Texts)
            {
                var id = ++_next;
                Texts[id] = text;
                return Task.FromResult(new AudioClip([(short)id], 16000));
            }
        }

        public static string TextOf(AudioClip clip)
        {
            lock (Texts)
                return Texts[clip.Samples[0]];
        }
    }

    private sealed class FakeStt(string text) : ISpeechToText
    {
        public bool IsReady => true;
        public bool LastQuick { get; private set; }

        public Task<string> TranscribeAsync(float[] samples, bool quick = false, CancellationToken cancellationToken = default)
        {
            LastQuick = quick;
            return Task.FromResult(text);
        }
    }

    private sealed class FakeSession : ISessionState
    {
        public bool Locked { get; set; }
        public bool IsLocked => Locked;
    }
}
