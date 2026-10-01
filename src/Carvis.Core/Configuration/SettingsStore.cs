using System.Text.Json;
using System.Text.Json.Nodes;

namespace Carvis.Core.Configuration;

/// <summary>
/// Factory defaults (appsettings.json next to the executable) overlaid with the user's own
/// settings.json. Only values that differ from the defaults are saved, so improved defaults
/// in a new version still reach the user. Lists are always replaced as a whole.
/// </summary>
public sealed class SettingsStore(string defaultsFile, string userFile)
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string UserFile { get; } = userFile;

    /// <summary>Set when a settings file was broken and had to be ignored.</summary>
    public string? LoadError { get; private set; }

    public CarvisSettings Load()
    {
        LoadError = null;
        var defaults = ReadNode(defaultsFile) ?? new JsonObject();
        var user = ReadNode(UserFile);
        var merged = user is null ? defaults : Merge(defaults, user);

        try
        {
            return merged.Deserialize<CarvisSettings>(ReadOptions) ?? new CarvisSettings();
        }
        catch (JsonException ex)
        {
            LoadError = $"La configuración tiene valores no válidos ({ex.Message}). Uso la de fábrica.";
            return LoadDefaults();
        }
    }

    public CarvisSettings LoadDefaults()
    {
        try
        {
            return ReadNode(defaultsFile)?.Deserialize<CarvisSettings>(ReadOptions) ?? new CarvisSettings();
        }
        catch (JsonException)
        {
            return new CarvisSettings();
        }
    }

    public void Save(CarvisSettings settings)
    {
        var defaults = JsonSerializer.SerializeToNode(LoadDefaults())!;
        var current = JsonSerializer.SerializeToNode(settings)!;
        var diff = Diff(defaults, current) ?? new JsonObject();

        Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
        var temp = UserFile + ".tmp";
        File.WriteAllText(temp, diff.ToJsonString(WriteOptions));
        File.Move(temp, UserFile, overwrite: true);
    }

    /// <summary>Forgets the user's changes.</summary>
    public void Reset()
    {
        if (File.Exists(UserFile))
            File.Delete(UserFile);
    }

    private JsonNode? ReadNode(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            LoadError = $"No he podido leer {path}: {ex.Message}. Lo ignoro.";
            return null;
        }
    }

    private static JsonNode Merge(JsonNode baseNode, JsonNode overlay)
    {
        if (baseNode is not JsonObject baseObject || overlay is not JsonObject overlayObject)
            return overlay.DeepClone();

        var result = (JsonObject)baseObject.DeepClone();
        foreach (var (key, value) in overlayObject)
        {
            var existingKey = result.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (value is null)
                continue;
            if (existingKey is not null && result[existingKey] is { } existing)
            {
                result[existingKey] = Merge(existing, value);
            }
            else
            {
                result[key] = value.DeepClone();
            }
        }
        return result;
    }

    private static JsonNode? Diff(JsonNode? defaults, JsonNode? current)
    {
        if (current is JsonObject currentObject && defaults is JsonObject defaultObject)
        {
            var result = new JsonObject();
            foreach (var (key, value) in currentObject)
            {
                var child = Diff(defaultObject[key], value);
                if (child is not null)
                    result[key] = child;
            }
            return result.Count > 0 ? result : null;
        }

        return JsonNode.DeepEquals(defaults, current) ? null : current?.DeepClone() ?? JsonValue.Create((string?)null);
    }
}
