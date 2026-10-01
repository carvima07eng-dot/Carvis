namespace Carvis.Core.Indexing;

// Index the chosen folders and answer with RAG, citing the source files.
// Flow: IDocumentReader -> TextChunker -> IEmbeddingService -> DocumentIndex (SQLite + sqlite-vec + FTS5),
// and RagContextProvider, which adds the best fragments to the conversation.

/// <summary>A piece of a document with where it comes from.</summary>
public sealed record DocumentPart(string Text, int? Page = null, string? Section = null);

public sealed record DocumentText(IReadOnlyList<DocumentPart> Parts)
{
    public string FullText => string.Join("\n\n", Parts.Select(p => p.Text).Where(t => t.Length > 0));

    public static DocumentText Single(string text) => new([new DocumentPart(text)]);
}

public sealed record TextChunk(string SourcePath, int Index, string Text, int? Page = null, string? Section = null);

public sealed record SearchResult(TextChunk Chunk, double Score);

public sealed record IndexProgress(string CurrentFile, int ProcessedFiles, int TotalFiles);

/// <summary>Extracts text from a file type (PDF with PdfPig, Office with OpenXML, text...).</summary>
public interface IDocumentReader
{
    bool CanRead(string path);
    Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>nomic-embed-text wants different prefixes for what is stored and what is searched.</summary>
public enum EmbeddingPurpose
{
    Document,
    Query,
}

/// <summary>Turns text into vectors (nomic-embed-text through Ollama).</summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, EmbeddingPurpose purpose, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default);
}
