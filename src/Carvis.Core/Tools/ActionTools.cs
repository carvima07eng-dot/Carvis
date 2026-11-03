using System.Text;
using System.Text.Json.Nodes;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools;

[BuiltInTool]
public sealed class UndoTool(IActionJournal journal) : ITool
{
    public string Name => "deshacer";
    public string Description => "Deshace la última acción que se pueda revertir (o la indicada por su id del historial).";
    public string Category => "acciones";
    public JsonObject Parameters { get; } = Object(Optional("id", String("Id de la acción del historial; si falta, la última")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var entry = Find(arguments);
        return new ToolPreview($"Deshacer: {entry.Summary}", ToolRisk.Modify)
        {
            Details = [$"  Hecho el {entry.Time:dd/MM HH:mm}"],
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var entry = Find(arguments);
        return ToolResult.Ok(await journal.UndoAsync(entry.Id, cancellationToken)) with { JournalEntryId = entry.Id };
    }

    private JournalEntry Find(ToolArguments arguments)
    {
        var id = arguments.OptionalString("id");
        var entry = string.IsNullOrWhiteSpace(id) ? journal.LastUndoable() : journal.Find(id);
        return entry ?? throw new ToolArgumentException("No hay ninguna acción reciente que se pueda deshacer.");
    }
}

[BuiltInTool]
public sealed class ActionHistoryTool(IActionJournal journal) : ITool
{
    public string Name => "historial_acciones";
    public string Description => "Muestra las últimas acciones que ha hecho Carvis en el PC (con su id para poder deshacerlas).";
    public string Category => "acciones";
    public JsonObject Parameters { get; } = Object(Optional("cantidad", Integer("Cuántas mostrar (por defecto 10)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Consultar el historial de acciones", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var entries = journal.Recent(arguments.Int("cantidad", 10, 1, 50));
        if (entries.Count == 0)
            return Task.FromResult(ToolResult.Ok("Todavía no he hecho ninguna acción."));

        var text = new StringBuilder("Últimas acciones:\n");
        foreach (var entry in entries)
        {
            var state = !entry.Success ? "falló" : entry.Undone ? "deshecha" : entry.CanUndo ? "se puede deshacer" : "hecha";
            text.Append($"- [{entry.Id}] {entry.Time:dd/MM HH:mm} {entry.Summary} ({state})\n");
        }
        return Task.FromResult(ToolResult.Ok(text.ToString().TrimEnd()));
    }
}
