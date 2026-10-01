using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Storage;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Productivity;

internal static class When
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static string Describe(DateTimeOffset value, DateTimeOffset now)
    {
        var day = (value.Date - now.Date).Days switch
        {
            0 => "hoy",
            1 => "mañana",
            > 1 and < 7 => "el " + value.ToString("dddd", Spanish),
            _ => "el " + value.ToString("d 'de' MMMM", Spanish),
        };
        return $"{day} a las {value:HH:mm}";
    }

    public static string Describe(Recurrence recurrence) => recurrence switch
    {
        Recurrence.Hourly => " (cada hora)",
        Recurrence.Daily => " (todos los días)",
        Recurrence.Weekdays => " (de lunes a viernes)",
        Recurrence.Weekly => " (cada semana)",
        _ => string.Empty,
    };

    public static Recurrence ParseRecurrence(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "nunca" or "no" => Recurrence.None,
        "cada_hora" or "hora" => Recurrence.Hourly,
        "diario" or "cada_dia" or "todos_los_dias" => Recurrence.Daily,
        "laborables" or "entre_semana" => Recurrence.Weekdays,
        "semanal" or "cada_semana" => Recurrence.Weekly,
        _ => throw new ToolArgumentException("«repetir» debe ser nunca, cada_hora, diario, laborables o semanal."),
    };

    /// <summary>The moment from «fecha_hora» or «en_minutos»; a bare time means the next time it comes.</summary>
    public static DateTimeOffset Resolve(ToolArguments arguments, DateTimeOffset now)
    {
        if (arguments.OptionalNumber("en_minutos") is { } minutes)
        {
            if (minutes <= 0 || minutes > 60 * 24 * 365)
                throw new ToolArgumentException("«en_minutos» debe ser mayor que 0.");
            return now.AddMinutes(minutes);
        }

        var text = arguments.OptionalString("fecha_hora") ?? throw new ToolArgumentException("Indica «fecha_hora» (AAAA-MM-DDTHH:MM) o «en_minutos».");
        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var timeOnly) && !text.Contains('-'))
        {
            var today = new DateTimeOffset(now.Date + timeOnly.ToTimeSpan(), now.Offset);
            return today > now ? today : today.AddDays(1);
        }
        var date = arguments.OptionalDate("fecha_hora")!.Value;
        if (date <= now.AddSeconds(-30))
            throw new ToolArgumentException($"La fecha {date:yyyy-MM-dd HH:mm} ya ha pasado (ahora es {now:yyyy-MM-dd HH:mm}).");
        return date;
    }
}

[BuiltInTool]
public sealed class CreateReminderTool(IReminderStore reminders, IRoutineStore routines, TimeProvider time) : ITool
{
    public string Name => "crear_recordatorio";
    public string Description =>
        "Crea un recordatorio o alarma que avisa con una notificación a una hora (o dentro de X minutos), opcionalmente repetido. " +
        "Con «rutina», a esa hora ejecuta esa rutina en lugar de solo avisar.";
    public string Category => "recordatorios";
    public JsonObject Parameters { get; } = Object(
        Required("texto", String("Qué hay que recordar, p. ej. «Llamar a mamá»")),
        Optional("fecha_hora", String("Cuándo: AAAA-MM-DDTHH:MM, o solo HH:MM para la próxima vez que llegue esa hora")),
        Optional("en_minutos", Number("Alternativa a fecha_hora: dentro de cuántos minutos")),
        Optional("repetir", String("Repetición", "nunca", "cada_hora", "diario", "laborables", "semanal")),
        Optional("rutina", String("Nombre de una rutina guardada a ejecutar a esa hora")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var now = time.GetLocalNow();
        var due = When.Resolve(arguments, now);
        var recurrence = When.ParseRecurrence(arguments.OptionalString("repetir"));
        var routine = RoutineName(arguments);
        var what = routine is null ? $"Recordatorio «{arguments.String("texto")}»" : $"Ejecutar la rutina «{routine}»";
        return new ToolPreview($"{what} {When.Describe(due, now)}{When.Describe(recurrence)}", ToolRisk.Low) { PermissionScope = Name };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow();
        var due = When.Resolve(arguments, now);
        var recurrence = When.ParseRecurrence(arguments.OptionalString("repetir"));
        var reminder = reminders.Add(arguments.String("texto"), due, recurrence, RoutineName(arguments));
        return Task.FromResult(ToolResult.Ok($"Recordatorio {reminder.Id} creado para {When.Describe(due, now)}{When.Describe(recurrence)}."));
    }

    private string? RoutineName(ToolArguments arguments)
    {
        var name = arguments.OptionalString("rutina");
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return routines.Find(name)?.Name ?? throw new ToolArgumentException($"No existe la rutina «{name}». Créala antes con crear_rutina.");
    }
}

[BuiltInTool]
public sealed class TimerTool(IReminderStore reminders, TimeProvider time) : ITool
{
    public string Name => "temporizador";
    public string Description => "Pone un temporizador o cuenta atrás de X minutos y/o segundos que avisa al terminar.";
    public string Category => "recordatorios";
    public JsonObject Parameters { get; } = Object(
        Optional("minutos", Number("Minutos")),
        Optional("segundos", Integer("Segundos")),
        Optional("texto", String("Para qué es, p. ej. «la pasta»")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Temporizador de {Format(Duration(arguments))}", ToolRisk.Low) { PermissionScope = Name };

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var duration = Duration(arguments);
        var label = arguments.OptionalString("texto") is { Length: > 0 } text ? $"Temporizador: {text}" : $"Temporizador de {Format(duration)} terminado";
        var reminder = reminders.Add(label, time.GetLocalNow() + duration);
        return Task.FromResult(ToolResult.Ok($"Temporizador {reminder.Id} de {Format(duration)} en marcha (sonará a las {reminder.DueAt:HH:mm:ss})."));
    }

    private static TimeSpan Duration(ToolArguments arguments)
    {
        var duration = TimeSpan.FromMinutes(arguments.OptionalNumber("minutos") ?? 0) + TimeSpan.FromSeconds(arguments.Int("segundos", 0, 0));
        if (duration < TimeSpan.FromSeconds(5) || duration > TimeSpan.FromDays(1))
            throw new ToolArgumentException("El temporizador debe durar entre 5 segundos y 24 horas.");
        return duration;
    }

    private static string Format(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes} min{(duration.Seconds > 0 ? $" {duration.Seconds} s" : string.Empty)}"
            : $"{duration.Seconds} s";
}

[BuiltInTool]
public sealed class ListRemindersTool(IReminderStore reminders, TimeProvider time) : ITool
{
    public string Name => "listar_recordatorios";
    public string Description => "Muestra los recordatorios, alarmas, temporizadores y tareas programadas pendientes.";
    public string Category => "recordatorios";
    public JsonObject Parameters { get; } = Object();

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Ver los recordatorios", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var pending = reminders.Pending();
        if (pending.Count == 0)
            return Task.FromResult(ToolResult.Ok("No hay recordatorios pendientes."));
        var now = time.GetLocalNow();
        var text = new StringBuilder();
        foreach (var r in pending)
        {
            text.Append($"[{r.Id}] {r.Text} — {When.Describe(r.DueAt, now)}{When.Describe(r.Recurrence)}");
            text.Append(r.Routine is null ? "\n" : $" → rutina «{r.Routine}»\n");
        }
        return Task.FromResult(ToolResult.Ok(text.ToString().TrimEnd()));
    }
}

[BuiltInTool]
public sealed class DeleteReminderTool(IReminderStore reminders) : ITool
{
    public string Name => "borrar_recordatorio";
    public string Description => "Cancela un recordatorio, alarma o temporizador por su id (míralo antes con listar_recordatorios).";
    public string Category => "recordatorios";
    public JsonObject Parameters { get; } = Object(Required("id", Integer("Id del recordatorio")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var reminder = Find(arguments);
        return new ToolPreview($"Cancelar el recordatorio «{reminder.Text}»", ToolRisk.Low) { PermissionScope = Name };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var reminder = Find(arguments);
        reminders.Delete(reminder.Id);
        return Task.FromResult(ToolResult.Ok($"Cancelado: {reminder.Text}"));
    }

    private Reminder Find(ToolArguments arguments)
    {
        var id = arguments.Int("id", -1);
        return reminders.Pending().FirstOrDefault(r => r.Id == id) ?? throw new ToolArgumentException($"No hay ningún recordatorio pendiente con id {id}.");
    }
}
