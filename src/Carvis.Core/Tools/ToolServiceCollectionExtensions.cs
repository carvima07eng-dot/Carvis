using Microsoft.Extensions.DependencyInjection;

namespace Carvis.Core.Tools;

public static class ToolServiceCollectionExtensions
{
    /// <summary>Registers every built-in tool that lives in the core (platform tools are added elsewhere).</summary>
    public static IServiceCollection AddCarvisTools(this IServiceCollection services)
    {
        foreach (var type in typeof(ToolServiceCollectionExtensions).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITool).IsAssignableFrom(t)
                                 && t.GetCustomAttributes(typeof(BuiltInToolAttribute), false).Length > 0))
        {
            services.AddSingleton(typeof(ITool), type);
        }
        return services;
    }
}

/// <summary>Marks a tool class to be registered automatically.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BuiltInToolAttribute : Attribute;
