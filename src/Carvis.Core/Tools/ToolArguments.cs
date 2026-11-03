using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Carvis.Core.Tools;

/// <summary>
/// Typed access to the arguments the model sent. Lenient with what small models usually get
/// wrong (numbers as strings, a single string instead of a list...), strict with what's missing.
/// </summary>
public sealed class ToolArguments(JsonObject json)
{
    public JsonObject Json { get; } = json;

    public string String(string name) =>
        OptionalString(name) is { Length: > 0 } value ? value : throw new ToolArgumentException($"Falta el argumento obligatorio «{name}».");

    public string? OptionalString(string name)
    {
        var node = Json[name];
        if (node is null)
            return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return text.Trim();
        return node.ToJsonString().Trim('"');
    }

    public bool Bool(string name, bool fallback = false)
    {
        var node = Json[name];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b))
                return b;
            if (value.TryGetValue<string>(out var s))
                return s.Trim().ToLowerInvariant() is "true" or "sí" or "si" or "yes" or "1";
        }
        return fallback;
    }

    public int Int(string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        var number = OptionalNumber(name);
        return number is null ? fallback : (int)Math.Clamp(Math.Round(number.Value), min, max);
    }

    public double? OptionalNumber(string name)
    {
        var node = Json[name];
        if (node is not JsonValue value)
            return null;
        // A value built in code from an int doesn't convert to double with TryGetValue.
        if (value.GetValueKind() == JsonValueKind.Number &&
            double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        if (value.TryGetValue<string>(out var s) &&
            double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    public double Number(string name) =>
        OptionalNumber(name) ?? throw new ToolArgumentException($"Falta el número «{name}» o no es válido.");

    public IReadOnlyList<string> StringList(string name, bool required = true)
    {
        var node = Json[name];
        var list = node switch
        {
            JsonArray array => array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToJsonString())
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList(),
            JsonValue value when value.TryGetValue<string>(out var text) => ParseLooseList(text),
            _ => [],
        };

        if (required && list.Count == 0)
            throw new ToolArgumentException($"Falta la lista «{name}».");
        return list;
    }

    public DateTimeOffset? OptionalDate(string name)
    {
        var text = OptionalString(name);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ||
            DateTimeOffset.TryParse(text, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.AssumeLocal, out date))
            return date;
        throw new ToolArgumentException($"La fecha «{text}» no es válida; usa el formato AAAA-MM-DD o AAAA-MM-DDTHH:MM.");
    }

    public T Enum<T>(string name, T fallback) where T : struct, System.Enum
    {
        var text = OptionalString(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        return System.Enum.TryParse<T>(text, ignoreCase: true, out var value)
            ? value
            : throw new ToolArgumentException($"«{text}» no es un valor válido para «{name}». Opciones: {string.Join(", ", System.Enum.GetNames<T>())}.");
    }

    // Models sometimes send '["a","b"]' as a string, or "a, b".
    private static List<string> ParseLooseList(string text)
    {
        text = text.Trim();
        if (text.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(text)?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
            }
            catch (JsonException)
            {
            }
        }
        return text.Length == 0 ? [] : [text];
    }
}
