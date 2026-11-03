using System.Text;
using Carvis.Core.Configuration;
using Carvis.Core.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Carvis.Voice;

/// <summary>
/// Whisper on the graphics card through Vulkan (or the CPU). The model is loaded the first time
/// it's needed and kept; the wake word uses the tiny model so checking it costs little.
/// </summary>
public sealed class WhisperSpeechToText(VoiceModels models, VoiceSettings settings, ILogger<WhisperSpeechToText>? logger = null)
    : ISpeechToText, IDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<WhisperSpeechToText>.Instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, WhisperFactory> _factories = [];
    private static bool _runtimeConfigured;

    public bool IsReady => models.HasWhisper;

    public async Task<string> TranscribeAsync(float[] samples, bool quick = false, CancellationToken cancellationToken = default)
    {
        var path = quick && models.HasWakeWordModel ? models.WakeWordModelPath : models.WhisperModelPath();
        if (!File.Exists(path))
            throw new FileNotFoundException("Falta el modelo de Whisper. Descárgalo en Ajustes → Voz.", path);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var factory = Factory(path);
            var builder = factory.CreateBuilder()
                .WithLanguage(settings.Language)
                .WithNoContext()
                .WithSingleSegment()
                .WithThreads(Math.Max(2, Environment.ProcessorCount / 2))
                // Names Whisper wouldn't spell right on its own.
                .WithPrompt(quick ? "Carvis." : "Carvis, el asistente de Carlos. Abre Spotify, crea una carpeta en el escritorio.");
            await using var processor = builder.Build();

            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally
        {
            _lock.Release();
        }
    }

    private WhisperFactory Factory(string path)
    {
        if (_factories.TryGetValue(path, out var existing))
            return existing;

        if (!_runtimeConfigured)
        {
            RuntimeOptions.RuntimeLibraryOrder = settings.UseGpu
                ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]
                : [RuntimeLibrary.Cpu];
            _runtimeConfigured = true;
        }

        var factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = settings.UseGpu });
        _logger.LogInformation("Whisper {Model} loaded with {Runtime}", Path.GetFileName(path), RuntimeOptions.LoadedLibrary);
        _factories[path] = factory;
        return factory;
    }

    /// <summary>Which runtime Whisper is using (Vulkan = GPU), for the settings page.</summary>
    public static string? LoadedRuntime => RuntimeOptions.LoadedLibrary?.ToString();

    public void Dispose()
    {
        foreach (var factory in _factories.Values)
            factory.Dispose();
        _factories.Clear();
    }
}
