using System.Runtime.Versioning;
using Carvis.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carvis.Windows;

public static class ServiceCollectionExtensions
{
    /// <summary>Replaces the portable defaults with the Windows implementations.</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddCarvisWindows(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IRecycleBin, WindowsRecycleBin>());
        services.Replace(ServiceDescriptor.Singleton<IAppCatalog, WindowsAppCatalog>());
        services.Replace(ServiceDescriptor.Singleton<IWindowManager, WindowsWindowManager>());
        services.Replace(ServiceDescriptor.Singleton<Carvis.Core.Indexing.Readers.IOcrEngine, WindowsOcrEngine>());
        services.Replace(ServiceDescriptor.Singleton<ISystemControl, WindowsSystemControl>());
        services.Replace(ServiceDescriptor.Singleton<IScriptRunner, WindowsScriptRunner>());
        return services;
    }
}
