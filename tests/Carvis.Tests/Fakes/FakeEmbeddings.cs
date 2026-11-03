using Carvis.Core.Indexing;
using Carvis.Core.Tools;

namespace Carvis.Tests.Fakes;

/// <summary>Deterministic "embeddings": a bag of word stems hashed into 64 dimensions.</summary>
internal sealed class FakeEmbeddings : IEmbeddingService
{
    public int Calls { get; private set; }

    public Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose, CancellationToken cancellationToken = default) =>
        Task.FromResult(Vector(text));

    public Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());
    }

    private static float[] Vector(string text)
    {
        var vector = new float[64];
        foreach (var word in ToolSelector.Normalize(text).Split(' ', ',', '.', '\n', '?', '¿', ':').Where(w => w.Length > 3))
        {
            var stem = word.Length > 5 ? word[..5] : word;
            vector[(stem.GetHashCode() & 0x7fffffff) % 64] += 1;
        }
        return vector;
    }
}
