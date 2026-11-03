using Carvis.Core.Platform;

namespace Carvis.Core.Tools;

public enum UndoKind
{
    /// <summary>Move <see cref="UndoStep.Path"/> back to <see cref="UndoStep.OriginalPath"/>.</summary>
    MoveBack,

    /// <summary>Remove something the action created (empty folder deleted, otherwise to the bin).</summary>
    RemoveCreated,

    /// <summary>Put back a file the action replaced, kept at <see cref="UndoStep.Path"/>.</summary>
    RestoreReplaced,
}

public sealed record UndoStep(UndoKind Kind, string Path, string? OriginalPath = null);

public sealed record JournalEntry(
    string Id,
    DateTimeOffset Time,
    string ToolName,
    string Summary,
    bool Success,
    IReadOnlyList<UndoStep> Undo)
{
    public bool Undone { get; init; }
    public bool CanUndo => Success && !Undone && Undo.Count > 0;
}

/// <summary>Where journal entries are kept (memory now, the database once it exists).</summary>
public interface IJournalStore
{
    void Save(JournalEntry entry);
    IReadOnlyList<JournalEntry> Recent(int count);
    JournalEntry? Find(string id);
}

public sealed class InMemoryJournalStore : IJournalStore
{
    private readonly List<JournalEntry> _entries = [];
    private readonly object _lock = new();

    public void Save(JournalEntry entry)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<JournalEntry> Recent(int count)
    {
        lock (_lock)
            return _entries.OrderByDescending(e => e.Time).Take(count).ToList();
    }

    public JournalEntry? Find(string id)
    {
        lock (_lock)
            return _entries.FirstOrDefault(e => e.Id == id);
    }
}

/// <summary>Every action Carvis performs, with what is needed to undo it.</summary>
public interface IActionJournal
{
    JournalEntry Record(string toolName, string summary, bool success, IReadOnlyList<UndoStep>? undo = null);
    IReadOnlyList<JournalEntry> Recent(int count = 50);
    JournalEntry? Find(string id);
    JournalEntry? LastUndoable();
    Task<string> UndoAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class ActionJournal(IJournalStore store, IRecycleBin recycleBin, TimeProvider time) : IActionJournal
{
    public JournalEntry Record(string toolName, string summary, bool success, IReadOnlyList<UndoStep>? undo = null)
    {
        var entry = new JournalEntry(Guid.NewGuid().ToString("N")[..12], time.GetLocalNow(), toolName, summary, success, undo ?? []);
        store.Save(entry);
        return entry;
    }

    public IReadOnlyList<JournalEntry> Recent(int count = 50) => store.Recent(count);

    public JournalEntry? Find(string id) => store.Find(id);

    public JournalEntry? LastUndoable() => store.Recent(100).FirstOrDefault(e => e.CanUndo);

    public async Task<string> UndoAsync(string id, CancellationToken cancellationToken = default)
    {
        var entry = store.Find(id) ?? throw new ToolArgumentException("No encuentro esa acción en el historial.");
        if (!entry.CanUndo)
            throw new ToolArgumentException(entry.Undone ? "Esa acción ya estaba deshecha." : "Esa acción no se puede deshacer.");

        var problems = new List<string>();
        foreach (var step in entry.Undo.Reverse())
        {
            try
            {
                await UndoStepAsync(step, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{step.Path}: {ex.Message}");
            }
        }

        store.Save(entry with { Undone = true });
        return problems.Count == 0
            ? $"Deshecho: {entry.Summary}"
            : $"Deshecho en parte ({entry.Summary}). Problemas: {string.Join("; ", problems)}";
    }

    private async Task UndoStepAsync(UndoStep step, CancellationToken cancellationToken)
    {
        switch (step.Kind)
        {
            case UndoKind.MoveBack or UndoKind.RestoreReplaced:
                var original = step.OriginalPath ?? throw new IOException("Falta la ruta original.");
                if (Exists(original) && step.Kind == UndoKind.RestoreReplaced)
                    await recycleBin.SendAsync([original], cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                if (Directory.Exists(step.Path))
                    Directory.Move(step.Path, original);
                else
                    File.Move(step.Path, original);
                break;

            case UndoKind.RemoveCreated:
                if (Directory.Exists(step.Path) && !Directory.EnumerateFileSystemEntries(step.Path).Any())
                    Directory.Delete(step.Path);
                else if (Exists(step.Path))
                    await recycleBin.SendAsync([step.Path], cancellationToken);
                break;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
