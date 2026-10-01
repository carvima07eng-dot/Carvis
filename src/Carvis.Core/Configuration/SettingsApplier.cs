using System.Collections;
using System.Text.Json;

namespace Carvis.Core.Configuration;

/// <summary>
/// Copies edited settings into the live instance. Services keep references to the section
/// objects (Ollama, Assistant...), so updating them in place applies most changes at once.
/// </summary>
public static class SettingsApplier
{
    public static CarvisSettings Clone(CarvisSettings settings) =>
        JsonSerializer.Deserialize<CarvisSettings>(JsonSerializer.Serialize(settings))!;

    public static void CopyInto(CarvisSettings source, CarvisSettings target)
    {
        foreach (var property in typeof(CarvisSettings).GetProperties())
        {
            if (!property.CanWrite)
                continue;
            var sourceValue = property.GetValue(source);
            var targetValue = property.GetValue(target);
            if (sourceValue is null || targetValue is null || property.PropertyType.IsValueType || property.PropertyType == typeof(string))
            {
                property.SetValue(target, sourceValue);
                continue;
            }
            CopySection(sourceValue, targetValue);
        }
    }

    private static void CopySection(object source, object target)
    {
        foreach (var property in source.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var value = property.GetValue(source);
            if (value is IList list && property.GetValue(target) is IList targetList)
            {
                targetList.Clear();
                foreach (var item in list)
                    targetList.Add(item);
            }
            else
            {
                property.SetValue(target, value);
            }
        }
    }
}
