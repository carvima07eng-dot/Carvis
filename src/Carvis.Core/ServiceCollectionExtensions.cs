using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Context;
using Carvis.Core.Indexing;
using Carvis.Core.Indexing.Readers;
using Carvis.Core.Ollama;
using Carvis.Core.Platform;
using Carvis.Core.Storage;
using Carvis.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OllamaSharp;

namespace Carvis.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the core services. Platform projects replace the defaults with TryAdd/Replace.</summary>
    public static IServiceCollection AddCarvisCore(this IServiceCollection services, CarvisSettings settings, AppPaths? paths = null)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.Ollama);
        services.AddSingleton(settings.Assistant);
        services.AddSingleton(settings.Permissions);
        services.TryAddSingleton(paths ?? new AppPaths());
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IOllamaApiClient>(_ => OllamaClientFactory.Create(settings.Ollama));
        services.AddSingleton<IOllamaHealthCheck, OllamaHealthCheck>();
        services.AddSingleton<IChatModelClient, OllamaChatModelClient>();
        services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();

        services.TryAddSingleton<IUserFolders, UserFolders>();
        services.AddSingleton<IDocumentReader, TextDocumentReader>();
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddSingleton<IChatContextProvider, SystemContextProvider>();
        services.AddSingleton<IChatContextProvider, MemoryContextProvider>();

        // Storage
        services.TryAddSingleton(sp => new CarvisDatabase(sp.GetRequiredService<AppPaths>().DatabaseFile,
            sp.GetService<Microsoft.Extensions.Logging.ILogger<CarvisDatabase>>()));
        services.TryAddSingleton<IContentProtector>(_ => new ContentProtector(settings.Privacy.EncryptData));
        services.TryAddSingleton<IConversationStore, SqliteConversationStore>();
        services.TryAddSingleton<IMemoryStore, SqliteMemoryStore>();

        // Actions
        services.TryAddSingleton<IRecycleBin, TrashFolderRecycleBin>();
        services.TryAddSingleton<IShell, DefaultShell>();
        services.TryAddSingleton<IAppCatalog, DefaultAppCatalog>();
        services.TryAddSingleton<IWindowManager, DefaultWindowManager>();
        services.TryAddSingleton<IJournalStore, SqliteJournalStore>();
        services.AddSingleton<IActionJournal, ActionJournal>();
        services.AddSingleton<IPathPolicy, PathPolicy>();
        services.AddSingleton<ToolPolicy>();
        services.AddSingleton<ToolConfirmationBroker>();
        services.AddSingleton<IToolConfirmation>(sp => sp.GetRequiredService<ToolConfirmationBroker>());
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolSelector, ToolSelector>();
        services.AddCarvisTools();

        services.AddSingleton<IChatService, ChatService>();
        return services;
    }
}
