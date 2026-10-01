using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carvis.Core.Tools;

/// <summary>
/// Loads extra tools from DLLs in %AppData%\Carvis\plugins (off by default: a plugin runs with
/// the user's permissions). Any public, non-abstract ITool is registered; its constructor can
/// ask for Carvis services.
/// </summary>
public static class PluginLoader
{
    public static IReadOnlyList<string> LoadInto(IToolRegistry registry, IServiceProvider services, string directory, ILogger logger)
    {
        var loaded = new List<string>();
        if (!Directory.Exists(directory))
            return loaded;

        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            try
            {
                var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(file), isCollectible: false);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(file));
                foreach (var type in assembly.GetExportedTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITool).IsAssignableFrom(t)))
                {
                    var tool = (ITool)ActivatorUtilities.CreateInstance(services, type);
                    registry.Add(tool);
                    loaded.Add($"{tool.Name} ({Path.GetFileName(file)})");
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException or InvalidOperationException or MissingMethodException)
            {
                logger.LogWarning(ex, "Plugin {File} could not be loaded", file);
            }
        }

        logger.LogInformation("Plugins loaded: {Tools}", loaded.Count == 0 ? "none" : string.Join(", ", loaded));
        return loaded;
    }
}
