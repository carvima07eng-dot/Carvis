using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Context;
using Carvis.Core.Ollama;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace Carvis.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the core services. Later phases (indexing, tools, voice...) add theirs here.</summary>
    public static IServiceCollection AddCarvisCore(this IServiceCollection services, CarvisSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.Ollama);
        services.AddSingleton(settings.Assistant);

        services.AddSingleton<IOllamaApiClient>(_ => OllamaClientFactory.Create(settings.Ollama));
        services.AddSingleton<IOllamaHealthCheck, OllamaHealthCheck>();
        services.AddSingleton<IChatModelClient, OllamaChatModelClient>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUserFolders, UserFolders>();
        services.AddSingleton<IChatContextProvider, SystemContextProvider>();
        services.AddSingleton<IChatService, ChatService>();

        return services;
    }
}
