using System.Collections.ObjectModel;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Input;
using Carvis.Core.Ollama;
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
    private CancellationTokenSource? _sendCancellation;
    private bool _isModelLoaded;
    private string? _lastSentMessage;

    public MainWindowViewModel(
        IChatService chat,
        IOllamaHealthCheck healthCheck,
        CarvisSettings settings,
        IActionJournal journal,
        ToolPolicy policy,
        ToolConfirmationBroker confirmations)
    {
        _chat = chat;
        _healthCheck = healthCheck;
        _journal = journal;
        _policy = policy;
        ModelName = settings.Ollama.ChatModel;

        HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out var gesture);
        HotkeyText = ToDisplay(gesture);
        DismissHint = settings.Window.HideOnFocusLost ? "Esc ocultar" : "Esc minimizar";

        confirmations.Handler = ConfirmAsync;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));
    }

    /// <summary>The conversation: messages and action cards in order.</summary>
    public ObservableCollection<ChatItemViewModel> Items { get; } = [];

    public bool HasMessages => Items.Count > 0;

    /// <summary>Informational messages (bad settings, unexpected errors) the user can dismiss.</summary>
    public ObservableCollection<string> Notices { get; } = [];

    public string ModelName { get; }
    public string HotkeyText { get; }
    public string DismissHint { get; }

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

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        Input = string.Empty;
        _lastSentMessage = text;

        Items.Add(new MessageViewModel(ChatRole.User, text));
        var reply = NewReply();

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _sendCancellation = cancellation;
        var anyTool = false;

        try
        {
            await foreach (var chatEvent in _chat.SendAsync(text, cancellation.Token))
            {
                switch (chatEvent)
                {
                    case TextDelta delta:
                        reply ??= NewReply();
                        reply.Append(delta.Text);
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
            foreach (var card in Items.OfType<ToolCallViewModel>().Where(c => c.IsPending))
                card.Deny();
            PendingConfirmation = null;
            _sendCancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _sendCancellation?.Cancel();

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
    }

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

    private MessageViewModel NewReply()
    {
        var reply = new MessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Items.Add(reply);
        return reply;
    }

    // An empty bubble before a tool card adds nothing: remove it.
    private MessageViewModel? CloseReply(MessageViewModel? reply)
    {
        if (reply is null)
            return null;
        reply.IsStreaming = false;
        if (reply.Content.Length == 0)
            Items.Remove(reply);
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
