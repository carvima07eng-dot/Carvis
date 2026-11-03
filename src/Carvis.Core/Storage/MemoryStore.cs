namespace Carvis.Core.Storage;

public sealed record MemoryItem(long Id, string Text, DateTimeOffset CreatedAt);

/// <summary>Facts about the user that Carvis keeps between conversations.</summary>
public interface IMemoryStore
{
    MemoryItem Add(string text);
    IReadOnlyList<MemoryItem> All();
    bool Delete(long id);
    void Clear();
}

public sealed class SqliteMemoryStore(CarvisDatabase database, IContentProtector protector, TimeProvider time) : IMemoryStore
{
    public MemoryItem Add(string text)
    {
        text = text.Trim();
        var existing = All().FirstOrDefault(m => string.Equals(m.Text, text, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var now = time.GetLocalNow();
        using var connection = database.Open();
        var id = (long)CarvisDatabase.Scalar(connection,
            "INSERT INTO memories(text, created_at) VALUES ($t, $now); SELECT last_insert_rowid();",
            ("$t", protector.Protect(text)), ("$now", SqliteConversationStore.Format(now)))!;
        return new MemoryItem(id, text, now);
    }

    public IReadOnlyList<MemoryItem> All()
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection, "SELECT id, text, created_at FROM memories ORDER BY id");
        using var reader = command.ExecuteReader();
        var items = new List<MemoryItem>();
        while (reader.Read())
        {
            string text;
            try
            {
                text = protector.Unprotect(reader.GetString(1));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                continue;
            }
            items.Add(new MemoryItem(reader.GetInt64(0), text, SqliteConversationStore.Parse(reader.GetString(2))));
        }
        return items;
    }

    public bool Delete(long id)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection, "DELETE FROM memories WHERE id = $id", ("$id", id));
        return command.ExecuteNonQuery() > 0;
    }

    public void Clear()
    {
        using var connection = database.Open();
        CarvisDatabase.Execute(connection, "DELETE FROM memories");
    }
}
