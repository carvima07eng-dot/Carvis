using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carvis.Core.Chat;
using Carvis.Core.Tools;
using Microsoft.Data.Sqlite;

namespace Carvis.Core.Storage;

public sealed record ConversationInfo(string Id, string Title, bool Pinned, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int MessageCount);

public interface IConversationStore
{
    ConversationInfo Create(string title);
    IReadOnlyList<ConversationInfo> List(string? search = null, int limit = 200);
    ConversationInfo? Find(string id);
    IReadOnlyList<ChatMessage> Messages(string id);
    void Append(string id, IReadOnlyList<ChatMessage> messages);

    /// <summary>Removes the last turn (from the last user message on), used by "regenerate" and "edit".</summary>
    void RemoveLastTurn(string id);

    void Rename(string id, string title);
    void SetPinned(string id, bool pinned);
    void SetSummary(string id, string? summary);
    string? Summary(string id);
    void Delete(string id);
}

public sealed class SqliteConversationStore(CarvisDatabase database, IContentProtector protector, TimeProvider time) : IConversationStore
{
    public ConversationInfo Create(string title)
    {
        var now = time.GetLocalNow();
        var id = Guid.NewGuid().ToString("N");
        using var connection = database.Open();
        CarvisDatabase.Execute(connection,
            "INSERT INTO conversations(id, title, created_at, updated_at) VALUES ($id, $title, $now, $now)",
            ("$id", id), ("$title", Clean(title)), ("$now", Format(now)));
        return new ConversationInfo(id, Clean(title), false, now, now, 0);
    }

    public IReadOnlyList<ConversationInfo> List(string? search = null, int limit = 200)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection,
            """
            SELECT c.id, c.title, c.pinned, c.created_at, c.updated_at, COUNT(m.id)
            FROM conversations c LEFT JOIN messages m ON m.conversation_id = c.id
            GROUP BY c.id ORDER BY c.pinned DESC, c.updated_at DESC LIMIT $limit
            """, ("$limit", limit));

        var result = new List<ConversationInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(ReadInfo(reader));

        if (string.IsNullOrWhiteSpace(search))
            return result;

        // Contents are encrypted, so the search runs here rather than in SQL.
        var query = ToolSelector.Normalize(search);
        return result.Where(c => ToolSelector.Normalize(c.Title).Contains(query)
                                 || Messages(c.Id).Any(m => m.Role is ChatRole.User or ChatRole.Assistant && ToolSelector.Normalize(m.Content).Contains(query)))
                     .ToList();
    }

    public ConversationInfo? Find(string id)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection,
            """
            SELECT c.id, c.title, c.pinned, c.created_at, c.updated_at, COUNT(m.id)
            FROM conversations c LEFT JOIN messages m ON m.conversation_id = c.id
            WHERE c.id = $id GROUP BY c.id
            """, ("$id", id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadInfo(reader) : null;
    }

    public IReadOnlyList<ChatMessage> Messages(string id)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection,
            "SELECT role, content, tool_calls, tool_name FROM messages WHERE conversation_id = $id ORDER BY id", ("$id", id));
        var messages = new List<ChatMessage>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var role = Enum.Parse<ChatRole>(reader.GetString(0));
            var content = Unprotect(reader.GetString(1));
            var calls = reader.IsDBNull(2) ? null : ParseCalls(Unprotect(reader.GetString(2)));
            messages.Add(new ChatMessage(role, content) { ToolCalls = calls, ToolName = reader.IsDBNull(3) ? null : reader.GetString(3) });
        }
        return messages;
    }

    public void Append(string id, IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return;
        var now = Format(time.GetLocalNow());
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var message in messages)
        {
            using var command = CarvisDatabase.Command(connection,
                "INSERT INTO messages(conversation_id, role, content, tool_calls, tool_name, created_at) VALUES ($c, $role, $content, $calls, $tool, $now)",
                ("$c", id), ("$role", message.Role.ToString()), ("$content", protector.Protect(message.Content)),
                ("$calls", message.ToolCalls is { Count: > 0 } calls ? protector.Protect(SerializeCalls(calls)) : null),
                ("$tool", message.ToolName), ("$now", now));
            command.Transaction = transaction;
            command.ExecuteNonQuery();
        }
        using (var touch = CarvisDatabase.Command(connection, "UPDATE conversations SET updated_at = $now WHERE id = $id", ("$now", now), ("$id", id)))
        {
            touch.Transaction = transaction;
            touch.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void RemoveLastTurn(string id)
    {
        using var connection = database.Open();
        var lastUser = CarvisDatabase.Scalar(connection,
            "SELECT MAX(id) FROM messages WHERE conversation_id = $id AND role = 'User'", ("$id", id));
        if (lastUser is long messageId)
            CarvisDatabase.Execute(connection, "DELETE FROM messages WHERE conversation_id = $id AND id >= $m", ("$id", id), ("$m", messageId));
    }

    public void Rename(string id, string title) => Update("UPDATE conversations SET title = $v WHERE id = $id", id, Clean(title));
    public void SetPinned(string id, bool pinned) => Update("UPDATE conversations SET pinned = $v WHERE id = $id", id, pinned ? 1 : 0);
    public void SetSummary(string id, string? summary) => Update("UPDATE conversations SET summary = $v WHERE id = $id", id, summary is null ? null : protector.Protect(summary));

    public string? Summary(string id)
    {
        using var connection = database.Open();
        return CarvisDatabase.Scalar(connection, "SELECT summary FROM conversations WHERE id = $id", ("$id", id)) is string s ? Unprotect(s) : null;
    }

    public void Delete(string id)
    {
        using var connection = database.Open();
        CarvisDatabase.Execute(connection, "DELETE FROM conversations WHERE id = $id", ("$id", id));
    }

    private void Update(string sql, string id, object? value)
    {
        using var connection = database.Open();
        CarvisDatabase.Execute(connection, sql, ("$v", value), ("$id", id));
    }

    private string Unprotect(string value)
    {
        try
        {
            return protector.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return "(No se puede leer este mensaje: se guardó cifrado con otro usuario de Windows.)";
        }
    }

    private static ConversationInfo ReadInfo(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0,
        Parse(reader.GetString(3)), Parse(reader.GetString(4)), reader.GetInt32(5));

    private static string SerializeCalls(IReadOnlyList<ToolCall> calls) =>
        new JsonArray(calls.Select(c => (JsonNode)new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.DeepClone(), ["id"] = c.Id }).ToArray()).ToJsonString();

    private static IReadOnlyList<ToolCall>? ParseCalls(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.AsArray()
                .Select(n => new ToolCall(n!["name"]!.GetValue<string>(), n["arguments"]?.AsObject().DeepClone().AsObject() ?? [], n["id"]?.GetValue<string>()))
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Clean(string title)
    {
        title = title.ReplaceLineEndings(" ").Trim();
        return title.Length == 0 ? "Conversación" : title.Length > 80 ? title[..80] : title;
    }

    internal static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    internal static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
