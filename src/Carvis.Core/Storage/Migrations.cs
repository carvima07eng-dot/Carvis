namespace Carvis.Core.Storage;

/// <summary>Schema changes in order. Never edit a released one: add a new migration instead.</summary>
internal static class Migrations
{
    public static readonly IReadOnlyList<string> All =
    [
        // 1: conversations, action journal, long-term memory
        """
        CREATE TABLE conversations (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            summary TEXT,
            pinned INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE TABLE messages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            role TEXT NOT NULL,
            content TEXT NOT NULL,
            tool_calls TEXT,
            tool_name TEXT,
            created_at TEXT NOT NULL
        );
        CREATE INDEX ix_messages_conversation ON messages(conversation_id, id);
        CREATE TABLE journal (
            id TEXT PRIMARY KEY,
            time TEXT NOT NULL,
            tool TEXT NOT NULL,
            summary TEXT NOT NULL,
            success INTEGER NOT NULL,
            undo TEXT NOT NULL,
            undone INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_journal_time ON journal(time);
        CREATE TABLE memories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            text TEXT NOT NULL,
            created_at TEXT NOT NULL
        )
        """,

        // 2: indexed documents (RAG). Vectors live in the vec0 table created at runtime when sqlite-vec loads.
        """
        CREATE TABLE documents (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            path TEXT NOT NULL UNIQUE,
            size INTEGER NOT NULL,
            modified_at TEXT NOT NULL,
            hash TEXT NOT NULL,
            indexed_at TEXT NOT NULL,
            chunk_count INTEGER NOT NULL DEFAULT 0,
            error TEXT
        );
        CREATE TABLE chunks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            page INTEGER,
            section TEXT,
            text TEXT NOT NULL,
            embedding BLOB
        );
        CREATE INDEX ix_chunks_document ON chunks(document_id);
        CREATE VIRTUAL TABLE chunks_fts USING fts5(text, content='chunks', content_rowid='id', tokenize='unicode61 remove_diacritics 2');
        CREATE TRIGGER chunks_ai AFTER INSERT ON chunks BEGIN
            INSERT INTO chunks_fts(rowid, text) VALUES (new.id, new.text);
        END;
        CREATE TRIGGER chunks_ad AFTER DELETE ON chunks BEGIN
            INSERT INTO chunks_fts(chunks_fts, rowid, text) VALUES ('delete', old.id, old.text);
        END
        """,

        // 3: notes, tasks, reminders and routines
        """
        CREATE TABLE notes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            text TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE tasks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            text TEXT NOT NULL,
            due TEXT,
            done INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        CREATE TABLE reminders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            text TEXT NOT NULL,
            due_at TEXT NOT NULL,
            recurrence TEXT,
            routine TEXT,
            fired INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        CREATE INDEX ix_reminders_due ON reminders(fired, due_at);
        CREATE TABLE routines (
            name TEXT PRIMARY KEY COLLATE NOCASE,
            description TEXT,
            steps TEXT NOT NULL,
            created_at TEXT NOT NULL
        )
        """,

        // 4: settings of the document index (embedding model and vector size)
        """
        CREATE TABLE index_meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        )
        """,
    ];

    /// <summary>Tables with the user's data, children first (for "delete all my data").</summary>
    public static readonly string[] UserDataTables =
        ["messages", "conversations", "journal", "memories", "chunks", "documents", "notes", "tasks", "reminders", "routines"];
}
