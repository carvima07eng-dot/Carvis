using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Indexing;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Documents;

[BuiltInTool]
public sealed class SearchDocumentsTool(IIndexService index, IPathPolicy paths) : ITool
{
    public string Name => "buscar_en_documentos";
    public string Description => "Busca información en los documentos indexados del usuario (apuntes, PDF, Word...) y devuelve los fragmentos con su fuente.";
    public string Category => "documentos";
    public JsonObject Parameters { get; } = Object(
        Required("consulta", String("Qué buscar, con las palabras clave del tema")),
        Optional("archivo", String("Ruta de un documento concreto donde buscar")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new($"Buscar «{arguments.String("consulta")}» en tus documentos", ToolRisk.Read);

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        if (index.Status.Summary.Chunks == 0)
            return ToolResult.Fail("No hay documentos indexados. El usuario puede elegir carpetas en Ajustes → Documentos.");

        var file = arguments.OptionalString("archivo") is { Length: > 0 } f ? paths.Resolve(f) : null;
        var results = await index.SearchAsync(arguments.String("consulta"), 8, file, cancellationToken);
        if (results.Count == 0)
            return ToolResult.Ok("No he encontrado nada sobre eso en los documentos.");

        var message = RagContextProvider.Format(results, asked: true);
        return ToolResult.Ok(message.Content) with { ContainsExternalContent = true };
    }
}

[BuiltInTool]
public sealed class IndexDocumentsTool(IIndexService index) : ITool
{
    public string Name => "indexar_documentos";
    public string Description => "Actualiza el índice de documentos del usuario (lee los archivos nuevos o modificados de sus carpetas) y dice cómo está.";
    public string Category => "documentos";
    public JsonObject Parameters { get; } = Object();

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Actualizar el índice de documentos", ToolRisk.Low) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        if (!index.Status.IsRunning)
            _ = Task.Run(() => index.IndexAsync(CancellationToken.None), CancellationToken.None);
        return Task.FromResult(ToolResult.Ok($"Indexado en marcha. Estado actual: {index.Status.Describe()}."));
    }
}

[BuiltInTool]
public sealed class SummarizeDocumentTool(IDocumentSummarizer summarizer, IPathPolicy paths) : ITool
{
    public string Name => "resumir_documento";
    public string Description => "Lee un documento entero (también si es muy largo) para resumirlo o responder sobre él.";
    public string Category => "documentos";
    public JsonObject Parameters { get; } = Object(Required("ruta", String("Ruta completa del documento")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Leer y resumir «{Path.GetFileName(Resolve(arguments))}»", ToolRisk.Read);

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default) =>
        ToolResult.Ok(await summarizer.DigestAsync(Resolve(arguments), cancellationToken)) with { ContainsExternalContent = true };

    private string Resolve(ToolArguments arguments)
    {
        var path = paths.Resolve(arguments.String("ruta"));
        paths.EnsureAllowed(path);
        if (!File.Exists(path))
            throw new ToolArgumentException($"No existe {path}.");
        return path;
    }
}

[BuiltInTool]
public sealed class CompareDocumentsTool(IDocumentSummarizer summarizer, IPathPolicy paths) : ITool
{
    public string Name => "comparar_documentos";
    public string Description => "Lee dos documentos para compararlos (diferencias, qué tienen en común).";
    public string Category => "documentos";
    public JsonObject Parameters { get; } = Object(
        Required("ruta_a", String("Ruta completa del primer documento")),
        Required("ruta_b", String("Ruta completa del segundo documento")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Comparar «{Path.GetFileName(Resolve(arguments, "ruta_a"))}» con «{Path.GetFileName(Resolve(arguments, "ruta_b"))}»", ToolRisk.Read);

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var a = await summarizer.DigestAsync(Resolve(arguments, "ruta_a"), cancellationToken);
        var b = await summarizer.DigestAsync(Resolve(arguments, "ruta_b"), cancellationToken);
        var text = new StringBuilder("DOCUMENTO A\n").Append(a).Append("\n\nDOCUMENTO B\n").Append(b);
        return ToolResult.Ok(text.ToString()) with { ContainsExternalContent = true };
    }

    private string Resolve(ToolArguments arguments, string name)
    {
        var path = paths.Resolve(arguments.String(name));
        paths.EnsureAllowed(path);
        if (!File.Exists(path))
            throw new ToolArgumentException($"No existe {path}.");
        return path;
    }
}
