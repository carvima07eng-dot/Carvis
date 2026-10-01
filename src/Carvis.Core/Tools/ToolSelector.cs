using System.Globalization;
using System.Text;
using Carvis.Core.Chat;
using Carvis.Core.Indexing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Tools;

public interface IToolSelector
{
    /// <summary>The tools worth offering for this message. Empty when it is just conversation.</summary>
    Task<IReadOnlyList<ITool>> SelectAsync(string userMessage, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default);
}

/// <summary>
/// Small models choose much better among a handful of tools than among fifty, and every tool
/// definition costs context. Tools are picked by category keywords, by semantic similarity of
/// their description (embeddings) and by what the previous turn was doing.
/// </summary>
public sealed class ToolSelector(
    IToolRegistry registry,
    IEmbeddingService? embeddings = null,
    ILogger<ToolSelector>? logger = null) : IToolSelector
{
    private const int MaxTools = 10;
    private const double SimilarityThreshold = 0.58;

    private static readonly Dictionary<string, string[]> CategoryKeywords = new()
    {
        ["archivos"] =
        [
            "carpeta",
            "archivo",
            "fichero",
            "documento",
            "mueve",
            "mover",
            "copia",
            "copiar",
            "renombra",
            "cambia el nombre",
            "borra",
            "elimina",
            "papelera",
            "zip",
            "comprim*",
            "descomprim*",
            "busca",
            "encuentra",
            "lista",
            "listar",
            "que hay en",
            "ordena",
            "organiza",
            "duplicad*",
            "ocupa",
            "grandes",
            "escritorio",
            "descargas",
            "documentos",
            "imagenes",
            "fotos",
            ".pdf",
            ".txt",
            ".docx",
            "pdf",
            "crea",
            "crear",
            "guarda",
            "explorador",
            "lee",
            "leer",
            "abre el archivo",
        ],
        ["programas"] =
        [
            "abre",
            "abrir",
            "lanza",
            "inicia",
            "ejecuta",
            "cierra",
            "cerrar",
            "programa",
            "aplicacion*",
            "app",
            "ventana",
            "minimiza",
            "maximiza",
            "pon a la izquierda",
            "pon a la derecha",
            "otro monitor",
            "cambia a",
            "que tengo abierto",
            "spotify",
            "chrome",
            "edge",
            "firefox",
            "word",
            "excel",
            "visual studio",
            "vs code",
            "discord",
            "steam",
            "bloc de notas",
        ],
        ["web"] = ["web", "pagina", "url", "google", "youtube", "wikipedia", "navegador", "http", "www", ".com", ".es", "busca en internet", "buscame"],
        ["sistema"] =
        [
            "volumen",
            "sonido",
            "silencia",
            "sube",
            "baja",
            "brillo",
            "apaga",
            "reinicia",
            "suspende",
            "bloquea",
            "bateria",
            "memoria ram",
            "ram",
            "cpu",
            "procesador",
            "grafica",
            "gpu",
            "disco",
            "espacio",
            "wifi",
            "bluetooth",
            "modo oscuro",
            "modo claro",
            "portapapeles",
            "copiado",
            "pausa",
            "siguiente cancion",
            "musica",
            "reproduce",
            "informacion del equipo",
        ],
        ["recordatorios"] = ["recuerdame", "recordatorio", "alarma", "temporizador", "avisame", "en 5 minutos", "minutos", "a las", "manana", "cada lunes", "programa una tarea"],
        ["notas"] = ["nota", "apunta", "anota", "tarea", "pendiente", "lista de tareas", "por hacer", "to do", "todo list"],
        ["calculos"] = ["calcula", "cuanto es", "cuanto son", "convierte", "conversion", "raiz", "porcentaje", "%", "dias entre", "dias faltan", "que dia", "que hora", "hora en", "divisa", "euros", "dolares"],
        ["memoria"] = ["recuerda que", "acuerdate", "me llamo", "mi nombre", "olvida", "que sabes de mi", "que recuerdas"],
        ["documentos"] = ["mis apuntes", "mis documentos", "segun", "en mis archivos", "en el pdf", "indexa", "indexar", "busca en mis"],
        ["scripts"] = ["powershell", "script", "comando", "terminal", "consola", "rutina", "modo estudio", "automatiza"],
        ["vision"] = ["pantalla", "captura", "screenshot", "que ves", "que hay en mi pantalla", "este error", "imagen"],
        ["internet"] = ["tiempo hace", "el tiempo", "temperatura en", "llueve", "clima", "noticias", "busca en la web"],
        ["acciones"] = ["deshaz", "deshacer", "revierte", "vuelve a como estaba", "que has hecho", "historial de acciones"],
    };

    // Follow-ups such as "y ahora muévelo a Descargas" keep the categories of the previous turn.
    private static readonly string[] FollowUpCategoriesWith = ["acciones"];

    private readonly Dictionary<string, float[]> _toolEmbeddings = new();
    private readonly SemaphoreSlim _embeddingLock = new(1, 1);
    private readonly ILogger _logger = logger ?? NullLogger<ToolSelector>.Instance;
    private bool _embeddingsUnavailable;

    public async Task<IReadOnlyList<ITool>> SelectAsync(string userMessage, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        var tools = registry.All;
        if (tools.Count == 0)
            return [];

        var text = Normalize(userMessage);
        var categories = CategoryKeywords.Where(c => c.Value.Any(k => Matches(text, k))).Select(c => c.Key).ToHashSet();

        foreach (var category in PreviousTurnCategories(history, tools))
            categories.Add(category);
        if (categories.Count > 0)
            categories.UnionWith(FollowUpCategoriesWith);

        var similar = await SimilarToolsAsync(userMessage, tools, cancellationToken);
        var stems = Stems(text);

        // Rank: tools whose name and description share words with the message come first.
        return tools
            .Select(t => (Tool: t, Score: Score(t, categories, similar, stems) is var basic and > 0 ? basic + KeywordScore(t, text) : 0))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(MaxTools)
            .Select(x => x.Tool)
            .ToList();
    }

    private static double Score(ITool tool, HashSet<string> categories, IReadOnlyList<ITool> similar, HashSet<string> messageStems)
    {
        var score = 0.0;
        if (categories.Contains(tool.Category))
            score += tool.Category == "acciones" ? 0.5 : 1;
        var rank = IndexOf(similar, tool);
        if (rank >= 0)
            score += 1.5 - rank * 0.1;
        if (score == 0)
            return 0;

        var toolStems = Stems(Normalize(tool.Name.Replace('_', ' ') + " " + tool.Description));
        score += 0.1 * messageStems.Count(toolStems.Contains);
        return score;
    }

    // Exact phrases for a tool weigh most; they also bring in a tool whose category didn't match.
    private static double KeywordScore(ITool tool, string text)
    {
        var keywords = tool is IHasKeywords own ? own.Keywords : ToolKeywords.ByTool.GetValueOrDefault(tool.Name, []);
        return keywords.Where(k => Matches(text, k)).Sum(k => k.Length >= 8 ? 2.5 : 1.5);
    }

    private static int IndexOf(IReadOnlyList<ITool> list, ITool tool)
    {
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], tool))
                return i;
        return -1;
    }

    // Crude Spanish stemming: the first five letters of each word with four or more letters.
    private static HashSet<string> Stems(string normalizedText) =>
        normalizedText.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4 && !StopWords.Contains(w))
            .Select(w => w.Length > 5 ? w[..5] : w)
            .ToHashSet();

    private static readonly char[] WordSeparators = [' ', ',', '.', '?', '¿', '!', '¡', ':', ';', '(', ')', '"', '\'', '/', '\\', '-'];

    private static readonly HashSet<string> StopWords = ["para", "como", "esta", "este", "esto", "tengo", "quiero", "puedes", "porfa", "favor", "dime", "hazme", "todos", "todas", "desde", "hasta", "sobre", "donde", "cuando", "mismo"];

    /// <summary>Whole-word match ("ordena" doesn't match "ordenador"); a trailing * allows any ending.</summary>
    public static bool Matches(string normalizedText, string keyword)
    {
        var prefix = keyword.EndsWith('*');
        var word = Normalize(prefix ? keyword[..^1] : keyword);
        var start = 0;
        while ((start = normalizedText.IndexOf(word, start, StringComparison.Ordinal)) >= 0)
        {
            var end = start + word.Length;
            var startsWord = start == 0 || !char.IsLetterOrDigit(normalizedText[start - 1]) || !char.IsLetterOrDigit(word[0]);
            var endsWord = prefix || end == normalizedText.Length || !char.IsLetterOrDigit(normalizedText[end]) || !char.IsLetterOrDigit(word[^1]);
            if (startsWord && endsWord)
                return true;
            start++;
        }
        return false;
    }

    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static IEnumerable<string> PreviousTurnCategories(IReadOnlyList<ChatMessage> history, IReadOnlyList<ITool> tools)
    {
        var lastUser = -1;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == ChatRole.User)
            {
                lastUser = i;
                break;
            }
        }
        if (lastUser < 0)
            yield break;

        for (var i = lastUser + 1; i < history.Count; i++)
        {
            foreach (var call in history[i].ToolCalls ?? [])
            {
                if (tools.FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.OrdinalIgnoreCase)) is { } tool)
                    yield return tool.Category;
            }
        }
    }

    private async Task<IReadOnlyList<ITool>> SimilarToolsAsync(string userMessage, IReadOnlyList<ITool> tools, CancellationToken cancellationToken)
    {
        if (embeddings is null || _embeddingsUnavailable || userMessage.Length < 6)
            return [];

        try
        {
            await EnsureToolEmbeddingsAsync(tools, cancellationToken);
            var query = await embeddings.EmbedAsync(userMessage, EmbeddingPurpose.Query, cancellationToken);

            return tools
                .Select(t => (Tool: t, Score: _toolEmbeddings.TryGetValue(t.Name, out var v) ? VectorMath.Cosine(query, v) : 0))
                .Where(x => x.Score >= SimilarityThreshold)
                .OrderByDescending(x => x.Score)
                .Take(6)
                .Select(x => x.Tool)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without the embedding model, keywords alone still work.
            _logger.LogInformation("Tool selection without embeddings: {Message}", ex.Message);
            _embeddingsUnavailable = true;
            return [];
        }
    }

    private async Task EnsureToolEmbeddingsAsync(IReadOnlyList<ITool> tools, CancellationToken cancellationToken)
    {
        var missing = tools.Where(t => !_toolEmbeddings.ContainsKey(t.Name)).ToList();
        if (missing.Count == 0)
            return;

        await _embeddingLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var tool in missing.Where(t => !_toolEmbeddings.ContainsKey(t.Name)))
                _toolEmbeddings[tool.Name] = await embeddings!.EmbedAsync($"{tool.Name}: {tool.Description}", EmbeddingPurpose.Document, cancellationToken);
        }
        finally
        {
            _embeddingLock.Release();
        }
    }
}
