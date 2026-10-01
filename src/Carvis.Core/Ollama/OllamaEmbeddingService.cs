using Carvis.Core.Configuration;
using Carvis.Core.Indexing;
using OllamaSharp;
using OllamaSharp.Models;

namespace Carvis.Core.Ollama;

public sealed class OllamaEmbeddingService(IOllamaApiClient ollama, OllamaSettings settings) : IEmbeddingService
{
    private const int BatchSize = 32;

    public async Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose, CancellationToken cancellationToken = default) =>
        (await EmbedManyAsync([text], purpose, cancellationToken))[0];

    public async Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default)
    {
        var result = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var response = await ollama.EmbedAsync(new EmbedRequest
            {
                Model = settings.EmbeddingModel,
                Input = batch.Select(t => Prefix(purpose) + t).ToList(),
                KeepAlive = settings.KeepAlive,
            }, cancellationToken);

            if (response.Embeddings is null || response.Embeddings.Count != batch.Length)
                throw new InvalidOperationException("Ollama no ha devuelto los embeddings esperados.");
            result.AddRange(response.Embeddings);
        }
        return result;
    }

    // nomic-embed-text is trained with these task prefixes; other models ignore them harmlessly.
    private string Prefix(EmbeddingPurpose purpose) =>
        settings.EmbeddingModel.Contains("nomic", StringComparison.OrdinalIgnoreCase)
            ? purpose == EmbeddingPurpose.Query ? "search_query: " : "search_document: "
            : string.Empty;
}
