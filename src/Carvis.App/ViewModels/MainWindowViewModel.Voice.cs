using Avalonia.Threading;
using Carvis.Core.Voice;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly VoiceAssistant? _voice;
    private VoiceAssistant.SpeechSession? _speech;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoiceText))]
    [NotifyPropertyChangedFor(nameof(IsListening))]
    [NotifyPropertyChangedFor(nameof(IsSpeaking))]
    [NotifyPropertyChangedFor(nameof(ShowVoiceIndicator))]
    private VoiceState _voiceState = VoiceState.Off;

    public bool IsVoiceEnabled => _settings.Voice.Enabled && _voice is not null;
    public bool IsListening => VoiceState is VoiceState.Listening;
    public bool IsSpeaking => VoiceState is VoiceState.Speaking;
    public bool ShowVoiceIndicator => VoiceState is VoiceState.Listening or VoiceState.Transcribing or VoiceState.Speaking or VoiceState.WaitingForWakeWord;

    public string VoiceText => VoiceState switch
    {
        VoiceState.Listening => "Escuchando…",
        VoiceState.Transcribing => "Entendiendo…",
        VoiceState.Speaking => "Hablando…",
        VoiceState.WaitingForWakeWord => "Di «Carvis»",
        _ => string.Empty,
    };

    private void AttachVoice()
    {
        if (_voice is null)
            return;
        _voice.StateChanged += state => Dispatcher.UIThread.Post(() => VoiceState = state);
        _voice.Problem += message => Dispatcher.UIThread.Post(() => AddNotice(message));
        _voice.CommandHeard += text => Dispatcher.UIThread.Post(() => _ = AskByVoiceAsync(text));
        _voice.Apply();
    }

    /// <summary>The mic button and the push-to-talk key: listen, finish, or stop speaking.</summary>
    [RelayCommand]
    public void ToggleVoice()
    {
        if (_voice is null)
            return;
        if (!_settings.Voice.Enabled)
        {
            AddNotice("La voz está desactivada. Actívala y descarga los modelos en Ajustes → Voz.");
            return;
        }
        _voice.Toggle();
    }

    [RelayCommand]
    private void StopSpeaking() => _voice?.StopSpeaking();

    /// <summary>A spoken question: answered aloud too, if the user wants it.</summary>
    private async Task AskByVoiceAsync(string text)
    {
        while (IsBusy)
            await Task.Delay(200);
        VoiceActivated?.Invoke();
        if (_settings.Voice.SpeakAnswers && _voice is { CanSpeak: true })
            _speech = _voice.BeginSpeaking();
        await AskAsync(text);
    }

    /// <summary>The window should show itself: the user spoke to Carvis.</summary>
    public event Action? VoiceActivated;

    public void OnVoiceSettingsApplied()
    {
        _voice?.Apply();
        OnPropertyChanged(nameof(IsVoiceEnabled));
    }
}
