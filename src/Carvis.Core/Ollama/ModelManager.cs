using OllamaSharp;
using OllamaSharp.Models;

namespace Carvis.Core.Ollama;

public sealed record LocalModel(string Name, long SizeBytes);

public sealed record LoadedModel(string Name, long SizeBytes, long VramBytes, int ContextLength)
{
    /// <summary>Share of the model in GPU memory (1 = fully on the GPU).</summary>
    public double GpuShare => SizeBytes > 0 ? Math.Clamp((double)VramBytes / SizeBytes, 0, 1) : 0;
}

public interface IModelManager
{
    Task<IReadOnlyList<LocalModel>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoadedModel>> LoadedAsync(CancellationToken cancellationToken = default);
    Task<bool> IsInstalledAsync(string model, CancellationToken cancellationToken = default);

    /// <summary>Downloads a model, reporting progress from 0 to 1.</summary>
    Task PullAsync(string model, IProgress<(string Status, double Progress)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Frees the model's memory now (keep_alive 0).</summary>
    Task UnloadAsync(string model, CancellationToken cancellationToken = default);
}

public sealed class ModelManager(IOllamaApiClient ollama) : IModelManager
{
    public async Task<IReadOnlyList<LocalModel>> ListAsync(CancellationToken cancellationToken = default) =>
        (await ollama.ListLocalModelsAsync(cancellationToken)).Select(m => new LocalModel(m.Name, m.Size)).OrderBy(m => m.Name).ToList();

    public async Task<IReadOnlyList<LoadedModel>> LoadedAsync(CancellationToken cancellationToken = default) =>
        (await ollama.ListRunningModelsAsync(cancellationToken)).Select(m => new LoadedModel(m.Name, m.Size, m.SizeVram, m.ContextLength)).ToList();

    public async Task<bool> IsInstalledAsync(string model, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).Any(m => ModelNames.AreSame(m.Name, model));

    public async Task PullAsync(string model, IProgress<(string Status, double Progress)>? progress = null, CancellationToken cancellationToken = default)
    {
        await foreach (var update in ollama.PullModelAsync(new PullModelRequest { Model = model }, cancellationToken))
        {
            if (update is null)
                continue;
            var fraction = update.Total > 0 ? (double)update.Completed / update.Total : 0;
            progress?.Report((Translate(update.Status), fraction));
        }
        progress?.Report(("Listo", 1));
    }

    public async Task UnloadAsync(string model, CancellationToken cancellationToken = default)
    {
        // A generate request with keep_alive 0 tells Ollama to free the model.
        await foreach (var _ in ollama.GenerateAsync(new GenerateRequest { Model = model, KeepAlive = "0" }, cancellationToken))
        {
        }
    }

    private static string Translate(string? status) => status switch
    {
        null => "Descargando…",
        "pulling manifest" => "Preparando…",
        "verifying sha256 digest" => "Comprobando…",
        "writing manifest" => "Guardando…",
        "success" => "Listo",
        _ when status.StartsWith("pulling", StringComparison.Ordinal) => "Descargando…",
        _ => status,
    };
}
