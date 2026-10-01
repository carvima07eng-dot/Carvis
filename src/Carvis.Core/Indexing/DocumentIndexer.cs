using System.Collections.Concurrent;
using System.Security.Cryptography;
using Carvis.Core.Configuration;
using Carvis.Core.Indexing.Readers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Indexing;

public sealed record IndexStatus(bool IsRunning, int Processed, int Total, string? CurrentFile, string? LastError, IndexSummary Summary)
{
    public string Describe() => IsRunning
        ? $"Indexando {Processed}/{Total}…"
        : Summary.Documents == 0 ? "Sin documentos indexados" : $"{Summary.Documents} documentos · {Summary.Chunks} fragmentos";
}

public interface IIndexService
{
    IndexStatus Status { get; }
    event Action<IndexStatus>? StatusChanged;

    /// <summary>Brings the index up to date with the configured folders (only new or changed files).</summary>
    Task IndexAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the running indexing; the next run continues where it stopped.</summary>
    void Pause();

    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 6, string? onlyPath = null, CancellationToken cancellationToken = default);

    /// <summary>Starts watching the folders for changes.</summary>
    void StartWatching();

    void ClearIndex();
}

public sealed class DocumentIndexer : IIndexService, IDisposable
{
    private readonly DocumentIndex _index;
    private readonly IDocumentTextExtractor _extractor;
    private readonly IEmbeddingService _embeddings;
    private readonly DocumentsSettings _settings;
    private readonly OllamaSettings _ollama;
    private readonly TextChunker _chunker = new();
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, DateTime> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _changeTimer;
    private CancellationTokenSource? _runCancellation;
    private IndexStatus _status;

    public DocumentIndexer(DocumentIndex index, IDocumentTextExtractor extractor, IEmbeddingService embeddings,
        DocumentsSettings settings, OllamaSettings ollama, ILogger<DocumentIndexer>? logger = null)
    {
        _index = index;
        _extractor = extractor;
        _embeddings = embeddings;
        _settings = settings;
        _ollama = ollama;
        _logger = logger ?? NullLogger<DocumentIndexer>.Instance;
        _status = new IndexStatus(false, 0, 0, null, null, index.Summary());
        _changeTimer = new Timer(_ => _ = ProcessChangesAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public IndexStatus Status => _status;
    public event Action<IndexStatus>? StatusChanged;

    public async Task IndexAsync(CancellationToken cancellationToken = default)
    {
        if (!await _running.WaitAsync(0, cancellationToken))
            return; // already running

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCancellation = run;
        try
        {
            // Changing the embedding model makes every stored vector incompatible.
            if (_index.Meta("embedding_model") is { } model && model != _ollama.EmbeddingModel)
            {
                _logger.LogInformation("Embedding model changed ({Old} -> {New}): rebuilding the index", model, _ollama.EmbeddingModel);
                _index.Clear();
            }
            _index.SetMeta("embedding_model", _ollama.EmbeddingModel);

            var files = await Task.Run(() => CandidateFiles().ToList(), run.Token);
            var wanted = files.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _index.All().Where(d => !wanted.Contains(d.Path)))
                _index.Remove(stale.Path);

            var processed = 0;
            string? lastError = null;
            Report(true, 0, files.Count, null, null);
            foreach (var file in files)
            {
                run.Token.ThrowIfCancellationRequested();
                Report(true, processed, files.Count, file.FullName, lastError);
                lastError = await IndexFileAsync(file, run.Token) ?? lastError;
                processed++;
            }
            Report(false, processed, files.Count, null, lastError);
        }
        catch (OperationCanceledException)
        {
            Report(false, _status.Processed, _status.Total, null, "Indexado en pausa.");
        }
        finally
        {
            _runCancellation = null;
            _running.Release();
        }
    }

    public void Pause() => _runCancellation?.Cancel();

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 6, string? onlyPath = null, CancellationToken cancellationToken = default)
    {
        float[]? embedding = null;
        try
        {
            embedding = await _embeddings.EmbedAsync(query, EmbeddingPurpose.Query, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without the embedding model the keyword search still works.
            _logger.LogInformation("Searching documents without embeddings: {Message}", ex.Message);
        }
        return _index.Search(query, embedding, limit, onlyPath);
    }

    public void StartWatching()
    {
        StopWatching();
        if (!_settings.WatchChanges)
            return;

        foreach (var folder in _settings.Folders.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
            };
            watcher.Changed += (_, e) => Queue(e.FullPath);
            watcher.Created += (_, e) => Queue(e.FullPath);
            watcher.Deleted += (_, e) => Queue(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Queue(e.OldFullPath);
                Queue(e.FullPath);
            };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public void ClearIndex()
    {
        Pause();
        _index.Clear();
        Report(false, 0, 0, null, null);
    }

    public void Dispose()
    {
        StopWatching();
        _changeTimer.Dispose();
    }

    private void StopWatching()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }

    // Editors save files in bursts: wait until things are quiet for two seconds.
    private void Queue(string path)
    {
        _pendingChanges[path] = DateTime.UtcNow;
        _changeTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private async Task ProcessChangesAsync()
    {
        if (!await _running.WaitAsync(0))
        {
            _changeTimer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            return;
        }
        try
        {
            foreach (var path in _pendingChanges.Keys.ToList())
            {
                _pendingChanges.TryRemove(path, out _);
                if (File.Exists(path) && IsCandidate(new FileInfo(path)))
                    await IndexFileAsync(new FileInfo(path), CancellationToken.None);
                else if (!File.Exists(path) && !Directory.Exists(path))
                    _index.Remove(path);
            }
            Report(false, _status.Processed, _status.Total, null, _status.LastError);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update the index after a change");
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<string?> IndexFileAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var known = _index.Find(file.FullName);
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        if (known is not null && known.Size == file.Length && known.ModifiedAt == modified)
            return null;

        try
        {
            var hash = await HashAsync(file.FullName, cancellationToken);
            // Touched but identical (copied, synced by OneDrive...): no need to read it again.
            if (known is not null && known.Hash == hash && known.Error is null)
            {
                _index.Touch(file.FullName, file.Length, modified);
                return null;
            }
            await ReindexAsync(file, modified, hash, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation("Could not index {File}: {Message}", file.FullName, ex.Message);
            _index.Save(file.FullName, file.Length, modified, string.Empty, [], [], ex.Message);
            return $"{file.Name}: {ex.Message}";
        }
    }

    private async Task ReindexAsync(FileInfo file, DateTimeOffset modified, string hash, CancellationToken cancellationToken)
    {
        var document = await _extractor.ReadStructuredAsync(file.FullName, cancellationToken);
        var chunks = _chunker.Split(file.FullName, document);
        var embeddings = chunks.Count == 0
            ? []
            : await _embeddings.EmbedManyAsync(chunks.Select(c => EmbeddingText(file, c)).ToList(), EmbeddingPurpose.Document, cancellationToken);
        _index.Save(file.FullName, file.Length, modified, hash, chunks, embeddings);
    }

    // The file name and section help to find a fragment ("tema 3" is often only in the title).
    private static string EmbeddingText(FileInfo file, TextChunk chunk) =>
        $"{Path.GetFileNameWithoutExtension(file.Name)}{(chunk.Section is null ? string.Empty : " · " + chunk.Section)}\n{chunk.Text}";

    private IEnumerable<FileInfo> CandidateFiles()
    {
        foreach (var folder in _settings.Folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(folder));
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                FileSystemInfo[] entries;
                try
                {
                    entries = directory.GetFileSystemInfos();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry.Attributes.HasFlag(FileAttributes.Hidden) || entry.Attributes.HasFlag(FileAttributes.System))
                        continue;
                    if (entry is DirectoryInfo sub && !_settings.ExcludedFolders.Contains(sub.Name, StringComparer.OrdinalIgnoreCase)
                        && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        pending.Push(sub);
                    else if (entry is FileInfo file && IsCandidate(file))
                        yield return file;
                }
            }
        }
    }

    private bool IsCandidate(FileInfo file) =>
        _extractor.CanRead(file.FullName)
        && file.Length > 0
        && file.Length <= _settings.MaxFileSizeMb * 1024L * 1024
        && !file.Name.StartsWith("~$", StringComparison.Ordinal)
        && !file.FullName.Split(Path.DirectorySeparatorChar).Any(part => _settings.ExcludedFolders.Contains(part, StringComparer.OrdinalIgnoreCase));

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private void Report(bool running, int processed, int total, string? current, string? error)
    {
        _status = new IndexStatus(running, processed, total, current, error, running ? _status.Summary : _index.Summary());
        StatusChanged?.Invoke(_status);
    }
}
