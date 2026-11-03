using System.IO.Compression;
using Carvis.Core.Configuration;

namespace Carvis.Core.Voice;

public sealed record DownloadItem(string Name, Uri Url, string Destination, long ApproximateBytes);

/// <summary>Where the voice models live and where they are downloaded from (only when the user asks).</summary>
public sealed class VoiceModels(AppPaths paths, VoiceSettings settings)
{
    public static readonly string[] WhisperSizes = ["tiny", "base", "small", "medium"];
    private const string PiperRelease = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";

    private static readonly Dictionary<string, long> WhisperBytes = new()
    {
        ["tiny"] = 78_000_000,
        ["base"] = 148_000_000,
        ["small"] = 488_000_000,
        ["medium"] = 1_530_000_000,
    };

    public string WhisperDirectory => Path.Combine(paths.ModelsDirectory, "whisper");
    public string PiperDirectory => Path.Combine(paths.ModelsDirectory, "piper");

    public string WhisperModelPath(string? size = null) => Path.Combine(WhisperDirectory, $"ggml-{size ?? settings.WhisperModel}.bin");

    /// <summary>The wake word only needs the tiny model, which is fast.</summary>
    public string WakeWordModelPath => WhisperModelPath("tiny");

    public string PiperExecutable
    {
        get
        {
            var bundled = Path.Combine(PiperDirectory, "piper", OperatingSystem.IsWindows() ? "piper.exe" : "piper");
            return File.Exists(bundled) ? bundled : "piper"; // or on the PATH
        }
    }

    public string VoicePath(string? voice = null) => Path.Combine(PiperDirectory, "voices", (voice ?? settings.PiperVoice) + ".onnx");

    public bool HasWhisper => File.Exists(WhisperModelPath());
    public bool HasWakeWordModel => File.Exists(WakeWordModelPath);
    public bool HasPiper => File.Exists(VoicePath()) && (File.Exists(PiperExecutable) || !OperatingSystem.IsWindows());

    /// <summary>What is missing for the current settings.</summary>
    public IReadOnlyList<DownloadItem> Missing()
    {
        var items = new List<DownloadItem>();
        if (!HasWhisper)
            items.Add(Whisper(settings.WhisperModel));
        if (settings.WakeWord && !HasWakeWordModel)
            items.Add(Whisper("tiny"));
        if (OperatingSystem.IsWindows() && !File.Exists(Path.Combine(PiperDirectory, "piper", "piper.exe")))
            items.Add(new DownloadItem("Piper (voz de Carvis)", new Uri(PiperRelease), Path.Combine(PiperDirectory, "piper_windows_amd64.zip"), 22_000_000));
        if (!File.Exists(VoicePath()))
        {
            var url = VoiceUrl(settings.PiperVoice);
            items.Add(new DownloadItem($"Voz {settings.PiperVoice}", url, VoicePath(), 63_000_000));
            items.Add(new DownloadItem($"Voz {settings.PiperVoice} (configuración)", new Uri(url + ".json"), VoicePath() + ".json", 5_000));
        }
        return items.DistinctBy(i => i.Destination).ToList();
    }

    public DownloadItem Whisper(string size)
    {
        if (!WhisperSizes.Contains(size))
            throw new ArgumentException($"Modelo de Whisper desconocido: {size}");
        return new DownloadItem($"Whisper {size}", new Uri($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{size}.bin"),
            WhisperModelPath(size), WhisperBytes[size]);
    }

    /// <summary>es_ES-davefx-medium → es/es_ES/davefx/medium/es_ES-davefx-medium.onnx on Hugging Face.</summary>
    public static Uri VoiceUrl(string voice)
    {
        var parts = voice.Split('-');
        if (parts.Length != 3 || !parts[0].Contains('_'))
            throw new ArgumentException($"Nombre de voz no válido: {voice} (formato idioma_PAÍS-nombre-calidad)");
        var language = parts[0].Split('_')[0];
        return new Uri($"https://huggingface.co/rhasspy/piper-voices/resolve/main/{language}/{parts[0]}/{parts[1]}/{parts[2]}/{voice}.onnx");
    }

    /// <summary>Unpacks downloaded archives (Piper comes as a zip).</summary>
    public void Install(DownloadItem item)
    {
        if (!item.Destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return;
        ZipFile.ExtractToDirectory(item.Destination, Path.GetDirectoryName(item.Destination)!, overwriteFiles: true);
        File.Delete(item.Destination);
    }
}

/// <summary>Downloads a file to a .part file and renames it when complete.</summary>
public sealed class ModelDownloader(HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public async Task DownloadAsync(DownloadItem item, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
        var part = item.Destination + ".part";
        using (var response = await _http.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? item.ApproximateBytes;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(part);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                progress?.Report(total > 0 ? Math.Min(1, (double)done / total) : 0);
            }
        }
        File.Move(part, item.Destination, overwrite: true);
    }
}
