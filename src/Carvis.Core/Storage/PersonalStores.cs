using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carvis.Core.Tools;
using Microsoft.Data.Sqlite;
using static Carvis.Core.Storage.SqliteConversationStore;

namespace Carvis.Core.Storage;

public sealed record Note(long Id, string Text, DateTimeOffset CreatedAt);

public sealed record TodoItem(long Id, string Text, DateTimeOffset? Due, bool Done, DateTimeOffset CreatedAt);

public enum Recurrence
{
    None,
    Hourly,
    Daily,
    Weekdays,
    Weekly,
}

public sealed record Reminder(long Id, string Text, DateTimeOffset DueAt, Recurrence Recurrence, string? Routine, bool Fired);

public sealed record Routine(string Name, string? Description, IReadOnlyList<ToolCall> Steps, DateTimeOffset CreatedAt);

public interface INoteStore
{
    Note Add(string text);
    IReadOnlyList<Note> All();
    bool Delete(long id);
}

public interface ITodoStore
{
    TodoItem Add(string text, DateTimeOffset? due);
    IReadOnlyList<TodoItem> All(bool includeDone = false);
    bool SetDone(long id, bool done);
    bool Delete(long id);
}

public interface IReminderStore
{
    Reminder Add(string text, DateTimeOffset dueAt, Recurrence recurrence = Recurrence.None, string? routine = null);
    IReadOnlyList<Reminder> Pending();
    IReadOnlyList<Reminder> Due(DateTimeOffset now);
    void Reschedule(long id, DateTimeOffset next);
    void MarkFired(long id);
    bool Delete(long id);
}

public interface IRoutineStore
{
    void Save(Routine routine);
    Routine? Find(string name);
    IReadOnlyList<Routine> All();
    bool Delete(string name);
}

/// <summary>Shared helpers: the text columns go through the content protector.</summary>
public abstract class ProtectedStore(CarvisDatabase database, IContentProtector protector)
{
    protected CarvisDatabase Database { get; } = database;

    protected string Protect(string text) => protector.Protect(text);

    protected string? TryUnprotect(string text)
    {
        try
        {
            return protector.Unprotect(text);
        }
        catch (CryptographicException)
        {
            // Encrypted on another Windows account: unreadable, skip it.
            return null;
        }
    }

    protected bool Delete(string table, long id)
    {
        using var connection = Database.Open();
        using var command = CarvisDatabase.Command(connection, $"DELETE FROM {table} WHERE id = $id", ("$id", id));
        return command.ExecuteNonQuery() > 0;
    }

    protected static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}

public sealed class SqliteNoteStore(CarvisDatabase database, IContentProtector protector, TimeProvider time)
    : ProtectedStore(database, protector), INoteStore
{
    public Note Add(string text)
    {
        var now = time.GetLocalNow();
        using var connection = Database.Open();
        var id = (long)CarvisDatabase.Scalar(connection, "INSERT INTO notes(text, created_at) VALUES ($t, $now); SELECT last_insert_rowid();",
            ("$t", Protect(text.Trim())), ("$now", Format(now)))!;
        return new Note(id, text.Trim(), now);
    }

    public IReadOnlyList<Note> All()
    {
        using var connection = Database.Open();
        using var command = CarvisDatabase.Command(connection, "SELECT id, text, created_at FROM notes ORDER BY id");
        using var reader = command.ExecuteReader();
        var notes = new List<Note>();
        while (reader.Read())
        {
            if (TryUnprotect(reader.GetString(1)) is { } text)
                notes.Add(new Note(reader.GetInt64(0), text, Parse(reader.GetString(2))));
        }
        return notes;
    }

    public bool Delete(long id) => Delete("notes", id);
}

public sealed class SqliteTodoStore(CarvisDatabase database, IContentProtector protector, TimeProvider time)
    : ProtectedStore(database, protector), ITodoStore
{
    public TodoItem Add(string text, DateTimeOffset? due)
    {
        var now = time.GetLocalNow();
        using var connection = Database.Open();
        var id = (long)CarvisDatabase.Scalar(connection, "INSERT INTO tasks(text, due, created_at) VALUES ($t, $due, $now); SELECT last_insert_rowid();",
            ("$t", Protect(text.Trim())), ("$due", due is { } d ? Format(d) : null), ("$now", Format(now)))!;
        return new TodoItem(id, text.Trim(), due, false, now);
    }

    public IReadOnlyList<TodoItem> All(bool includeDone = false)
    {
        using var connection = Database.Open();
        using var command = CarvisDatabase.Command(connection,
            $"SELECT id, text, due, done, created_at FROM tasks {(includeDone ? string.Empty : "WHERE done = 0")} ORDER BY done, due IS NULL, due, id");
        using var reader = command.ExecuteReader();
        var items = new List<TodoItem>();
        while (reader.Read())
        {
            if (TryUnprotect(reader.GetString(1)) is { } text)
            {
                items.Add(new TodoItem(reader.GetInt64(0), text, NullableString(reader, 2) is { } due ? Parse(due) : null,
                    reader.GetInt64(3) != 0, Parse(reader.GetString(4))));
            }
        }
        return items;
    }

    public bool SetDone(long id, bool done)
    {
        using var connection = Database.Open();
        using var command = CarvisDatabase.Command(connection, "UPDATE tasks SET done = $d WHERE id = $id", ("$d", done ? 1 : 0), ("$id", id));
        return command.ExecuteNonQuery() > 0;
    }

    public bool Delete(long id) => Delete("tasks", id);
}

public sealed class SqliteReminderStore(CarvisDatabase database, IContentProtector protector, TimeProvider time)
    : ProtectedStore(database, protector), IReminderStore
{
    public Reminder Add(string text, DateTimeOffset dueAt, Recurrence recurrence = Recurrence.None, string? routine = null)
    {
        using var connection = Database.Open();
        var id = (long)CarvisDatabase.Scalar(connection,
            "INSERT INTO reminders(text, due_at, recurrence, routine, created_at) VALUES ($t, $due, $r, $routine, $now); SELECT last_insert_rowid();",
            ("$t", Protect(text.Trim())), ("$due", FormatUtc(dueAt)), ("$r", recurrence == Recurrence.None ? null : recurrence.ToString()),
            ("$routine", routine), ("$now", Format(time.GetLocalNow())))!;
        return new Reminder(id, text.Trim(), dueAt, recurrence, routine, false);
    }

    public IReadOnlyList<Reminder> Pending() => Query("WHERE fired = 0 ORDER BY due_at");

    public IReadOnlyList<Reminder> Due(DateTimeOffset now) => Query("WHERE fired = 0 AND due_at <= $now ORDER BY due_at", ("$now", FormatUtc(now)));

    public void Reschedule(long id, DateTimeOffset next)
    {
        using var connection = Database.Open();
        CarvisDatabase.Execute(connection, "UPDATE reminders SET due_at = $due WHERE id = $id", ("$due", FormatUtc(next)), ("$id", id));
    }

    public void MarkFired(long id)
    {
        using var connection = Database.Open();
        CarvisDatabase.Execute(connection, "UPDATE reminders SET fired = 1 WHERE id = $id", ("$id", id));
    }

    public bool Delete(long id) => Delete("reminders", id);

    // Stored in UTC so that comparing the text in SQL compares the instants.
    private static string FormatUtc(DateTimeOffset value) => Format(value.ToUniversalTime());

    private List<Reminder> Query(string where, params (string, object?)[] parameters)
    {
        using var connection = Database.Open();
        using var command = CarvisDatabase.Command(connection, $"SELECT id, text, due_at, recurrence, routine, fired FROM reminders {where}", parameters);
        using var reader = command.ExecuteReader();
        var reminders = new List<Reminder>();
        while (reader.Read())
        {
            if (TryUnprotect(reader.GetString(1)) is not { } text)
                continue;
            var recurrence = NullableString(reader, 3) is { } r && Enum.TryParse<Recurrence>(r, out var parsed) ? parsed : Recurrence.None;
            reminders.Add(new Reminder(reader.GetInt64(0), text, Parse(reader.GetString(2)).ToLocalTime(), recurrence,
                NullableString(reader, 4), reader.GetInt64(5) != 0));
        }
        return reminders;
    }
}

public sealed class SqliteRoutineStore(CarvisDatabase database, TimeProvider time) : IRoutineStore
{
    public void Save(Routine routine)
    {
        var steps = new JsonArray(routine.Steps.Select(s => (JsonNode)new JsonObject
        {
            ["herramienta"] = s.Name,
            ["argumentos"] = s.Arguments.DeepClone(),
        }).ToArray());

        using var connection = database.Open();
        CarvisDatabase.Execute(connection,
            """
            INSERT INTO routines(name, description, steps, created_at) VALUES ($n, $d, $s, $now)
            ON CONFLICT(name) DO UPDATE SET description = excluded.description, steps = excluded.steps
            """,
            ("$n", routine.Name.Trim()), ("$d", routine.Description), ("$s", steps.ToJsonString()), ("$now", Format(time.GetLocalNow())));
    }

    public Routine? Find(string name) => Query("WHERE name = $n", ("$n", name.Trim())).FirstOrDefault();

    public IReadOnlyList<Routine> All() => Query("ORDER BY name");

    public bool Delete(string name)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection, "DELETE FROM routines WHERE name = $n", ("$n", name.Trim()));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Parses the step list the model writes: [{ "herramienta": "...", "argumentos": { ... } }].</summary>
    public static List<ToolCall> ParseSteps(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                throw new ToolArgumentException("Los pasos de la rutina no son JSON válido.");
            }
        }
        if (node is not JsonArray array || array.Count == 0)
            throw new ToolArgumentException("Faltan los pasos de la rutina: una lista de {herramienta, argumentos}.");

        var steps = new List<ToolCall>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var name = item["herramienta"]?.GetValue<string>() ?? item["tool"]?.GetValue<string>() ?? item["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                throw new ToolArgumentException("Cada paso necesita «herramienta».");
            var arguments = (item["argumentos"] ?? item["arguments"]) switch
            {
                JsonObject obj => (JsonObject)obj.DeepClone(),
                JsonValue v when v.TryGetValue<string>(out var json) && json.TrimStart().StartsWith('{') => JsonNode.Parse(json) as JsonObject ?? [],
                _ => [],
            };
            steps.Add(new ToolCall(name.Trim(), arguments));
        }
        return steps;
    }

    private List<Routine> Query(string where, params (string, object?)[] parameters)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection, $"SELECT name, description, steps, created_at FROM routines {where}", parameters);
        using var reader = command.ExecuteReader();
        var routines = new List<Routine>();
        while (reader.Read())
        {
            routines.Add(new Routine(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseSteps(JsonNode.Parse(reader.GetString(2))), Parse(reader.GetString(3))));
        }
        return routines;
    }
}
