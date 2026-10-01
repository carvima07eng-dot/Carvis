using System.Collections.ObjectModel;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Input;
using Carvis.Core.Ollama;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IChatService _chat;
    private readonly IOllamaHealthCheck _healthCheck;
    private CancellationTokenSource? _sendCancellation;

    public MainWindowViewModel(IChatService chat, IOllamaHealthCheck healthCheck, CarvisSettings settings)
    {
        _chat = chat;
        _healthCheck = healthCheck;
        ModelName = settings.Ollama.ChatModel;

        HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out var gesture);
        HotkeyText = ToDisplay(gesture);

        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public bool HasMessages => Messages.Count > 0;

    public string ModelName { get; }
    public string HotkeyText { get; }

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

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public bool HasStatusCommand => !string.IsNullOrEmpty(StatusCommand);
    public bool HasHotkeyWarning => !string.IsNullOrEmpty(HotkeyWarning);

    public bool IsOllamaReady => ConnectionState == OllamaState.Ready;

    public string ConnectionText => IsCheckingStatus
        ? "Comprobando…"
        : ConnectionState switch
        {
            OllamaState.Ready => "Conectado",
            OllamaState.ModelMissing => "Falta el modelo",
            _ => "Sin conexión",
        };

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        Input = string.Empty;

        Messages.Add(new MessageViewModel(ChatRole.User, text));
        var reply = new MessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Messages.Add(reply);

        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _sendCancellation = cancellation;

        try
        {
            await foreach (var chunk in _chat.SendAsync(text, cancellation.Token))
                reply.Append(chunk);

            if (reply.Content.Length == 0)
                reply.Content = "(Sin respuesta)";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (reply.Content.Length == 0)
                reply.Content = "(Respuesta cancelada)";
        }
        catch (Exception ex)
        {
            reply.IsError = true;
            reply.Content = DescribeError(ex);
            _ = CheckStatusAsync();
        }
        finally
        {
            reply.IsStreaming = false;
            _sendCancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _sendCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanStartNewConversation))]
    private void NewConversation()
    {
        _chat.ClearHistory();
        Messages.Clear();
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
