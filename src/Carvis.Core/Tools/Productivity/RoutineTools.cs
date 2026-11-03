using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Storage;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Productivity;

[BuiltInTool]
public sealed class CreateRoutineTool(IRoutineStore routines, IServiceProvider services) : ITool
{
    // Routines that start routines could loop forever.
    internal static readonly HashSet<string> NotAllowedInRoutines = new(StringComparer.OrdinalIgnoreCase)
    {
        "ejecutar_rutina", "crear_rutina", "borrar_rutina", "ejecutar_powershell",
    };

    public string Name => "crear_rutina";
    public string Description =>
        "Guarda una rutina: una lista de pasos (herramientas con sus argumentos) que se ejecutan juntos con una orden, " +
        "p. ej. «modo estudio» = abrir VS Code, abrir la carpeta de apuntes y poner el volumen al 20%.";
    public string Category => "scripts";
    public JsonObject Parameters { get; } = Object(
        Required("nombre", String("Nombre corto, p. ej. «modo estudio»")),
        Required("pasos", Array("Pasos en orden", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["herramienta"] = String("Nombre de la herramienta"),
                ["argumentos"] = new JsonObject { ["type"] = "object", ["description"] = "Argumentos de esa herramienta" },
            },
            ["required"] = new JsonArray("herramienta", "argumentos"),
        })),
        Optional("descripcion", String("Para qué sirve")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var steps = Validate(arguments);
        var exists = routines.Find(arguments.String("nombre")) is not null;
        return new ToolPreview($"{(exists ? "Reemplazar" : "Guardar")} la rutina «{arguments.String("nombre")}» ({steps.Count} pasos)", ToolRisk.Low)
        {
            Details = steps.Select((s, i) => $"  {i + 1}. {s.Name} {s.Arguments.ToJsonString()}").ToList(),
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var steps = Validate(arguments);
        routines.Save(new Routine(arguments.String("nombre"), arguments.OptionalString("descripcion"), steps, DateTimeOffset.Now));
        return Task.FromResult(ToolResult.Ok($"Rutina «{arguments.String("nombre")}» guardada con {steps.Count} pasos. Se ejecuta diciendo «ejecuta {arguments.String("nombre")}»."));
    }

    private List<ToolCall> Validate(ToolArguments arguments)
    {
        var steps = SqliteRoutineStore.ParseSteps(arguments.Json["pasos"]);
        if (steps.Count > 20)
            throw new ToolArgumentException("Una rutina puede tener como mucho 20 pasos.");

        // The registry is resolved lazily: it contains this tool.
        var registry = (IToolRegistry)services.GetService(typeof(IToolRegistry))!;
        foreach (var step in steps)
        {
            if (NotAllowedInRoutines.Contains(step.Name))
                throw new ToolArgumentException($"«{step.Name}» no se puede usar dentro de una rutina.");
            var tool = registry.Find(step.Name) ?? throw new ToolArgumentException($"La herramienta «{step.Name}» no existe.");
            try
            {
                tool.Preview(new ToolArguments(step.Arguments), ToolContext.Default);
            }
            catch (ToolArgumentException ex)
            {
                throw new ToolArgumentException($"Paso «{step.Name}»: {ex.Message}");
            }
        }
        return steps;
    }
}

[BuiltInTool]
public sealed class RunRoutineTool(IRoutineStore routines) : ITool
{
    public string Name => "ejecutar_rutina";
    public string Description => "Ejecuta una rutina guardada por su nombre (cada paso pide confirmación si hace falta).";
    public string Category => "scripts";
    public JsonObject Parameters { get; } = Object(Required("nombre", String("Nombre de la rutina")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var routine = Find(arguments);
        return new ToolPreview($"Ejecutar la rutina «{routine.Name}» ({routine.Steps.Count} pasos)", ToolRisk.Read);
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var routine = Find(arguments);
        return Task.FromResult(ToolResult.Ok($"Ejecutando la rutina «{routine.Name}»: {string.Join(", ", routine.Steps.Select(s => s.Name))}.")
            with
        { FollowUpCalls = routine.Steps });
    }

    private Routine Find(ToolArguments arguments)
    {
        var name = arguments.String("nombre");
        var saved = string.Join(", ", routines.All().Select(r => r.Name));
        return routines.Find(name) ?? throw new ToolArgumentException(
            $"No existe la rutina «{name}». Rutinas guardadas: {(saved.Length > 0 ? saved : "ninguna")}.");
    }
}

[BuiltInTool]
public sealed class ListRoutinesTool(IRoutineStore routines) : ITool
{
    public string Name => "listar_rutinas";
    public string Description => "Muestra las rutinas guardadas y sus pasos.";
    public string Category => "scripts";
    public JsonObject Parameters { get; } = Object();

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Ver las rutinas", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var all = routines.All();
        if (all.Count == 0)
            return Task.FromResult(ToolResult.Ok("No hay rutinas guardadas."));
        var text = new StringBuilder();
        foreach (var routine in all)
        {
            text.Append($"«{routine.Name}»{(routine.Description is { Length: > 0 } d ? $" — {d}" : string.Empty)}\n");
            foreach (var step in routine.Steps)
                text.Append($"  - {step.Name} {step.Arguments.ToJsonString()}\n");
        }
        return Task.FromResult(ToolResult.Ok(text.ToString().TrimEnd()));
    }
}

[BuiltInTool]
public sealed class DeleteRoutineTool(IRoutineStore routines) : ITool
{
    public string Name => "borrar_rutina";
    public string Description => "Borra una rutina guardada.";
    public string Category => "scripts";
    public JsonObject Parameters { get; } = Object(Required("nombre", String("Nombre de la rutina")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var name = arguments.String("nombre");
        _ = routines.Find(name) ?? throw new ToolArgumentException($"No existe la rutina «{name}».");
        return new ToolPreview($"Borrar la rutina «{name}»", ToolRisk.Modify);
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(routines.Delete(arguments.String("nombre")) ? ToolResult.Ok("Rutina borrada.") : ToolResult.Fail("No existe esa rutina."));
}
