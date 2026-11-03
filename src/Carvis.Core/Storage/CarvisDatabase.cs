using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Storage;

/// <summary>
/// The SQLite file with everything Carvis remembers. The schema is created and upgraded with
/// numbered migrations (PRAGMA user_version), so updates never lose data.
/// </summary>
public sealed class CarvisDatabase
{
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly object _initLock = new();
    private bool _initialized;

    public CarvisDatabase(string path, ILogger<CarvisDatabase>? logger = null)
    {
        Path = path;
        _logger = logger ?? NullLogger<CarvisDatabase>.Instance;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Cache = SqliteCacheMode.Shared, Pooling = true }.ToString();
    }

    public string Path { get; }

    /// <summary>Whether the sqlite-vec extension loaded (vector search for documents).</summary>
    public bool HasVectorSearch { get; private set; }

    public SqliteConnection Open()
    {
        EnsureInitialized();
        return OpenRaw();
    }

    public int SchemaVersion
    {
        get
        {
            using var connection = Open();
            return Convert.ToInt32(Scalar(connection, "PRAGMA user_version"));
        }
    }

    private SqliteConnection OpenRaw()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        TryLoadVectorExtension(connection);
        return connection;
    }

    private void TryLoadVectorExtension(SqliteConnection connection)
    {
        try
        {
            connection.EnableExtensions(true);
            connection.LoadExtension("vec0");
            HasVectorSearch = true;
        }
        catch (SqliteException ex)
        {
            if (HasVectorSearch || !_initialized)
                _logger.LogWarning("sqlite-vec not available: {Message}", ex.Message);
            HasVectorSearch = false;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        lock (_initLock)
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            using var connection = OpenRaw();
            Execute(connection, "PRAGMA journal_mode = WAL;");
            Migrate(connection);
            _initialized = true;
        }
    }

    private void Migrate(SqliteConnection connection)
    {
        var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"));
        for (var i = version; i < Migrations.All.Count; i++)
        {
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Migrations.All[i] + $"; PRAGMA user_version = {i + 1};";
            command.ExecuteNonQuery();
            transaction.Commit();
            _logger.LogInformation("Database migrated to version {Version}", i + 1);
        }
    }

    public static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteScalar();
    }

    public static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    /// <summary>Deletes every row the user created (conversations, memories, notes...), keeping the schema.</summary>
    public void DeleteAllUserData()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'chunks_vec'")) > 0 && HasVectorSearch)
        {
            using var vectors = Command(connection, "DELETE FROM chunks_vec");
            vectors.Transaction = transaction;
            vectors.ExecuteNonQuery();
        }
        foreach (var table in Migrations.UserDataTables)
        {
            using var command = Command(connection, $"DELETE FROM {table}");
            command.Transaction = transaction;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        Execute(connection, "VACUUM");
    }
}
