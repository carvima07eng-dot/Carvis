using System.Globalization;
using System.Text.Json.Nodes;
using Carvis.Core.Text;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Utilities;

[BuiltInTool]
public sealed class CalculateTool : ITool
{
    public string Name => "calcular";
    public string Description =>
        "Calcula una expresión matemática exacta: + - * / ^, paréntesis, mod, porcentajes (20% de 150, 80 + 21%), sqrt, sin, cos, log, ln. " +
        "Úsala siempre en vez de calcular de cabeza.";
    public string Category => "calculos";
    public JsonObject Parameters { get; } = Object(Required("expresion", String("La expresión, p. ej. «(1250 * 0.21) + 30» o «15% de 80»")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new($"Calcular {arguments.String("expresion")}", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var expression = arguments.String("expresion");
        try
        {
            var value = ExpressionEvaluator.Evaluate(expression);
            return Task.FromResult(ToolResult.Ok($"{expression} = {Format(value)}"));
        }
        catch (Exception ex) when (ex is FormatException or ArithmeticException)
        {
            return Task.FromResult(ToolResult.Fail($"No he podido calcular «{expression}»: {ex.Message}"));
        }
    }

    public static string Format(double value) =>
        Math.Abs(value) >= 1e15 || (Math.Abs(value) < 1e-6 && value != 0)
            ? value.ToString("G10", CultureInfo.GetCultureInfo("es-ES"))
            : Math.Round(value, 10).ToString("#,0.##########", CultureInfo.GetCultureInfo("es-ES"));
}

[BuiltInTool]
public sealed class ConvertUnitsTool : ITool
{
    // Factor to the base unit of each dimension.
    private static readonly Dictionary<string, (string Dimension, double Factor)> Units = Build();

    public string Name => "convertir_unidades";
    public string Description =>
        "Convierte entre unidades: longitud (km, m, cm, mm, millas, pies, pulgadas, yardas), peso (kg, g, mg, libras, onzas, toneladas), " +
        "volumen (l, ml, galones, tazas), temperatura (celsius, fahrenheit, kelvin), velocidad (km/h, m/s, mph, nudos), " +
        "datos (bytes, KB, MB, GB, TB, bits), tiempo (segundos a años), superficie (m2, km2, hectáreas, acres) y energía (julios, calorías, kWh).";
    public string Category => "calculos";
    public JsonObject Parameters { get; } = Object(
        Required("valor", Number("Cantidad")),
        Required("de", String("Unidad de origen, p. ej. «millas»")),
        Required("a", String("Unidad de destino, p. ej. «km»")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Convertir {arguments.Number("valor")} {arguments.String("de")} a {arguments.String("a")}", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var value = arguments.Number("valor");
        var from = Find(arguments.String("de"));
        var to = Find(arguments.String("a"));
        if (from.Unit.Dimension != to.Unit.Dimension)
            return Task.FromResult(ToolResult.Fail($"No se puede convertir {from.Name} a {to.Name}: miden cosas distintas."));

        var result = from.Unit.Dimension == "temperatura"
            ? FromKelvin(ToKelvin(value, from.Name), to.Name)
            : value * from.Unit.Factor / to.Unit.Factor;
        return Task.FromResult(ToolResult.Ok($"{CalculateTool.Format(value)} {from.Name} = {CalculateTool.Format(Math.Round(result, 6))} {to.Name}"));
    }

    public static double Convert(double value, string from, string to)
    {
        var a = Find(from);
        var b = Find(to);
        if (a.Unit.Dimension != b.Unit.Dimension)
            throw new ToolArgumentException("Unidades incompatibles.");
        return a.Unit.Dimension == "temperatura" ? FromKelvin(ToKelvin(value, a.Name), b.Name) : value * a.Unit.Factor / b.Unit.Factor;
    }

    private static (string Name, (string Dimension, double Factor) Unit) Find(string name)
    {
        var key = ToolSelector.Normalize(name).Trim().Replace("º", string.Empty).Replace("°", string.Empty).Replace("grados ", string.Empty);
        if (Units.TryGetValue(key, out var unit) || (key.EndsWith('s') && Units.TryGetValue(key[..^1], out unit)) ||
            (key.EndsWith("es") && Units.TryGetValue(key[..^2], out unit)))
            return (Canonical(key), unit);
        throw new ToolArgumentException($"No conozco la unidad «{name}».");
    }

    private static string Canonical(string key) => key switch
    {
        "c" or "celsius" or "centigrados" => "celsius",
        "f" or "fahrenheit" => "fahrenheit",
        "k" or "kelvin" => "kelvin",
        _ => key,
    };

    private static double ToKelvin(double value, string unit) => unit switch
    {
        "celsius" => value + 273.15,
        "fahrenheit" => (value - 32) * 5 / 9 + 273.15,
        _ => value,
    };

    private static double FromKelvin(double kelvin, string unit) => unit switch
    {
        "celsius" => kelvin - 273.15,
        "fahrenheit" => (kelvin - 273.15) * 9 / 5 + 32,
        _ => kelvin,
    };

    private static Dictionary<string, (string, double)> Build()
    {
        var units = new Dictionary<string, (string, double)>();
        void Add(string dimension, double factor, params string[] names)
        {
            foreach (var name in names)
                units[name] = (dimension, factor);
        }

        Add("longitud", 1000, "km", "kilometro", "kilometros");
        Add("longitud", 1, "m", "metro", "metros");
        Add("longitud", 0.01, "cm", "centimetro", "centimetros");
        Add("longitud", 0.001, "mm", "milimetro", "milimetros");
        Add("longitud", 1609.344, "milla", "millas", "mi");
        Add("longitud", 1852, "milla nautica", "millas nauticas");
        Add("longitud", 0.3048, "pie", "pies", "ft");
        Add("longitud", 0.0254, "pulgada", "pulgadas", "in", "\"");
        Add("longitud", 0.9144, "yarda", "yardas", "yd");

        Add("peso", 1, "kg", "kilo", "kilos", "kilogramo", "kilogramos");
        Add("peso", 0.001, "g", "gramo", "gramos");
        Add("peso", 0.000001, "mg", "miligramo", "miligramos");
        Add("peso", 1000, "t", "tonelada", "toneladas");
        Add("peso", 0.45359237, "lb", "lbs", "libra", "libras");
        Add("peso", 0.028349523125, "oz", "onza", "onzas");

        Add("volumen", 1, "l", "litro", "litros");
        Add("volumen", 0.001, "ml", "mililitro", "mililitros");
        Add("volumen", 0.01, "cl", "centilitro", "centilitros");
        Add("volumen", 1000, "m3", "metro cubico", "metros cubicos");
        Add("volumen", 3.785411784, "galon", "galones", "gal");
        Add("volumen", 0.24, "taza", "tazas");

        Add("temperatura", 1, "c", "celsius", "centigrados", "f", "fahrenheit", "k", "kelvin");

        Add("velocidad", 1 / 3.6, "km/h", "kmh", "kilometros por hora");
        Add("velocidad", 1, "m/s", "metros por segundo");
        Add("velocidad", 0.44704, "mph", "millas por hora");
        Add("velocidad", 0.514444, "nudo", "nudos");

        Add("datos", 1, "b", "byte", "bytes");
        Add("datos", 0.125, "bit", "bits");
        Add("datos", 1024, "kb", "kilobyte", "kilobytes");
        Add("datos", 1024 * 1024, "mb", "megabyte", "megabytes");
        Add("datos", 1024.0 * 1024 * 1024, "gb", "gigabyte", "gigabytes");
        Add("datos", 1024.0 * 1024 * 1024 * 1024, "tb", "terabyte", "terabytes");

        Add("tiempo", 1, "s", "seg", "segundo", "segundos");
        Add("tiempo", 0.001, "ms", "milisegundo", "milisegundos");
        Add("tiempo", 60, "min", "minuto", "minutos");
        Add("tiempo", 3600, "h", "hora", "horas");
        Add("tiempo", 86400, "dia", "dias");
        Add("tiempo", 604800, "semana", "semanas");
        Add("tiempo", 2629746, "mes", "meses");
        Add("tiempo", 31556952, "año", "años", "ano", "anos");

        Add("superficie", 1, "m2", "metro cuadrado", "metros cuadrados");
        Add("superficie", 1_000_000, "km2", "kilometro cuadrado", "kilometros cuadrados");
        Add("superficie", 10_000, "ha", "hectarea", "hectareas");
        Add("superficie", 4046.8564224, "acre", "acres");

        Add("energia", 1, "j", "julio", "julios");
        Add("energia", 4.184, "cal", "caloria", "calorias");
        Add("energia", 4184, "kcal", "kilocaloria", "kilocalorias");
        Add("energia", 3_600_000, "kwh", "kilovatio hora");
        return units;
    }
}

[BuiltInTool]
public sealed class DateTimeTool(TimeProvider time) : ITool
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    private static readonly Dictionary<string, string> Cities = new()
    {
        ["madrid"] = "Europe/Madrid",
        ["valencia"] = "Europe/Madrid",
        ["barcelona"] = "Europe/Madrid",
        ["espana"] = "Europe/Madrid",
        ["canarias"] = "Atlantic/Canary",
        ["tenerife"] = "Atlantic/Canary",
        ["londres"] = "Europe/London",
        ["lisboa"] = "Europe/Lisbon",
        ["paris"] = "Europe/Paris",
        ["berlin"] = "Europe/Berlin",
        ["roma"] = "Europe/Rome",
        ["moscu"] = "Europe/Moscow",
        ["nueva york"] = "America/New_York",
        ["new york"] = "America/New_York",
        ["miami"] = "America/New_York",
        ["los angeles"] = "America/Los_Angeles",
        ["california"] = "America/Los_Angeles",
        ["chicago"] = "America/Chicago",
        ["mexico"] = "America/Mexico_City",
        ["ciudad de mexico"] = "America/Mexico_City",
        ["bogota"] = "America/Bogota",
        ["colombia"] = "America/Bogota",
        ["lima"] = "America/Lima",
        ["peru"] = "America/Lima",
        ["santiago"] = "America/Santiago",
        ["chile"] = "America/Santiago",
        ["buenos aires"] = "America/Argentina/Buenos_Aires",
        ["argentina"] = "America/Argentina/Buenos_Aires",
        ["caracas"] = "America/Caracas",
        ["sao paulo"] = "America/Sao_Paulo",
        ["brasil"] = "America/Sao_Paulo",
        ["tokio"] = "Asia/Tokyo",
        ["japon"] = "Asia/Tokyo",
        ["pekin"] = "Asia/Shanghai",
        ["china"] = "Asia/Shanghai",
        ["seul"] = "Asia/Seoul",
        ["dubai"] = "Asia/Dubai",
        ["india"] = "Asia/Kolkata",
        ["sidney"] = "Australia/Sydney",
        ["sydney"] = "Australia/Sydney",
        ["utc"] = "UTC",
    };

    public string Name => "fecha_hora";
    public string Description =>
        "Fechas y horas exactas: la hora en otra ciudad, días entre dos fechas, cuántos días faltan para una fecha, " +
        "qué día de la semana es una fecha, o sumar/restar días a una fecha.";
    public string Category => "calculos";
    public JsonObject Parameters { get; } = Object(
        Required("operacion", String("Qué calcular", "hora_en", "dias_entre", "dias_hasta", "dia_semana", "sumar_dias")),
        Optional("ciudad", String("Para hora_en: ciudad o país")),
        Optional("fecha", String("AAAA-MM-DD")),
        Optional("fecha2", String("Para dias_entre: segunda fecha AAAA-MM-DD")),
        Optional("dias", Integer("Para sumar_dias: cuántos (negativo para restar)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("Calcular fecha u hora", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow();
        DateTime Date(string name) => (arguments.OptionalDate(name) ?? throw new ToolArgumentException($"Falta «{name}» (AAAA-MM-DD).")).Date;

        var output = arguments.String("operacion") switch
        {
            "hora_en" => TimeIn(arguments.String("ciudad"), now),
            "dias_entre" => $"Entre el {Date("fecha"):dd/MM/yyyy} y el {Date("fecha2"):dd/MM/yyyy} hay {Math.Abs((Date("fecha2") - Date("fecha")).Days)} días.",
            "dias_hasta" => DaysUntil(Date("fecha"), now.Date),
            "dia_semana" => $"El {Date("fecha"):dd/MM/yyyy} es {Date("fecha").ToString("dddd", Spanish)}.",
            "sumar_dias" => SumDays(arguments.OptionalDate("fecha")?.Date ?? now.Date, arguments.Int("dias", 0)),
            _ => throw new ToolArgumentException("Operación no válida."),
        };
        return Task.FromResult(ToolResult.Ok(output));
    }

    private static string DaysUntil(DateTime date, DateTime today)
    {
        var days = (date - today).Days;
        return days switch
        {
            0 => "Es hoy.",
            > 0 => $"Faltan {days} días para el {date:dd/MM/yyyy} ({date.ToString("dddd", Spanish)}).",
            _ => $"El {date:dd/MM/yyyy} fue hace {-days} días.",
        };
    }

    private static string SumDays(DateTime date, int days)
    {
        var result = date.AddDays(days);
        return $"{date:dd/MM/yyyy} {(days >= 0 ? "+" : "-")} {Math.Abs(days)} días = {result.ToString("dddd d 'de' MMMM 'de' yyyy", Spanish)}.";
    }

    private static string TimeIn(string city, DateTimeOffset now)
    {
        var key = ToolSelector.Normalize(city).Trim();
        if (!Cities.TryGetValue(key, out var id))
            id = city.Contains('/') ? city : throw new ToolArgumentException($"No conozco la zona horaria de «{city}». Prueba con una ciudad grande del mismo país.");
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ToolArgumentException($"No encuentro la zona horaria «{id}».");
        }
        var there = TimeZoneInfo.ConvertTime(now, zone);
        var difference = (there.Offset - now.Offset).TotalHours;
        var relative = difference == 0 ? "la misma hora que aquí" : $"{Math.Abs(difference):0.#} h {(difference > 0 ? "más" : "menos")} que aquí";
        return $"En {city} son las {there:HH:mm} del {there.ToString("dddd d 'de' MMMM", Spanish)} ({relative}).";
    }
}
