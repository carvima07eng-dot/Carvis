using System.Text.Json.Nodes;

namespace Carvis.Core.Tools;

/// <summary>Small helpers to write the JSON Schema of tool arguments.</summary>
public static class Schema
{
    public static JsonObject Object(params SchemaProperty[] properties)
    {
        var props = new JsonObject();
        foreach (var property in properties)
            props[property.Name] = property.Schema;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(properties.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray()),
        };
    }

    public static SchemaProperty Required(string name, JsonObject schema) => new(name, schema, true);
    public static SchemaProperty Optional(string name, JsonObject schema) => new(name, schema, false);

    public static JsonObject String(string description, params string[] allowed)
    {
        var schema = new JsonObject { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0)
            schema["enum"] = new JsonArray(allowed.Select(a => (JsonNode)a).ToArray());
        return schema;
    }

    public static JsonObject Integer(string description) => new() { ["type"] = "integer", ["description"] = description };
    public static JsonObject Number(string description) => new() { ["type"] = "number", ["description"] = description };
    public static JsonObject Boolean(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject StringArray(string description) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = new JsonObject { ["type"] = "string" },
    };

    public static JsonObject Array(string description, JsonObject items) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = items,
    };
}

public sealed record SchemaProperty(string Name, JsonObject Schema, bool Required);
