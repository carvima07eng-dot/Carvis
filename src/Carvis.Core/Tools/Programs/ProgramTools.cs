using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Platform;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Programs;

[BuiltInTool]
public sealed class OpenProgramTool(IAppCatalog catalog) : ITool
{
    public string Name => "abrir_programa";
    public string Description => "Abre un programa instalado por su nombre (Spotify, Word, Chrome, Bloc de notas, Calculadora...).";
    public string Category => "programas";
    public JsonObject Parameters { get; } = Object(Required("nombre", String("Nombre del programa tal como lo diría el usuario")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Abrir {arguments.String("nombre")}", ToolRisk.Low) { PermissionScope = Name };

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var name = arguments.String("nombre");
        var apps = await catalog.GetAppsAsync(cancellationToken: cancellationToken);
        var ranked = AppMatcher.Rank(name, apps);

        if (ranked.Count == 0)
        {
            apps = await catalog.GetAppsAsync(refresh: true, cancellationToken);
            ranked = AppMatcher.Rank(name, apps);
        }
        if (ranked.Count == 0)
            return ToolResult.Fail($"No encuentro ningún programa instalado que se llame «{name}».");

        var (app, score) = ranked[0];
        // Two equally good matches: ask instead of guessing.
        if (score < 0.9 && ranked.Count > 1 && ranked[1].Score >= score - 0.02)
        {
            var options = string.Join(", ", ranked.Take(5).Select(r => r.App.Name));
            return ToolResult.Fail($"Hay varios programas parecidos a «{name}»: {options}. Pregunta al usuario cuál quiere.");
        }

        await catalog.LaunchAsync(app, cancellationToken);
        return ToolResult.Ok($"Abriendo {app.Name}.");
    }
}

[BuiltInTool]
public sealed class InstalledProgramsTool(IAppCatalog catalog) : ITool
{
    public string Name => "programas_instalados";
    public string Description => "Lista los programas instalados (o los que coinciden con un filtro).";
    public string Category => "programas";
    public JsonObject Parameters { get; } = Object(Optional("filtro", String("Parte del nombre a buscar")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Consultar los programas instalados", ToolRisk.Read);

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var apps = await catalog.GetAppsAsync(cancellationToken: cancellationToken);
        var filter = arguments.OptionalString("filtro");
        var list = string.IsNullOrWhiteSpace(filter) ? apps : AppMatcher.Rank(filter, apps).Select(r => r.App).ToList();
        return ToolResult.Ok(list.Count == 0
            ? "No hay programas que coincidan."
            : $"{list.Count} programas:\n" + string.Join('\n', list.Take(150).Select(a => "- " + a.Name)));
    }
}

[BuiltInTool]
public sealed class OpenWindowsTool(IWindowManager windows) : ITool
{
    public string Name => "programas_abiertos";
    public string Description => "Lista las ventanas de programas que están abiertas ahora mismo.";
    public string Category => "programas";
    public JsonObject Parameters { get; } = Object();

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Ver los programas abiertos", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var list = windows.ListWindows().Where(w => !w.ProcessName.Equals("Carvis", StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0)
            return Task.FromResult(ToolResult.Ok("No hay ventanas abiertas (aparte de Carvis)."));

        var text = new StringBuilder($"{list.Count} ventanas abiertas:\n");
        foreach (var window in list)
            text.Append($"- {window.Title} ({window.ProcessName})\n");
        return Task.FromResult(ToolResult.Ok(text.ToString().TrimEnd()));
    }
}

[BuiltInTool]
public sealed class WindowTool(IWindowManager windows) : ITool
{
    private static readonly Dictionary<string, WindowCommand> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["activar"] = WindowCommand.Activate,
        ["minimizar"] = WindowCommand.Minimize,
        ["maximizar"] = WindowCommand.Maximize,
        ["restaurar"] = WindowCommand.Restore,
        ["izquierda"] = WindowCommand.SnapLeft,
        ["derecha"] = WindowCommand.SnapRight,
        ["otro_monitor"] = WindowCommand.NextMonitor,
    };

    public string Name => "ventana";
    public string Description =>
        "Trae al frente, minimiza, maximiza, restaura o coloca (mitad izquierda/derecha, otro monitor) la ventana de un programa abierto.";
    public string Category => "programas";
    public JsonObject Parameters { get; } = Object(
        Required("programa", String("Nombre del programa o parte del título de la ventana")),
        Required("accion", String("Qué hacer con la ventana", [.. Commands.Keys])));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (window, command) = Find(arguments);
        return new ToolPreview($"{Verb(command)} «{window.Title}»", ToolRisk.Low) { PermissionScope = Name };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (window, command) = Find(arguments);
        return Task.FromResult(windows.Apply(window, command)
            ? ToolResult.Ok($"Hecho: {Verb(command).ToLowerInvariant()} «{window.Title}».")
            : ToolResult.Fail("No he podido cambiar esa ventana."));
    }

    private (WindowInfo Window, WindowCommand Command) Find(ToolArguments arguments)
    {
        var action = arguments.String("accion").Replace(' ', '_');
        if (!Commands.TryGetValue(action, out var command))
            throw new ToolArgumentException($"Acción «{action}» desconocida. Opciones: {string.Join(", ", Commands.Keys)}.");
        return (FindWindow(windows, arguments.String("programa")), command);
    }

    internal static WindowInfo FindWindow(IWindowManager windows, string query)
    {
        var q = ToolSelector.Normalize(query);
        var match = windows.ListWindows()
            .Select(w => (Window: w, Score: Math.Max(AppMatcher.Score(q, ToolSelector.Normalize(w.ProcessName)), AppMatcher.Score(q, ToolSelector.Normalize(w.Title)))))
            .Where(x => x.Score >= 0.6)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        return match.Window ?? throw new ToolArgumentException($"No hay ninguna ventana abierta de «{query}».");
    }

    private static string Verb(WindowCommand command) => command switch
    {
        WindowCommand.Activate => "Traer al frente",
        WindowCommand.Minimize => "Minimizar",
        WindowCommand.Maximize => "Maximizar",
        WindowCommand.Restore => "Restaurar",
        WindowCommand.SnapLeft => "Poner a la izquierda",
        WindowCommand.SnapRight => "Poner a la derecha",
        _ => "Mover al otro monitor",
    };
}

[BuiltInTool]
public sealed class CloseProgramTool(IWindowManager windows) : ITool
{
    public string Name => "cerrar_programa";
    public string Description =>
        "Cierra un programa abierto. Por defecto le pide que se cierre (puede preguntar si guardar); forzar=true lo termina sin guardar.";
    public string Category => "programas";
    public JsonObject Parameters { get; } = Object(
        Required("programa", String("Nombre del programa o parte del título de su ventana")),
        Optional("forzar", Boolean("true para terminarlo a la fuerza (se pierde lo no guardado)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var window = WindowTool.FindWindow(windows, arguments.String("programa"));
        var force = arguments.Bool("forzar");
        return new ToolPreview(force ? $"Forzar el cierre de «{window.Title}»" : $"Cerrar «{window.Title}»", force ? ToolRisk.Dangerous : ToolRisk.Modify)
        {
            Details = force ? ["  Se perderá lo que no esté guardado."] : [],
            PermissionScope = force ? null : $"{Name}:{window.ProcessName}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var window = WindowTool.FindWindow(windows, arguments.String("programa"));
        var closed = arguments.Bool("forzar") ? windows.Kill(window.ProcessId) : windows.Close(window);
        return Task.FromResult(closed
            ? ToolResult.Ok($"He pedido a {window.ProcessName} que se cierre.")
            : ToolResult.Fail($"No he podido cerrar {window.ProcessName}."));
    }
}
