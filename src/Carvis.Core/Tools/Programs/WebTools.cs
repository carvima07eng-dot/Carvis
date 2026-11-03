using System.Text.Json.Nodes;
using Carvis.Core.Platform;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Programs;

[BuiltInTool]
public sealed class OpenUrlTool(IShell shell) : ITool
{
    public string Name => "abrir_web";
    public string Description => "Abre una página web en el navegador predeterminado.";
    public string Category => "web";
    public JsonObject Parameters { get; } = Object(Required("url", String("Dirección web, p. ej. https://www.floridauniversitaria.es")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Abrir {Normalize(arguments.String("url"))} en el navegador", ToolRisk.Low) { PermissionScope = Name };

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var url = Normalize(arguments.String("url"));
        await shell.OpenAsync(url, cancellationToken);
        return ToolResult.Ok($"Abierto {url} en el navegador.");
    }

    internal static string Normalize(string url)
    {
        url = url.Trim();
        if (!url.Contains("://"))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ToolArgumentException($"«{url}» no es una dirección web válida (solo http y https).");
        return uri.AbsoluteUri;
    }
}

[BuiltInTool]
public sealed class WebSearchTool(IShell shell) : ITool
{
    private static readonly Dictionary<string, string> Sites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google"] = "https://www.google.com/search?q=",
        ["youtube"] = "https://www.youtube.com/results?search_query=",
        ["wikipedia"] = "https://es.wikipedia.org/w/index.php?search=",
        ["mapas"] = "https://www.google.com/maps/search/",
        ["imagenes"] = "https://www.google.com/search?tbm=isch&q=",
        ["traductor"] = "https://translate.google.com/?sl=auto&tl=es&text=",
    };

    public string Name => "buscar_en_web";
    public string Description => "Abre en el navegador una búsqueda en Google, YouTube, Wikipedia, Google Maps, imágenes o el traductor.";
    public string Category => "web";
    public JsonObject Parameters { get; } = Object(
        Required("consulta", String("Qué buscar")),
        Optional("sitio", String("Dónde buscar (por defecto google)", [.. Sites.Keys])));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Buscar «{arguments.String("consulta")}» en {Site(arguments)}", ToolRisk.Low) { PermissionScope = Name };

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var url = Sites[Site(arguments)] + Uri.EscapeDataString(arguments.String("consulta"));
        await shell.OpenAsync(url, cancellationToken);
        return ToolResult.Ok($"Búsqueda abierta en el navegador: {url}");
    }

    private static string Site(ToolArguments arguments)
    {
        var site = ToolSelector.Normalize(arguments.OptionalString("sitio") ?? "google");
        return Sites.ContainsKey(site) ? site : "google";
    }
}
