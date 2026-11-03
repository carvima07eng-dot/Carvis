using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Voice;

/// <summary>
/// Piper running as one long-lived process: each line written to it becomes a WAV file whose path
/// it prints back. Loading the voice once makes every sentence start quickly.
/// </summary>
public sealed class PiperTextToSpeech(VoiceModels models, VoiceSettings settings, AppPaths paths, ILogger<PiperTextToSpeech>? logger = null)
    : ITextToSpeech, IDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<PiperTextToSpeech>.Instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _process;
    private string? _loadedKey;

    public bool IsReady => models.HasPiper;

    public async Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        if (text.Length == 0)
            return new AudioClip([], 22050);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var process = EnsureProcess();
            await process.StandardInput.WriteLineAsync(text.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var path = (await process.StandardOutput.ReadLineAsync(timeout.Token))?.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new InvalidOperationException("Piper no ha generado audio.");
            try
            {
                return WavFile.Read(path);
            }
            finally
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Piper failed");
            Kill();
            throw;
        }
        catch (OperationCanceledException)
        {
            // The pending line would come out as the answer to the next sentence: start clean.
            Kill();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private Process EnsureProcess()
    {
        var key = $"{settings.PiperVoice}|{settings.SpeechRate}";
        if (_process is { HasExited: false } && _loadedKey == key)
            return _process;
        Kill();

        var output = Path.Combine(paths.TempDirectory, "speech");
        Directory.CreateDirectory(output);
        var lengthScale = (1 / Math.Clamp(settings.SpeechRate, 0.5, 2)).ToString("0.00", CultureInfo.InvariantCulture);
        var info = new ProcessStartInfo(models.PiperExecutable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "--model", models.VoicePath(), "--output_dir", output, "--length_scale", lengthScale, "--quiet" })
            info.ArgumentList.Add(argument);

        _process = Process.Start(info) ?? throw new InvalidOperationException("No he podido arrancar Piper.");
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogDebug("piper: {Line}", e.Data);
        };
        _process.BeginErrorReadLine();
        _loadedKey = key;
        return _process;
    }

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Kill();

    /// <summary>The sample rate a voice speaks at (its .onnx.json).</summary>
    public static int SampleRateOf(string voicePath)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(voicePath + ".json"))?["audio"]?["sample_rate"]?.GetValue<int>() ?? 22050;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return 22050;
        }
    }
}

public static class WavFile
{
    /// <summary>Reads a PCM 16-bit WAV (mono, or the first channel).</summary>
    public static AudioClip Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("No es un WAV.");
        reader.ReadInt32();
        reader.ReadChars(4); // WAVE
        int channels = 1, rate = 22050, bits = 16;
        while (reader.BaseStream.Position < reader.BaseStream.Length - 8)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
            }
            else if (id == "data")
            {
                if (bits != 16)
                    throw new InvalidDataException("Solo WAV de 16 bits.");
                var count = size / 2 / channels;
                var samples = new short[count];
                for (var i = 0; i < count; i++)
                {
                    samples[i] = reader.ReadInt16();
                    for (var c = 1; c < channels; c++)
                        reader.ReadInt16();
                }
                return new AudioClip(samples, rate);
            }
            else
            {
                reader.BaseStream.Seek(size, SeekOrigin.Current);
            }
        }
        throw new InvalidDataException("El WAV no tiene audio.");
    }

    public static void Write(Stream stream, AudioClip clip)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + clip.Samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(clip.SampleRate);
        writer.Write(clip.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(clip.Samples.Length * 2);
        foreach (var sample in clip.Samples)
            writer.Write(sample);
    }
}
