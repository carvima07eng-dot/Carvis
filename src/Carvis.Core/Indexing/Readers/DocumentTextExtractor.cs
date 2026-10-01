namespace Carvis.Core.Indexing.Readers;

/// <summary>Picks the right reader for a file. More readers are added by registering IDocumentReader.</summary>
public interface IDocumentTextExtractor
{
    bool CanRead(string path);
    Task<DocumentText> ReadStructuredAsync(string path, CancellationToken cancellationToken = default);
    Task<string> ReadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class DocumentTextExtractor(IEnumerable<IDocumentReader> readers) : IDocumentTextExtractor
{
    private readonly IReadOnlyList<IDocumentReader> _readers = readers.ToList();

    public bool CanRead(string path) => _readers.Any(r => r.CanRead(path));

    public Task<DocumentText> ReadStructuredAsync(string path, CancellationToken cancellationToken = default)
    {
        var reader = _readers.FirstOrDefault(r => r.CanRead(path))
                     ?? throw new NotSupportedException($"No sé leer archivos {Path.GetExtension(path)}.");
        return reader.ReadAsync(path, cancellationToken);
    }

    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var document = await ReadStructuredAsync(path, cancellationToken);
        return string.Join("\n\n", document.Parts.Select(p => p.Page is { } page ? $"[Página {page}]\n{p.Text}" : p.Text));
    }
}
