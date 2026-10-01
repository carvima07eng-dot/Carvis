using System.Collections.ObjectModel;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Input;
using Carvis.Core.Ollama;
using Carvis.Core.Storage;
using Carvis.Core.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IChatService _chat;
    private readonly IOllamaHealthCheck _healthCheck;
    private readonly IActionJournal _journal;
    private readonly ToolPolicy _policy;
    private readonly IConversationStore _conversations;
    private readonly ITitleGenerator _titles;
    private readonly IModelManager _models;
    private readonly IMemoryStore _memories;
    private readonly SettingsStore _settingsStore;
    private readonly CarvisSettings _settings;
    private readonly Carvis.Core.Platform.IShell _shell;
    private CancellationTokenSource? _sendCancellation;
    private bool _isModelLoaded;
    private string? _lastSentMessage;

    public MainWindowViewModel(
        IChatService chat,
        IOllamaHealthCheck healthCheck,
        CarvisSettings settings,
        IActionJournal journal,
        ToolPolicy policy,
        ToolConfirmationBroker confirmations,
        IConversationStore conversations,
        ITitleGenerator titles,
        IModelManager models,
        IMemoryStore memories,
        SettingsStore settingsStore,
        Carvis.Core.Platform.IShell shell,
        Carvis.Core.Indexing.IIndexService index,
        Carvis.Core.Voice.VoiceAssistant? voice = null)
    {
        _shell = shell;
        _voice = voice;
        AttachVoice();
        IndexStatusText = DescribeIndex(index.Status);
        index.StatusChanged += status => Avalonia.Threading.Dispatcher.UIThread.Post(() => IndexStatusText = DescribeIndex(status));
        _chat = chat;
        _healthCheck = healthCheck;
        _journal = journal;
        _policy = policy;
        _conversations = conversations;
        _titles = titles;
        _models = models;
        _memories = memories;
        _settingsStore = settingsStore;
        _settings = settings;
        _modelName = settings.Ollama.ChatModel;

        History = new ConversationListViewModel(conversations);
        History.OpenRequested += item => OpenConversation(item.Id);
        History.ExportRequested += item => Export(item.Id);
        History.Deleted += id =>
        {
            if (id == CurrentConversationId)
                NewConversation();
        };
        _chat.TurnCommitted += OnTurnCommitted;
        _chat.SummaryUpdated += summary =>
        {
            if (CurrentConversationId is not null)
                _conversations.SetSummary(CurrentConversationId, summary);
        };

        HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out var gesture);
        HotkeyText = ToDisplay(gesture);
        DismissHint = settings.Window.HideOnFocusLost ? "Esc ocultar" : "Esc minimizar";

        confirmations.Handler = ConfirmAsync;
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(ShowConversation));
        };
    }

    /// <summary>The conversation: messages and action cards in order.</summary>
    public ObservableCollection<ChatItemViewModel> Items { get; } = [];

    public bool HasMessages => Items.Count > 0;

    /// <summary>Informational messages (bad settings, unexpected errors) the user can dismiss.</summary>
    public ObservableCollection<string> Notices { get; } = [];

    public ConversationListViewModel History { get; }
    public ObservableCollection<string> AvailableModels { get; } = [];

    public string HotkeyText { get; }
    public string DismissHint { get; }

    /// <summary>The window asked to open the settings.</summary>
    public event Action? SettingsRequested;

    /// <summary>An answer finished; the app shows a notification if the window is hidden.</summary>
    public event Action<string>? AnswerCompleted;

    [ObservableProperty]
    private string _modelName;

    /// <summary>The saved conversation shown, or null for a new one not saved yet.</summary>
    [ObservableProperty]
    private string? _currentConversationId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConversation))]
    private bool _isHistoryOpen;

    public bool ShowConversation => HasMessages && !IsHistoryOpen;

    /// <summary>Files to send with the next message (dropped on the window or chosen with 📎).</summary>
    public ObservableCollection<string> Attachments { get; } = [];

    /// <summary>"Indexando 3/40…" while documents are being read; empty otherwise.</summary>
    [ObservableProperty]
    private string? _indexStatusText;

    /// <summary>Speed of the last answer, e.g. "42 tok/s".</summary>
    [ObservableProperty]
    private string? _lastSpeed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewConversationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOllamaReady))]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    private OllamaState? _connectionState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    private bool _isCheckingStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    private bool _isLoadingModel;

    /// <summary>Problem shown in the banner, or null when everything is fine.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    /// <summary>Command the user can run to fix the problem, e.g. "ollama pull qwen3:8b".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusCommand))]
    private string? _statusCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkeyWarning))]
    private string? _hotkeyWarning;

    /// <summary>The action waiting for the user's answer, if any (Enter accepts, Esc cancels).</summary>
    [ObservableProperty]
    private ToolCallViewModel? _pendingConfirmation;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public bool HasStatusCommand => !string.IsNullOrEmpty(StatusCommand);
    public bool HasHotkeyWarning => !string.IsNullOrEmpty(HotkeyWarning);

    public bool IsOllamaReady => ConnectionState == OllamaState.Ready;

    public string ConnectionText => IsCheckingStatus
        ? "Comprobando…"
        : ConnectionState switch
        {
            OllamaState.Ready when IsLoadingModel => "Cargando modelo…",
            OllamaState.Ready => "Conectado",
            OllamaState.ModelMissing => "Falta el modelo",
            _ => "Sin conexión",
        };

    public void AddNotice(string notice)
    {
        if (!Notices.Contains(notice))
            Notices.Add(notice);
    }

    [RelayCommand]
    private void DismissNotice(string notice) => Notices.Remove(notice);

    private bool CanSend() => !IsBusy && (!string.IsNullOrWhiteSpace(Input) || Images.Count > 0);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0)
            text = "¿Qué ves en esta imagen?";
        Input = string.Empty;
        _lastSentMessage = text;

        if (text.StartsWith('/') && await RunCommandAsync(text))
            return;

        var attachments = Attachments.ToList();
        Attachments.Clear();
        var images = Images.ToList();
        Images.Clear();
        await AskAsync(text, attachments, images);
    }

    /// <summary>Images for the next message (captures, pasted or dropped). Never written to disk.</summary>
    public ObservableCollection<ImageAttachmentViewModel> Images { get; } = [];

    public void AttachImage(ImageAttachmentViewModel image)
    {
        if (Images.Count >= 4)
            Images.RemoveAt(0);
        Images.Add(image);
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveImage(ImageAttachmentViewModel image)
    {
        Images.Remove(image);
        SendCommand.NotifyCanExecuteChanged();
    }

    public void AttachFile(string path)
    {
        if (ImageAttachmentViewModel.IsImageFile(path) && File.Exists(path) && new FileInfo(path).Length < 30_000_000)
        {
            try
            {
                AttachImage(ImageAttachmentViewModel.Create(File.ReadAllBytes(path), Path.GetFileName(path)));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                // Not an image Avalonia can read: send it as a document (OCR) instead.
            }
        }
        if (File.Exists(path) && !Attachments.Contains(path, StringComparer.OrdinalIgnoreCase))
            Attachments.Add(path);
    }

    [RelayCommand]
    private void RemoveAttachment(string path) => Attachments.Remove(path);

    [RelayCommand]
    private Task OpenSourceAsync(SourceReference source) => _shell.OpenAsync(source.Path);

    private static string? DescribeIndex(Carvis.Core.Indexing.IndexStatus status) =>
        status.IsRunning ? status.Describe() : null;

    private async Task AskAsync(string text, IReadOnlyList<string>? attachments = null, IReadOnlyList<ImageAttachmentViewModel>? images = null)
    {
        Items.Add(new MessageViewModel(ChatRole.User, text) { Attachments = attachments ?? [], ImageCount = images?.Count ?? 0 });
        var input = new ChatInput(text) { Attachments = attachments ?? [], Images = images?.Select(i => i.Png).ToList() ?? [] };
        await RunTurnAsync(token => _chat.SendAsync(input, token), expectAnswer: true);
    }

    /// <summary>Runs a routine's steps without the model (a scheduled routine, for example).</summary>
    public async Task RunToolsAsync(string description, IReadOnlyList<ToolCall> calls)
    {
        // A scheduled routine waits for the answer in progress to finish.
        while (IsBusy)
            await Task.Delay(500);
        Items.Add(new MessageViewModel(ChatRole.User, description));
        await RunTurnAsync(token => _chat.RunToolsAsync(description, calls, token), expectAnswer: false);
    }

    private async Task RunTurnAsync(Func<CancellationToken, IAsyncEnumerable<ChatEvent>> run, bool expectAnswer)
    {
        var reply = expectAnswer ? NewReply() : null;

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _sendCancellation = cancellation;
        var anyTool = false;

        try
        {
            await foreach (var chatEvent in run(cancellation.Token))
            {
                switch (chatEvent)
                {
                    case SourcesAttached attached:
                        reply ??= NewReply();
                        foreach (var source in attached.Sources)
                            reply.Sources.Add(source);
                        break;

                    case TextDelta delta:
                        reply ??= NewReply();
                        reply.Append(delta.Text);
                        _speech?.Push(delta.Text);
                        break;

                    case ThinkingDelta thinking:
                        reply ??= NewReply();
                        reply.Thinking += thinking.Text;
                        break;

                    case StatsReported stats:
                        LastSpeed = $"{stats.Stats.TokensPerSecond:0} tok/s";
                        break;

                    case ToolStarted started:
                        anyTool = true;
                        reply = CloseReply(reply);
                        Items.Add(new ToolCallViewModel(started.Invocation, _policy.CanApproveForSession(started.Invocation.Preview), UndoAsync));
                        break;

                    case ToolFinished finished:
                        if (FindCard(finished.Invocation.Id) is { } card)
                        {
                            var entry = finished.Result.JournalEntryId is { } id ? _journal.Find(id) : null;
                            card.Complete(finished.Result, entry?.CanUndo == true);
                        }
                        break;

                    case StepCompleted:
                        // The model continues after the tool results: show that it's thinking again.
                        reply ??= NewReply();
                        break;
                }
            }

            if (reply is { Content.Length: 0 } && !anyTool)
                reply.Content = "(Sin respuesta)";
            if (expectAnswer)
                AnswerCompleted?.Invoke(Items.OfType<MessageViewModel>().LastOrDefault(m => !m.IsUser)?.Content ?? string.Empty);
            _ = CheckGpuAsync(report: false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (reply is { Content.Length: 0 })
                reply.Content = "(Respuesta cancelada)";
        }
        catch (Exception ex)
        {
            reply ??= NewReply();
            reply.IsError = true;
            reply.Content = DescribeError(ex);
            _ = CheckStatusAsync();
        }
        finally
        {
            CloseReply(reply);
            _ = _speech?.CompleteAsync();
            _speech = null;
            foreach (var card in Items.OfType<ToolCallViewModel>().Where(c => c.IsPending))
                card.Deny();
            PendingConfirmation = null;
            _sendCancellation = null;
            IsBusy = false;
            UpdateLastMessageFlags();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _sendCancellation?.Cancel();
        _voice?.StopSpeaking();
    }

    /// <summary>Puts the last message back in the prompt (arrow up on an empty prompt).</summary>
    public bool RecallLastMessage()
    {
        if (!string.IsNullOrEmpty(Input) || _lastSentMessage is null)
            return false;

        Input = _lastSentMessage;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanStartNewConversation))]
    private void NewConversation()
    {
        _chat.ClearHistory();
        Items.Clear();
        CurrentConversationId = null;
        History.CurrentId = null;
        LastSpeed = null;
    }

    [RelayCommand]
    private void ToggleHistory()
    {
        IsHistoryOpen = !IsHistoryOpen;
        if (IsHistoryOpen)
            History.Refresh();
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    public void OpenConversation(string id)
    {
        if (IsBusy || _conversations.Find(id) is null)
            return;

        var messages = _conversations.Messages(id);
        _chat.LoadHistory(messages, _conversations.Summary(id));
        CurrentConversationId = id;
        History.CurrentId = id;
        IsHistoryOpen = false;

        Items.Clear();
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case ChatRole.User:
                    Items.Add(new MessageViewModel(ChatRole.User, message.Content));
                    break;
                case ChatRole.Assistant when message.Content.Length > 0:
                    Items.Add(new MessageViewModel(ChatRole.Assistant, message.Content));
                    break;
                case ChatRole.Tool:
                    Items.Add(new HistoryToolViewModel(message.ToolName ?? "herramienta", message.Content));
                    break;
            }
        }
        UpdateLastMessageFlags();
    }

    /// <summary>Asks again the last question, discarding the last answer.</summary>
    [RelayCommand]
    private async Task RegenerateAsync()
    {
        var text = TakeBackLastTurn();
        if (text is not null)
            await AskAsync(text);
    }

    /// <summary>Puts the last question back in the prompt to change it.</summary>
    [RelayCommand]
    private void EditLast()
    {
        var text = TakeBackLastTurn();
        if (text is not null)
            Input = text;
    }

    private string? TakeBackLastTurn()
    {
        if (IsBusy)
            return null;
        var lastUser = Items.OfType<MessageViewModel>().LastOrDefault(m => m.IsUser);
        if (lastUser is null)
            return null;

        _chat.RemoveLastTurn();
        if (CurrentConversationId is not null)
            _conversations.RemoveLastTurn(CurrentConversationId);

        var index = Items.IndexOf(lastUser);
        while (Items.Count > index)
            Items.RemoveAt(Items.Count - 1);
        UpdateLastMessageFlags();
        return lastUser.Content;
    }

    private void UpdateLastMessageFlags()
    {
        var messages = Items.OfType<MessageViewModel>().ToList();
        foreach (var message in messages)
        {
            message.CanRegenerate = false;
            message.CanEdit = false;
        }
        if (IsBusy)
            return;

        var lastUser = messages.LastOrDefault(m => m.IsUser);
        if (lastUser is null)
            return;
        lastUser.CanEdit = true;
        if (messages.LastOrDefault() is { IsUser: false } lastAnswer)
            lastAnswer.CanRegenerate = true;
    }

    private void OnTurnCommitted(IReadOnlyList<ChatMessage> turn)
    {
        if (!_settings.Privacy.SaveConversations)
            return;

        var question = turn.FirstOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;
        var isNew = CurrentConversationId is null;
        if (isNew)
        {
            CurrentConversationId = _conversations.Create(TitleGenerator.Fallback(question)).Id;
            History.CurrentId = CurrentConversationId;
        }
        _conversations.Append(CurrentConversationId!, turn);

        if (isNew)
        {
            var id = CurrentConversationId!;
            var answer = turn.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Content ?? string.Empty;
            _ = Task.Run(async () =>
            {
                var title = await _titles.GenerateAsync(question, answer);
                _conversations.Rename(id, title);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (IsHistoryOpen)
                        History.Refresh();
                });
            });
        }
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await _models.ListAsync();
            AvailableModels.Clear();
            foreach (var model in models.Where(m => !m.Name.Contains("embed", StringComparison.OrdinalIgnoreCase)))
                AvailableModels.Add(model.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AvailableModels.Clear();
        }
    }

    [RelayCommand]
    private async Task SelectModelAsync(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model == _settings.Ollama.ChatModel)
            return;
        _settings.Ollama.ChatModel = model;
        _settingsStore.Save(_settings);
        ModelName = model;
        _isModelLoaded = false;
        await CheckStatusAsync();
    }

    /// <summary>Settings were changed in the settings window.</summary>
    public void OnSettingsApplied()
    {
        ModelName = _settings.Ollama.ChatModel;
        _isModelLoaded = false;
        OnVoiceSettingsApplied();
        _ = CheckStatusAsync();
    }

    private async Task<bool> RunCommandAsync(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        var argument = parts.Length > 1 ? parts[1] : string.Empty;

        switch (parts[0].ToLowerInvariant())
        {
            case "/nueva":
                NewConversation();
                return true;
            case "/historial":
                IsHistoryOpen = true;
                History.Refresh();
                return true;
            case "/buscar":
                IsHistoryOpen = true;
                History.Search = argument;
                return true;
            case "/ajustes":
                SettingsRequested?.Invoke();
                return true;
            case "/modelo" when argument.Length > 0:
                await SelectModelAsync(argument);
                Say($"Ahora uso el modelo **{argument}**.");
                return true;
            case "/modelo":
                await LoadModelsAsync();
                Say("Modelos instalados:\n" + string.Join("\n", AvailableModels.Select(m => $"- {m}{(m == ModelName ? " (actual)" : string.Empty)}")) +
                    "\n\nCambia con `/modelo nombre`.");
                return true;
            case "/memoria":
                var memories = _memories.All();
                Say(memories.Count == 0 ? "No tengo nada guardado sobre ti." : "Lo que recuerdo de ti:\n" + string.Join("\n", memories.Select(m => $"- {m.Text}")));
                return true;
            case "/olvidar" when argument.Equals("todo", StringComparison.OrdinalIgnoreCase):
                _memories.Clear();
                Say("He borrado todo lo que recordaba de ti.");
                return true;
            case "/acciones":
                var entries = _journal.Recent(10);
                Say(entries.Count == 0 ? "Todavía no he hecho ninguna acción." :
                    "Últimas acciones:\n" + string.Join("\n", entries.Select(e => $"- {e.Time:dd/MM HH:mm} · {e.Summary}{(e.Undone ? " (deshecha)" : e.Success ? string.Empty : " (falló)")}")));
                return true;
            case "/exportar":
                if (CurrentConversationId is { } current)
                    Export(current);
                else
                    Say("Todavía no hay nada que exportar en esta conversación.");
                return true;
            case "/gpu":
                await CheckGpuAsync(report: true);
                return true;
            case "/liberar":
                await FreeMemoryAsync();
                return true;
            case "/ayuda":
            case "/olvidar":
                Say("""
                    Comandos:
                    - `/nueva`: empezar una conversación nueva
                    - `/historial` y `/buscar texto`: conversaciones guardadas
                    - `/modelo` y `/modelo nombre`: ver o cambiar el modelo
                    - `/memoria`: lo que recuerdo de ti · `/olvidar todo`: borrarlo
                    - `/acciones`: lo último que he hecho en el PC
                    - `/exportar`: guardar esta conversación en Markdown
                    - `/gpu`: qué modelos hay en la tarjeta gráfica · `/liberar`: sacarlos de la memoria
                    - `/ajustes`: abrir los ajustes

                    Teclado: Ctrl+N nueva · Ctrl+H historial · Ctrl+, ajustes · Ctrl+M hablar · Ctrl+V pega también imágenes · Esc cancela u oculta
                    """);
                return true;
            default:
                return false;
        }
    }

    private void Export(string id)
    {
        if (_conversations.Find(id) is not { } info)
            return;
        try
        {
            var name = string.IsNullOrWhiteSpace(_settings.Assistant.UserName) ? "Yo" : _settings.Assistant.UserName;
            var path = ConversationExporter.Save(info, _conversations.Messages(id), userName: name);
            IsHistoryOpen = false;
            Say($"He guardado «{info.Title}» en `{path}`.");
            _ = _shell.RevealAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddNotice($"No he podido exportar la conversación: {ex.Message}");
        }
    }

    // Local answers to commands: shown but not sent to the model.
    private void Say(string markdown) => Items.Add(new MessageViewModel(ChatRole.Assistant, markdown));

    private bool CanStartNewConversation() => !IsBusy;

    [RelayCommand]
    public async Task CheckStatusAsync()
    {
        if (IsCheckingStatus)
            return;

        IsCheckingStatus = true;
        try
        {
            var status = await _healthCheck.CheckAsync();
            ConnectionState = status.State;
            (StatusMessage, StatusCommand) = status.State switch
            {
                OllamaState.ServerUnavailable => (
                    $"No encuentro Ollama en {status.BaseUrl}. Comprueba que está instalado y abierto.",
                    "ollama serve"),
                OllamaState.ModelMissing => (
                    $"El modelo {status.Model} no está descargado. Descárgalo con:",
                    $"ollama pull {status.Model}"),
                _ => ((string?)null, (string?)null),
            };
        }
        finally
        {
            IsCheckingStatus = false;
        }

        if (IsOllamaReady)
            await LoadModelAsync();
    }

    private List<SourceReference> _carriedSources = [];

    private MessageViewModel NewReply()
    {
        var reply = new MessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        foreach (var source in _carriedSources)
            reply.Sources.Add(source);
        _carriedSources = [];
        Items.Add(reply);
        return reply;
    }

    // An empty bubble before a tool card adds nothing: remove it (its sources go to the next one).
    private MessageViewModel? CloseReply(MessageViewModel? reply)
    {
        if (reply is null)
            return null;
        reply.IsStreaming = false;
        if (reply.Content.Length == 0)
        {
            _carriedSources = reply.Sources.ToList();
            Items.Remove(reply);
        }
        return null;
    }

    private ToolCallViewModel? FindCard(string id) =>
        Items.OfType<ToolCallViewModel>().LastOrDefault(c => c.Invocation.Id == id);

    private async Task<ConfirmationDecision> ConfirmAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var card = FindCard(invocation.Id);
        if (card is null)
            return ConfirmationDecision.Deny;

        PendingConfirmation = card;
        try
        {
            return await card.WaitForDecisionAsync(cancellationToken);
        }
        finally
        {
            PendingConfirmation = null;
        }
    }

    private Task<string> UndoAsync(string journalEntryId) => _journal.UndoAsync(journalEntryId);

    // Loading qwen3:8b into the GPU takes a few seconds; do it before the first question.
    private async Task LoadModelAsync()
    {
        if (_isModelLoaded || IsLoadingModel)
            return;

        IsLoadingModel = true;
        try
        {
            await _chat.WarmUpAsync();
            _isModelLoaded = true;
            _ = CheckGpuAsync(report: false);
        }
        catch (Exception)
        {
            // Not critical: the first question will load the model instead.
        }
        finally
        {
            IsLoadingModel = false;
        }
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        HttpRequestException => "No he podido conectar con Ollama. ¿Está abierto?",
        OperationCanceledException => "Ollama ha tardado demasiado en responder.",
        _ => $"Ollama ha devuelto un error: {ex.Message}",
    };

    private static string ToDisplay(HotkeyGesture gesture) =>
        gesture.ToString().Replace("Space", "Espacio");
}
