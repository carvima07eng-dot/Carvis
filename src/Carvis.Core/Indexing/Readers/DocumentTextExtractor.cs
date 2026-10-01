namespace Carvis.Core.Indexing.Readers;

/// <summary>Picks the right reader for a file. More readers are added by registering IDocumentReader.</summary>
public interface IDocumentTextExtractor
{
    bool CanRead(string path);
    Task<string> ReadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class DocumentTextExtractor(IEnumerable<IDocumentReader> readers) : IDocumentTextExtractor
{
    private readonly IReadOnlyList<IDocumentReader> _readers = readers.ToList();

    public bool CanRead(string path) => _readers.Any(r => r.CanRead(path));

    public Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var reader = _readers.FirstOrDefault(r => r.CanRead(path))
                     ?? throw new NotSupportedException($"No sé leer archivos {Path.GetExtension(path)}.");
        return reader.ReadTextAsync(path, cancellationToken);
    }
}
