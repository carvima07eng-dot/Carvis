using System.Text.Json.Nodes;
using Carvis.Core.Storage;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Memory;

[BuiltInTool]
public sealed class RememberTool(IMemoryStore memories) : ITool
{
    public string Name => "recordar";
    public string Description => "Guarda un dato sobre el usuario para recordarlo en futuras conversaciones (nombre, estudios, preferencias...).";
    public string Category => "memoria";
    public JsonObject Parameters { get; } = Object(Required("dato", String("El dato, en una frase clara, p. ej. «Se llama Carlos y estudia 2º de DAM»")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Recordar: «{arguments.String("dato")}»", ToolRisk.Low) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var text = arguments.String("dato");
        if (text.Length > 500)
            return Task.FromResult(ToolResult.Fail("El dato es demasiado largo; resúmelo en una frase."));
        var item = memories.Add(text);
        return Task.FromResult(ToolResult.Ok($"Guardado en la memoria (id {item.Id}): {item.Text}"));
    }
}

[BuiltInTool]
public sealed class ForgetTool(IMemoryStore memories) : ITool
{
    public string Name => "olvidar";
    public string Description => "Borra de la memoria un dato guardado (por su id) cuando el usuario lo pida.";
    public string Category => "memoria";
    public JsonObject Parameters { get; } = Object(Required("id", Integer("Id del recuerdo, tal como aparece en el contexto")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var item = Find(arguments);
        return new ToolPreview($"Olvidar: «{item.Text}»", ToolRisk.Modify) { PermissionScope = Name };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var item = Find(arguments);
        memories.Delete(item.Id);
        return Task.FromResult(ToolResult.Ok($"Olvidado: {item.Text}"));
    }

    private MemoryItem Find(ToolArguments arguments)
    {
        var id = arguments.Int("id", -1);
        return memories.All().FirstOrDefault(m => m.Id == id) ?? throw new ToolArgumentException($"No hay ningún recuerdo con id {id}.");
    }
}

[BuiltInTool]
public sealed class ListMemoriesTool(IMemoryStore memories) : ITool
{
    public string Name => "listar_recuerdos";
    public string Description => "Muestra todo lo que Carvis recuerda del usuario.";
    public string Category => "memoria";
    public JsonObject Parameters { get; } = Object();

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Consultar lo que recuerdo de ti", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var items = memories.All();
        return Task.FromResult(ToolResult.Ok(items.Count == 0
            ? "No tengo nada guardado sobre el usuario."
            : "Recuerdos:\n" + string.Join('\n', items.Select(i => $"- [{i.Id}] {i.Text} ({i.CreatedAt:dd/MM/yyyy})"))));
    }
}
