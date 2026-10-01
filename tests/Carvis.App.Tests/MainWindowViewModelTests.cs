using Carvis.App.ViewModels;
using Carvis.App.Views;
using Carvis.Core;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Ollama;
using Carvis.Core.Storage;
using Carvis.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carvis.App.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("carvis-app").FullName;
    private readonly ScriptedModel _model = new();
    private readonly ServiceProvider _services;

    public MainWindowViewModelTests()
    {
        var paths = new AppPaths(Path.Combine(_dir, "data"), Path.Combine(_dir, "settings"));
        paths.EnsureCreated();
        var settings = new CarvisSettings();
        settings.Privacy.EncryptData = false;

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(new SettingsStore(Path.Combine(_dir, "appsettings.json"), paths.UserSettingsFile));
        services.AddCarvisCore(settings, paths);
        services.Replace(ServiceDescriptor.Singleton<IChatModelClient>(_model));
        services.Replace(ServiceDescriptor.Singleton<IOllamaHealthCheck, ReadyOllama>());
        services.Replace(ServiceDescriptor.Singleton<IModelManager, FakeModels>());
        services.Replace(ServiceDescriptor.Singleton<ITitleGenerator>(new FixedTitle()));
        services.Replace(ServiceDescriptor.Singleton<Carvis.Core.Indexing.IEmbeddingService, NoEmbeddings>());
        // A fake profile: on Windows the temp folder is inside AppData, which Carvis never touches.
        var home = Path.Combine(_dir, "home");
        services.Replace(ServiceDescriptor.Singleton<Carvis.Core.Context.IUserFolders>(new Carvis.Core.Context.UserFolders(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Perfil"] = home, ["Escritorio"] = Path.Combine(home, "Desktop") })));
        services.AddSingleton<MainWindowViewModel>();
        _services = services.BuildServiceProvider();
    }

    private MainWindowViewModel ViewModel => _services.GetRequiredService<MainWindowViewModel>();
    private IConversationStore Store => _services.GetRequiredService<IConversationStore>();

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static async Task SendAsync(MainWindowViewModel vm, string text)
    {
        vm.Input = text;
        await vm.SendCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task Answers_AreShownAndSavedAsAConversation()
    {
        _model.Answer("¡Hola Carlos!");
        var vm = ViewModel;

        await SendAsync(vm, "hola");

        Assert.Equal(["hola", "¡Hola Carlos!"], vm.Items.OfType<MessageViewModel>().Select(m => m.Content));
        var saved = Assert.Single(Store.List());
        Assert.Equal(saved.Id, vm.CurrentConversationId);
        Assert.Equal(2, Store.Messages(saved.Id).Count);
    }

    [AvaloniaFact]
    public async Task NewConversationCommand_StartsAnotherSavedConversation()
    {
        _model.Answer("uno").Answer("dos");
        var vm = ViewModel;

        await SendAsync(vm, "primera");
        await SendAsync(vm, "/nueva");
        await SendAsync(vm, "segunda");

        Assert.Equal(2, Store.List().Count);
        Assert.Equal(["segunda", "dos"], vm.Items.OfType<MessageViewModel>().Select(m => m.Content));
        Assert.Equal(2, _model.Requests.Count);
    }

    [AvaloniaFact]
    public async Task SlashCommands_AnswerLocallyWithoutTheModel()
    {
        var vm = ViewModel;

        await SendAsync(vm, "/ayuda");
        await SendAsync(vm, "/modelo");

        Assert.Empty(_model.Requests);
        Assert.Contains("/nueva", vm.Items.OfType<MessageViewModel>().First().Content);
        Assert.Contains("qwen3:14b", vm.Items.OfType<MessageViewModel>().Last().Content);
        Assert.DoesNotContain(vm.AvailableModels, m => m.Contains("embed"));
    }

    [AvaloniaFact]
    public async Task Regenerate_ReplacesTheLastAnswerEverywhere()
    {
        _model.Answer("mala").Answer("buena");
        var vm = ViewModel;
        await SendAsync(vm, "pregunta");

        Assert.True(vm.Items.OfType<MessageViewModel>().Last().CanRegenerate);
        await vm.RegenerateCommand.ExecuteAsync(null);

        Assert.Equal(["pregunta", "buena"], vm.Items.OfType<MessageViewModel>().Select(m => m.Content));
        Assert.Equal(["pregunta", "buena"], Store.Messages(vm.CurrentConversationId!).Select(m => m.Content));
    }

    [AvaloniaFact]
    public async Task EditLast_PutsTheQuestionBackInThePrompt()
    {
        _model.Answer("respuesta");
        var vm = ViewModel;
        await SendAsync(vm, "pregunta con fallo");

        vm.EditLastCommand.Execute(null);

        Assert.Equal("pregunta con fallo", vm.Input);
        Assert.Empty(vm.Items);
    }

    [AvaloniaFact]
    public async Task OpenConversation_RestoresMessagesAndContext()
    {
        _model.Answer("A").Answer("B");
        var vm = ViewModel;
        await SendAsync(vm, "uno");
        var id = vm.CurrentConversationId!;
        vm.NewConversationCommand.Execute(null);

        vm.OpenConversation(id);
        await SendAsync(vm, "dos");

        Assert.Equal(["uno", "A", "dos", "B"], vm.Items.OfType<MessageViewModel>().Select(m => m.Content));
        Assert.Contains(_model.Requests[^1].Messages, m => m.Content == "uno");
    }

    [AvaloniaFact]
    public async Task ToolCards_WaitForTheUserAndRunAfterApproval()
    {
        var folder = Path.Combine(_dir, "home", "Desktop", "Clase");
        _model.CallTool("crear_carpeta", $$"""{"ruta": {{System.Text.Json.JsonSerializer.Serialize(folder)}}}""").Answer("Creada.");
        var vm = ViewModel;

        vm.Input = "crea una carpeta llamada Clase";
        var sending = vm.SendCommand.ExecuteAsync(null);
        for (var i = 0; i < 500 && vm.PendingConfirmation is null; i++)
            await Task.Delay(20);

        var card = Assert.IsType<ToolCallViewModel>(vm.PendingConfirmation);
        Assert.False(Directory.Exists(folder));
        card.Approve();
        await sending;

        Assert.True(Directory.Exists(folder));
        Assert.Equal(ToolCallState.Succeeded, card.State);
        Assert.True(card.CanUndo);
        await card.UndoCommand.ExecuteAsync(null);
        Assert.False(Directory.Exists(folder));
    }

    [AvaloniaFact]
    public void MainWindow_BuildsWithTheViewModel()
    {
        var window = new MainWindow(null, new WindowSettings()) { DataContext = ViewModel };
        window.Show();
        Assert.True(window.IsVisible);
        window.AllowClose = true;
        window.Close();
    }

    [AvaloniaFact]
    public void SettingsWindow_BuildsWithAllTabs()
    {
        var viewModel = ActivatorUtilities.CreateInstance<SettingsViewModel>(_services);
        viewModel.AttachVoice(_services.GetRequiredService<Carvis.Core.Voice.VoiceModels>(), _services.GetRequiredService<Carvis.Core.Voice.ModelDownloader>(),
            _services.GetRequiredService<Carvis.Core.Voice.VoiceAssistant>(), _services.GetRequiredService<Carvis.Core.Voice.IAudioInput>(),
            _services.GetRequiredService<Carvis.Core.Voice.IAudioOutput>());
        var window = new SettingsWindow { DataContext = viewModel };
        window.Show();
        Assert.True(window.IsVisible);
        Assert.False(string.IsNullOrEmpty(viewModel.VoiceStatus));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Images_GoToTheVisionModelOnlyOnce()
    {
        _model.Answer("Veo un error de compilación.").Answer("De nada.");
        var vm = ViewModel;
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(2000, 1000), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        vm.AttachImage(ImageAttachmentViewModel.Create(bitmap, "Captura"));
        Assert.True(vm.SendCommand.CanExecute(null)); // an image alone can be sent

        await vm.SendCommand.ExecuteAsync(null);
        await SendAsync(vm, "gracias");

        var first = _model.Requests[0];
        Assert.Equal("qwen2.5vl:7b", first.Model);
        var png = Assert.Single(first.Messages.Last(m => m.Role == ChatRole.User).Images!);
        using (var decoded = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png)))
            Assert.Equal(1600, decoded.PixelSize.Width); // scaled down
        Assert.Null(_model.Requests[1].Model);
        Assert.All(_model.Requests[1].Messages, m => Assert.Null(m.Images));
        Assert.Empty(vm.Images);
        Assert.Equal("🖼 1 imagen", vm.Items.OfType<MessageViewModel>().First().AttachmentsText);
    }

    [AvaloniaFact]
    public void Voice_IsOffUntilEnabled()
    {
        var vm = ViewModel;
        Assert.False(vm.IsVoiceEnabled);
        vm.ToggleVoice();
        Assert.Contains(vm.Notices, n => n.Contains("Ajustes → Voz"));
    }

    private sealed class FixedTitle : ITitleGenerator
    {
        public Task<string> GenerateAsync(string userMessage, string answer, CancellationToken cancellationToken = default) => Task.FromResult("Título");
    }
}
