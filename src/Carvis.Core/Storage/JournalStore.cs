using System.Text.Json;
using Carvis.Core.Tools;

namespace Carvis.Core.Storage;

/// <summary>The action journal survives restarts, so "deshacer" and the history work tomorrow too.</summary>
public sealed class SqliteJournalStore(CarvisDatabase database) : IJournalStore
{
    public void Save(JournalEntry entry)
    {
        using var connection = database.Open();
        CarvisDatabase.Execute(connection,
            """
            INSERT INTO journal(id, time, tool, summary, success, undo, undone) VALUES ($id, $time, $tool, $summary, $success, $undo, $undone)
            ON CONFLICT(id) DO UPDATE SET undone = excluded.undone, success = excluded.success
            """,
            ("$id", entry.Id), ("$time", SqliteConversationStore.Format(entry.Time)), ("$tool", entry.ToolName),
            ("$summary", entry.Summary), ("$success", entry.Success ? 1 : 0),
            ("$undo", JsonSerializer.Serialize(entry.Undo)), ("$undone", entry.Undone ? 1 : 0));
    }

    public IReadOnlyList<JournalEntry> Recent(int count) =>
        Query("SELECT id, time, tool, summary, success, undo, undone FROM journal ORDER BY time DESC LIMIT $n", ("$n", count));

    public JournalEntry? Find(string id) =>
        Query("SELECT id, time, tool, summary, success, undo, undone FROM journal WHERE id = $id", ("$id", id)).FirstOrDefault();

    private List<JournalEntry> Query(string sql, params (string, object?)[] parameters)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var entries = new List<JournalEntry>();
        while (reader.Read())
        {
            entries.Add(new JournalEntry(
                reader.GetString(0), SqliteConversationStore.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                reader.GetInt64(4) != 0, JsonSerializer.Deserialize<List<UndoStep>>(reader.GetString(5)) ?? [])
            {
                Undone = reader.GetInt64(6) != 0,
            });
        }
        return entries;
    }
}
