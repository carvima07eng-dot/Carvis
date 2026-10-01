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
        services.AddSingleton(settings.Logging);
        services.TryAddSingleton(paths ?? new AppPaths());
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IOllamaApiClient>(_ => OllamaClientFactory.Create(settings.Ollama));
        services.AddSingleton<IOllamaHealthCheck, OllamaHealthCheck>();
        services.AddSingleton<IChatModelClient, OllamaChatModelClient>();
        services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
        services.AddSingleton<IModelManager, ModelManager>();
        services.AddSingleton<ITitleGenerator, TitleGenerator>();
        services.TryAddSingleton<INotifier, NullNotifier>();

        services.TryAddSingleton<IUserFolders, UserFolders>();
        services.AddSingleton(settings.Documents);
        services.AddSingleton<IDocumentReader, TextDocumentReader>();
        services.AddSingleton<IDocumentReader, PdfDocumentReader>();
        services.AddSingleton<IDocumentReader, DocxDocumentReader>();
        services.AddSingleton<IDocumentReader, XlsxDocumentReader>();
        services.AddSingleton<IDocumentReader, PptxDocumentReader>();
        services.AddSingleton<IDocumentReader, ImageDocumentReader>();
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddSingleton<DocumentIndex>();
        services.AddSingleton<DocumentIndexer>();
        services.AddSingleton<IIndexService>(sp => sp.GetRequiredService<DocumentIndexer>());
        services.AddSingleton<IChatContextProvider, RagContextProvider>();
        services.AddSingleton<IAttachmentContextBuilder, AttachmentContextBuilder>();
        services.AddSingleton<IDocumentSummarizer, DocumentSummarizer>();
        services.AddSingleton<IChatContextProvider, SystemContextProvider>();
        services.AddSingleton<IChatContextProvider, MemoryContextProvider>();

        // Storage
        services.TryAddSingleton(sp => new CarvisDatabase(sp.GetRequiredService<AppPaths>().DatabaseFile,
            sp.GetService<Microsoft.Extensions.Logging.ILogger<CarvisDatabase>>()));
        services.TryAddSingleton<IContentProtector>(_ => new ContentProtector(settings.Privacy.EncryptData));
        services.TryAddSingleton<IConversationStore, SqliteConversationStore>();
        services.TryAddSingleton<IMemoryStore, SqliteMemoryStore>();
        services.TryAddSingleton<INoteStore, SqliteNoteStore>();
        services.TryAddSingleton<ITodoStore, SqliteTodoStore>();
        services.TryAddSingleton<IReminderStore, SqliteReminderStore>();
        services.TryAddSingleton<IRoutineStore, SqliteRoutineStore>();
        services.AddSingleton<Scheduling.ReminderScheduler>();

        // Actions
        services.TryAddSingleton<IRecycleBin, TrashFolderRecycleBin>();
        services.TryAddSingleton<IShell, DefaultShell>();
        services.TryAddSingleton<IAppCatalog, DefaultAppCatalog>();
        services.TryAddSingleton<IWindowManager, DefaultWindowManager>();
        services.TryAddSingleton<ISystemControl, DefaultSystemControl>();
        services.TryAddSingleton<IScriptRunner, UnavailableScriptRunner>();
        services.TryAddSingleton<IClipboardService, MemoryClipboard>();
        services.TryAddSingleton(_ => new Tools.Internet.InternetClient());
        services.TryAddSingleton<IJournalStore, SqliteJournalStore>();
        services.AddSingleton<IActionJournal, ActionJournal>();
        services.AddSingleton<IPathPolicy, PathPolicy>();
        services.AddSingleton<ToolPolicy>();
        services.AddSingleton<ToolConfirmationBroker>();
        services.AddSingleton<IToolConfirmation>(sp => sp.GetRequiredService<ToolConfirmationBroker>());
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolSelector, ToolSelector>();
        services.AddCarvisTools();

        // Voice: the engines are replaced by Carvis.Voice and the Windows audio.
        services.AddSingleton(settings.Voice);
        services.AddSingleton(settings.Vision);
        services.TryAddSingleton<ISessionState, UnlockedSession>();
        services.TryAddSingleton<Voice.IAudioInput, Voice.NoAudioInput>();
        services.TryAddSingleton<Voice.IAudioOutput, Voice.NoAudioOutput>();
        services.TryAddSingleton<Voice.ISpeechToText, Voice.NoSpeechToText>();
        services.TryAddSingleton<Voice.ITextToSpeech, Voice.PiperTextToSpeech>();
        services.AddSingleton<Voice.VoiceModels>();
        services.AddSingleton(_ => new Voice.ModelDownloader());
        services.AddSingleton<Voice.VoiceAssistant>();
        services.TryAddSingleton<Vision.IScreenCapture, Vision.NoScreenCapture>();
        services.AddSingleton<Vision.IVisionService, Vision.OllamaVisionService>();

        services.AddSingleton<IChatService, ChatService>();
        return services;
    }
}
