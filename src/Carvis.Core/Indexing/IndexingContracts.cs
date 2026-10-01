namespace Carvis.Core.Indexing;

// Phase 2: index chosen folders and answer with RAG, citing the source files.
// Flow: IDocumentReader -> ITextChunker -> IEmbeddingService -> IVectorStore (sqlite-vec),
// and an IChatContextProvider that injects the best chunks into the conversation.

public sealed record TextChunk(string SourcePath, int Index, string Text);

public sealed record SearchResult(TextChunk Chunk, double Score);

public sealed record IndexProgress(string CurrentFile, int ProcessedFiles, int TotalFiles);

/// <summary>Extracts plain text from a file type (PDF with PdfPig, DOCX with OpenXML, txt/md).</summary>
public interface IDocumentReader
{
    bool CanRead(string path);
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);
}

public interface ITextChunker
{
    IEnumerable<TextChunk> Split(string sourcePath, string text);
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

/// <summary>Stores chunks with their embeddings (SQLite + sqlite-vec).</summary>
public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<(TextChunk Chunk, float[] Embedding)> items, CancellationToken cancellationToken = default);
    Task RemoveSourceAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(float[] query, int limit, CancellationToken cancellationToken = default);
}

public interface IIndexService
{
    Task IndexFolderAsync(string folder, IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 5, CancellationToken cancellationToken = default);
}
