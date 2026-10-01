using System.Globalization;
using System.Runtime.InteropServices;
using Carvis.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Carvis.Core.Indexing;

public sealed record IndexedDocument(long Id, string Path, long Size, DateTimeOffset ModifiedAt, string Hash, int ChunkCount, string? Error);

public sealed record IndexSummary(int Documents, int Chunks, int Failed, DateTimeOffset? LastIndexed);

/// <summary>
/// Storage and search of document fragments: vectors in a sqlite-vec table (falls back to an
/// in-process scan if the extension is missing) and words in FTS5, merged with reciprocal rank fusion.
/// </summary>
public sealed class DocumentIndex(CarvisDatabase database)
{
    private const int CandidatePool = 40;
    private const double FusionK = 60;

    /// <summary>Below this similarity a fragment only counts if its words also match.</summary>
    private const double MinimumSimilarity = 0.35;
    private readonly object _vecLock = new();

    public IndexedDocument? Find(string path)
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection,
            "SELECT id, path, size, modified_at, hash, chunk_count, error FROM documents WHERE path = $p", ("$p", path));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDocument(reader) : null;
    }

    public IReadOnlyList<IndexedDocument> All()
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection, "SELECT id, path, size, modified_at, hash, chunk_count, error FROM documents ORDER BY path");
        using var reader = command.ExecuteReader();
        var list = new List<IndexedDocument>();
        while (reader.Read())
            list.Add(ReadDocument(reader));
        return list;
    }

    public IndexSummary Summary()
    {
        using var connection = database.Open();
        using var command = CarvisDatabase.Command(connection,
            "SELECT COUNT(*), COALESCE(SUM(chunk_count), 0), COALESCE(SUM(error IS NOT NULL), 0), MAX(indexed_at) FROM documents");
        using var reader = command.ExecuteReader();
        reader.Read();
        return new IndexSummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
    }

    /// <summary>Replaces everything stored for a file.</summary>
    public void Save(string path, long size, DateTimeOffset modified, string hash, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> embeddings, string? error = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        DeleteDocument(connection, transaction, path);

        var documentId = (long)Scalar(connection, transaction,
            """
            INSERT INTO documents(path, size, modified_at, hash, indexed_at, chunk_count, error)
            VALUES ($p, $s, $m, $h, $now, $c, $e); SELECT last_insert_rowid();
            """,
            ("$p", path), ("$s", size), ("$m", modified.ToString("O", CultureInfo.InvariantCulture)), ("$h", hash),
            ("$now", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)), ("$c", chunks.Count), ("$e", error))!;

        var useVec = embeddings.Count > 0 && EnsureVectorTable(connection, transaction, embeddings[0].Length);
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var blob = i < embeddings.Count ? ToBlob(embeddings[i]) : null;
            var chunkId = (long)Scalar(connection, transaction,
                "INSERT INTO chunks(document_id, ordinal, page, section, text, embedding) VALUES ($d, $o, $pg, $sec, $t, $emb); SELECT last_insert_rowid();",
                ("$d", documentId), ("$o", chunk.Index), ("$pg", chunk.Page), ("$sec", chunk.Section), ("$t", chunk.Text), ("$emb", blob))!;
            if (useVec && blob is not null)
                Execute(connection, transaction, "INSERT INTO chunks_vec(rowid, embedding) VALUES ($id, $v)", ("$id", chunkId), ("$v", blob));
        }
        transaction.Commit();
    }

    public void Touch(string path, long size, DateTimeOffset modified)
    {
        using var connection = database.Open();
        CarvisDatabase.Execute(connection, "UPDATE documents SET size = $s, modified_at = $m WHERE path = $p",
            ("$s", size), ("$m", modified.ToString("O", CultureInfo.InvariantCulture)), ("$p", path));
    }

    public void Remove(string path)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        DeleteDocument(connection, transaction, path);
        transaction.Commit();
    }

    public void Clear()
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        if (VectorTableExists(connection, transaction))
            Execute(connection, transaction, "DELETE FROM chunks_vec");
        Execute(connection, transaction, "DELETE FROM chunks");
        Execute(connection, transaction, "DELETE FROM documents");
        transaction.Commit();
    }

    public string? Meta(string key)
    {
        using var connection = database.Open();
        return CarvisDatabase.Scalar(connection, "SELECT value FROM index_meta WHERE key = $k", ("$k", key)) as string;
    }

    public void SetMeta(string key, string value)
    {
        using var connection = database.Open();
        CarvisDatabase.Execute(connection, "INSERT INTO index_meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$k", key), ("$v", value));
    }

    /// <summary>Hybrid search. <paramref name="queryEmbedding"/> may be null (keywords only).</summary>
    public IReadOnlyList<SearchResult> Search(string query, float[]? queryEmbedding, int limit, string? onlyPath = null)
    {
        using var connection = database.Open();
        var vector = queryEmbedding is null ? [] : VectorCandidates(connection, queryEmbedding, onlyPath);
        var keyword = KeywordCandidates(connection, query, onlyPath);

        // Reciprocal rank fusion: good in either list is good; good in both is best.
        var scores = new Dictionary<long, double>();
        for (var i = 0; i < vector.Count; i++)
            scores[vector[i].Id] = scores.GetValueOrDefault(vector[i].Id) + 1 / (FusionK + i + 1);
        for (var i = 0; i < keyword.Count; i++)
            scores[keyword[i]] = scores.GetValueOrDefault(keyword[i]) + 1 / (FusionK + i + 1);

        var similarity = vector.ToDictionary(v => v.Id, v => v.Similarity);
        var keywordHits = keyword.ToHashSet();
        var top = scores
            .Where(s => keywordHits.Contains(s.Key) || similarity.GetValueOrDefault(s.Key) >= MinimumSimilarity)
            .OrderByDescending(s => s.Value).Take(limit).Select(s => s.Key).ToList();
        var chunks = LoadChunks(connection, top);

        return top.Where(chunks.ContainsKey)
            .Select(id => new SearchResult(chunks[id], similarity.TryGetValue(id, out var sim) ? sim : 0))
            .ToList();
    }

    private List<(long Id, double Similarity)> VectorCandidates(SqliteConnection connection, float[] query, string? onlyPath)
    {
        var results = new List<(long, double)>();
        if (onlyPath is null && database.HasVectorSearch && VectorTableExists(connection, null) && (Meta("dimension") is not { } dim || dim == query.Length.ToString(CultureInfo.InvariantCulture)))
        {
            using var command = CarvisDatabase.Command(connection,
                "SELECT rowid, distance FROM chunks_vec WHERE embedding MATCH $q AND k = $k ORDER BY distance",
                ("$q", ToBlob(query)), ("$k", CandidatePool));
            using var reader = command.ExecuteReader();
            while (reader.Read())
                results.Add((reader.GetInt64(0), 1 - reader.GetDouble(1)));
            return results;
        }

        // Fallback (or a search inside one file): compare against stored vectors here.
        var sql = "SELECT c.id, c.embedding FROM chunks c" + (onlyPath is null ? string.Empty : " JOIN documents d ON d.id = c.document_id WHERE d.path = $p") +
                  (onlyPath is null ? " WHERE c.embedding IS NOT NULL" : " AND c.embedding IS NOT NULL");
        using (var command = CarvisDatabase.Command(connection, sql, ("$p", onlyPath)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                results.Add((reader.GetInt64(0), VectorMath.Cosine(query, FromBlob((byte[])reader[1]))));
        }
        return results.OrderByDescending(r => r.Item2).Take(CandidatePool).ToList();
    }

    private static List<long> KeywordCandidates(SqliteConnection connection, string query, string? onlyPath)
    {
        var match = FtsQuery(query);
        if (match.Length == 0)
            return [];

        var sql = onlyPath is null
            ? "SELECT rowid FROM chunks_fts WHERE chunks_fts MATCH $q ORDER BY bm25(chunks_fts) LIMIT $k"
            : "SELECT f.rowid FROM chunks_fts f JOIN chunks c ON c.id = f.rowid JOIN documents d ON d.id = c.document_id WHERE chunks_fts MATCH $q AND d.path = $p ORDER BY bm25(chunks_fts) LIMIT $k";
        try
        {
            using var command = CarvisDatabase.Command(connection, sql, ("$q", match), ("$k", CandidatePool), ("$p", onlyPath));
            using var reader = command.ExecuteReader();
            var ids = new List<long>();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }
        catch (SqliteException)
        {
            return [];
        }
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "que", "qué", "de", "del", "la", "las", "el", "los", "un", "una", "unos", "unas", "y", "o", "en", "por", "para", "con", "sin",
        "sobre", "mis", "mi", "tu", "tus", "su", "sus", "es", "son", "como", "cómo", "cual", "cuál", "dice", "dicen", "segun", "según",
        "apuntes", "documento", "documentos", "archivo", "archivos", "me", "lo", "le", "se", "al", "hay", "dame", "explica", "resume",
    };

    // Every meaningful word as a prefix, any of them: "clases abstractas" -> "clases"* OR "abstractas"*
    internal static string FtsQuery(string query)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(query.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Distinct()
            .Take(12)
            .Select(w => "\"" + (w.Length > 5 ? w[..^1] : w) + "\"*");
        return string.Join(" OR ", words);
    }

    private static Dictionary<long, TextChunk> LoadChunks(SqliteConnection connection, IReadOnlyList<long> ids)
    {
        var result = new Dictionary<long, TextChunk>();
        if (ids.Count == 0)
            return result;
        var parameters = ids.Select((id, i) => ($"$i{i}", (object?)id)).ToArray();
        using var command = CarvisDatabase.Command(connection,
            $"SELECT c.id, d.path, c.ordinal, c.text, c.page, c.section FROM chunks c JOIN documents d ON d.id = c.document_id WHERE c.id IN ({string.Join(",", parameters.Select(p => p.Item1))})",
            parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetInt64(0)] = new TextChunk(reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5));
        }
        return result;
    }

    private bool EnsureVectorTable(SqliteConnection connection, SqliteTransaction transaction, int dimension)
    {
        if (!database.HasVectorSearch)
            return false;
        lock (_vecLock)
        {
            var current = Scalar(connection, transaction, "SELECT value FROM index_meta WHERE key = 'dimension'") as string;
            if (current is not null && current != dimension.ToString(CultureInfo.InvariantCulture))
            {
                // A different embedding model: the old vectors are useless.
                Execute(connection, transaction, "DROP TABLE IF EXISTS chunks_vec");
            }
            Execute(connection, transaction, $"CREATE VIRTUAL TABLE IF NOT EXISTS chunks_vec USING vec0(embedding float[{dimension}] distance_metric=cosine)");
            Execute(connection, transaction, "INSERT INTO index_meta(key, value) VALUES ('dimension', $d) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                ("$d", dimension.ToString(CultureInfo.InvariantCulture)));
            return true;
        }
    }

    private bool VectorTableExists(SqliteConnection connection, SqliteTransaction? transaction) =>
        database.HasVectorSearch && Convert.ToInt32(Scalar(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'chunks_vec'")) > 0;

    private void DeleteDocument(SqliteConnection connection, SqliteTransaction transaction, string path)
    {
        if (VectorTableExists(connection, transaction))
            Execute(connection, transaction, "DELETE FROM chunks_vec WHERE rowid IN (SELECT c.id FROM chunks c JOIN documents d ON d.id = c.document_id WHERE d.path = $p)", ("$p", path));
        Execute(connection, transaction, "DELETE FROM documents WHERE path = $p", ("$p", path));
    }

    private static IndexedDocument ReadDocument(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.GetString(4), reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetString(6));

    private static byte[] ToBlob(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    private static float[] FromBlob(byte[] blob) => MemoryMarshal.Cast<byte, float>(blob).ToArray();

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    {
        using var command = CarvisDatabase.Command(connection, sql, parameters);
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    {
        using var command = CarvisDatabase.Command(connection, sql, parameters);
        command.Transaction = transaction;
        return command.ExecuteScalar();
    }
}
