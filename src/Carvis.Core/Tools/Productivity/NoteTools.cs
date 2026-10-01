using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Storage;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Productivity;

[BuiltInTool]
public sealed class CreateNoteTool(INoteStore notes) : ITool
{
    public string Name => "crear_nota";
    public string Description => "Apunta una nota rápida (una idea, un dato, algo que no hay que olvidar) en las notas de Carvis.";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(Required("texto", String("Texto de la nota")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Apuntar: «{Short(arguments.String("texto"))}»", ToolRisk.Low) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var note = notes.Add(arguments.String("texto"));
        return Task.FromResult(ToolResult.Ok($"Nota {note.Id} guardada."));
    }

    internal static string Short(string text) => text.Length > 80 ? text[..80] + "…" : text;
}

[BuiltInTool]
public sealed class ListNotesTool(INoteStore notes) : ITool
{
    public string Name => "listar_notas";
    public string Description => "Muestra las notas apuntadas, opcionalmente filtrando por un texto.";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(Optional("buscar", String("Texto a buscar en las notas")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Ver las notas", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var filter = arguments.OptionalString("buscar");
        var list = notes.All()
            .Where(n => string.IsNullOrWhiteSpace(filter) || ToolSelector.Normalize(n.Text).Contains(ToolSelector.Normalize(filter), StringComparison.Ordinal))
            .ToList();
        if (list.Count == 0)
            return Task.FromResult(ToolResult.Ok(string.IsNullOrWhiteSpace(filter) ? "No hay notas." : "Ninguna nota coincide."));
        var text = new StringBuilder();
        foreach (var note in list)
            text.Append($"[{note.Id}] ({note.CreatedAt:dd/MM/yyyy}) {note.Text}\n");
        return Task.FromResult(ToolResult.Ok(text.ToString().TrimEnd()));
    }
}

[BuiltInTool]
public sealed class DeleteNoteTool(INoteStore notes) : ITool
{
    public string Name => "borrar_nota";
    public string Description => "Borra una nota por su id.";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(Required("id", Integer("Id de la nota")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Borrar la nota «{CreateNoteTool.Short(Find(arguments).Text)}»", ToolRisk.Modify) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        notes.Delete(Find(arguments).Id);
        return Task.FromResult(ToolResult.Ok("Nota borrada."));
    }

    private Note Find(ToolArguments arguments)
    {
        var id = arguments.Int("id", -1);
        return notes.All().FirstOrDefault(n => n.Id == id) ?? throw new ToolArgumentException($"No hay ninguna nota con id {id}.");
    }
}

[BuiltInTool]
public sealed class CreateTaskTool(ITodoStore tasks) : ITool
{
    public string Name => "crear_tarea";
    public string Description => "Añade una tarea pendiente a la lista de tareas (con fecha límite opcional).";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(
        Required("texto", String("La tarea, p. ej. «Entregar la práctica de Android»")),
        Optional("fecha_limite", String("AAAA-MM-DD, si tiene")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Nueva tarea: «{CreateNoteTool.Short(arguments.String("texto"))}»", ToolRisk.Low) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var item = tasks.Add(arguments.String("texto"), arguments.OptionalDate("fecha_limite"));
        return Task.FromResult(ToolResult.Ok($"Tarea {item.Id} añadida{(item.Due is { } due ? $" para el {due:dd/MM/yyyy}" : string.Empty)}."));
    }
}

[BuiltInTool]
public sealed class ListTasksTool(ITodoStore tasks) : ITool
{
    public string Name => "listar_tareas";
    public string Description => "Muestra la lista de tareas pendientes (y las hechas si se pide).";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(Optional("incluir_hechas", Boolean("Mostrar también las completadas")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Ver las tareas", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var list = tasks.All(arguments.Bool("incluir_hechas"));
        if (list.Count == 0)
            return Task.FromResult(ToolResult.Ok("No hay tareas pendientes."));
        var today = DateTimeOffset.Now.Date;
        var text = new StringBuilder();
        foreach (var item in list)
        {
            var due = item.Due is { } d ? $" (para el {d:dd/MM}{(d.Date < today && !item.Done ? ", ¡atrasada!" : string.Empty)})" : string.Empty;
            text.Append($"[{item.Id}] {(item.Done ? "✔ " : string.Empty)}{item.Text}{due}\n");
        }
        return Task.FromResult(ToolResult.Ok(text.ToString().TrimEnd()));
    }
}

[BuiltInTool]
public sealed class CompleteTaskTool(ITodoStore tasks) : ITool
{
    public string Name => "completar_tarea";
    public string Description => "Marca una tarea como hecha por su id.";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(Required("id", Integer("Id de la tarea")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Marcar como hecha: «{CreateNoteTool.Short(Find(arguments).Text)}»", ToolRisk.Low) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        tasks.SetDone(Find(arguments).Id, true);
        return Task.FromResult(ToolResult.Ok("Tarea completada."));
    }

    private TodoItem Find(ToolArguments arguments)
    {
        var id = arguments.Int("id", -1);
        return tasks.All(includeDone: true).FirstOrDefault(t => t.Id == id) ?? throw new ToolArgumentException($"No hay ninguna tarea con id {id}.");
    }
}

[BuiltInTool]
public sealed class DeleteTaskTool(ITodoStore tasks) : ITool
{
    public string Name => "borrar_tarea";
    public string Description => "Borra una tarea de la lista por su id.";
    public string Category => "notas";
    public JsonObject Parameters { get; } = Object(Required("id", Integer("Id de la tarea")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var id = arguments.Int("id", -1);
        var item = tasks.All(includeDone: true).FirstOrDefault(t => t.Id == id) ?? throw new ToolArgumentException($"No hay ninguna tarea con id {id}.");
        return new ToolPreview($"Borrar la tarea «{CreateNoteTool.Short(item.Text)}»", ToolRisk.Modify) { PermissionScope = Name };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(tasks.Delete(arguments.Int("id", -1)) ? ToolResult.Ok("Tarea borrada.") : ToolResult.Fail("No existe esa tarea."));
}
