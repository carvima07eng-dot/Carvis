using System.Text;
using Carvis.Core.Chat;
using Carvis.Core.Indexing.Readers;

namespace Carvis.Core.Indexing;

public interface IDocumentSummarizer
{
    /// <summary>The document if it is short; otherwise summaries of each part, made by the model.</summary>
    Task<string> DigestAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class DocumentSummarizer(IDocumentTextExtractor extractor, IChatModelClient model) : IDocumentSummarizer
{
    private const int DirectLimit = 20000;
    private const int SegmentSize = 12000;
    private const int MaxSegments = 12;

    public async Task<string> DigestAsync(string path, CancellationToken cancellationToken = default)
    {
        var text = (await extractor.ReadStructuredAsync(path, cancellationToken)).FullText;
        if (text.Length <= DirectLimit)
            return $"Contenido completo de {Path.GetFileName(path)}:\n{text}";

        // Long documents: summarize parts (spread over the whole text) and let the model join them.
        var segments = Enumerable.Range(0, (text.Length + SegmentSize - 1) / SegmentSize)
            .Select(i => text.Substring(i * SegmentSize, Math.Min(SegmentSize, text.Length - i * SegmentSize)))
            .ToList();
        if (segments.Count > MaxSegments)
            segments = Enumerable.Range(0, MaxSegments).Select(i => segments[i * segments.Count / MaxSegments]).ToList();

        var result = new StringBuilder($"{Path.GetFileName(path)} es largo ({text.Length} caracteres). Resúmenes de sus partes en orden:\n\n");
        for (var i = 0; i < segments.Count; i++)
        {
            var request = new ModelRequest(
            [
                new ChatMessage(ChatRole.System, "Resumes textos de forma fiel, en español, sin inventar nada."),
                new ChatMessage(ChatRole.User, $"Resume esta parte ({i + 1} de {segments.Count}) del documento «{Path.GetFileName(path)}» en 5 a 8 frases con los datos clave:\n\n{segments[i]}"),
            ])
            { Temperature = 0.2 };

            var filter = new ThinkTagFilter();
            var summary = new StringBuilder();
            await foreach (var chunk in model.StreamAsync(request, cancellationToken))
                summary.Append(filter.Process(chunk.Text ?? string.Empty));
            summary.Append(filter.Flush());
            result.Append($"Parte {i + 1}: ").Append(summary.ToString().Trim()).Append("\n\n");
        }
        return result.ToString().TrimEnd();
    }
}
