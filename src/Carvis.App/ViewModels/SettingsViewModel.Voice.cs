using System.Collections.ObjectModel;
using Carvis.Core.Voice;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class SettingsViewModel
{
    private VoiceModels? _voiceModels;
    private ModelDownloader? _downloader;
    private VoiceAssistant? _voice;
    private IAudioInput? _audioInput;
    private IAudioOutput? _audioOutput;
    private CancellationTokenSource? _voiceDownload;

    public string[] WhisperSizes { get; } = VoiceModels.WhisperSizes;
    private const string DefaultDevice = "El predeterminado de Windows";

    public string SelectedMicrophone
    {
        get => string.IsNullOrEmpty(Draft.Voice.InputDevice) ? DefaultDevice : Draft.Voice.InputDevice;
        set => Draft.Voice.InputDevice = value == DefaultDevice ? string.Empty : value ?? string.Empty;
    }

    public string SelectedSpeaker
    {
        get => string.IsNullOrEmpty(Draft.Voice.OutputDevice) ? DefaultDevice : Draft.Voice.OutputDevice;
        set => Draft.Voice.OutputDevice = value == DefaultDevice ? string.Empty : value ?? string.Empty;
    }
    public ObservableCollection<string> Microphones { get; } = [];
    public ObservableCollection<string> Speakers { get; } = [];

    [ObservableProperty]
    private string? _voiceStatus;

    [ObservableProperty]
    private double _voiceDownloadProgress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadVoiceModelsCommand))]
    private bool _isDownloadingVoice;

    /// <summary>Called by the window with the voice services (kept out of the constructor to keep it short).</summary>
    public void AttachVoice(VoiceModels models, ModelDownloader downloader, VoiceAssistant voice, IAudioInput input, IAudioOutput output)
    {
        _voiceModels = models;
        _downloader = downloader;
        _voice = voice;
        _audioInput = input;
        _audioOutput = output;

        Microphones.Clear();
        Microphones.Add(DefaultDevice);
        foreach (var device in input.Devices())
            Microphones.Add(device.Name);
        Speakers.Clear();
        Speakers.Add(DefaultDevice);
        foreach (var device in output.Devices())
            Speakers.Add(device.Name);
        RefreshVoiceStatus();
    }

    public void RefreshVoiceStatus()
    {
        if (_voiceModels is null)
            return;
        // The status is about the draft (what will be saved), so the right files are checked.
        var draftModels = new VoiceModels(_paths, Draft.Voice);
        var missing = draftModels.Missing();
        VoiceStatus = !(_audioInput?.IsAvailable ?? false)
            ? "No encuentro ningún micrófono."
            : missing.Count == 0
                ? "Todo listo: los modelos de voz están descargados."
                : $"Faltan por descargar: {string.Join(", ", missing.Select(m => m.Name))} (unos {missing.Sum(m => m.ApproximateBytes) / 1_000_000} MB).";
    }

    private bool CanDownloadVoice() => !IsDownloadingVoice;

    /// <summary>Downloads Whisper, Piper and the voice: the only time Carvis goes to the Internet for voice.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadVoice))]
    private async Task DownloadVoiceModelsAsync()
    {
        if (_downloader is null)
            return;
        var models = new VoiceModels(_paths, Draft.Voice);
        var items = models.Missing();
        if (items.Count == 0)
        {
            RefreshVoiceStatus();
            return;
        }

        IsDownloadingVoice = true;
        _voiceDownload = new CancellationTokenSource();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var index = i;
                VoiceStatus = $"Descargando {item.Name} ({index + 1}/{items.Count})…";
                var progress = new Progress<double>(p => VoiceDownloadProgress = (index + p) / items.Count * 100);
                await _downloader.DownloadAsync(item, progress, _voiceDownload.Token);
                models.Install(item);
            }
            VoiceDownloadProgress = 100;
            RefreshVoiceStatus();
        }
        catch (OperationCanceledException)
        {
            VoiceStatus = "Descarga cancelada.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Voice download failed");
            VoiceStatus = "No he podido descargar la voz. Comprueba la conexión y vuelve a intentarlo.";
        }
        finally
        {
            IsDownloadingVoice = false;
            _voiceDownload = null;
        }
    }

    [RelayCommand]
    private void CancelVoiceDownload() => _voiceDownload?.Cancel();

    [RelayCommand]
    private async Task TestSpeakerAsync()
    {
        if (_voice is null)
            return;
        if (!_voice.CanSpeak)
        {
            VoiceStatus = "Para oír a Carvis hay que descargar antes Piper y la voz.";
            return;
        }
        try
        {
            VoiceStatus = "Hablando…";
            var name = string.IsNullOrWhiteSpace(Draft.Assistant.UserName) ? string.Empty : $", {Draft.Assistant.UserName}";
            await _voice.SayAsync($"Hola{name}. Soy Carvis. Así sueno.");
            VoiceStatus = "¿Se oye bien? Si va muy rápido o lento, cambia la velocidad.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Voice test failed");
            VoiceStatus = "No he podido reproducir la voz. Prueba a descargarla otra vez.";
        }
    }

    [RelayCommand]
    private void TestMicrophone()
    {
        if (_audioInput is not { IsAvailable: true })
        {
            VoiceStatus = "No encuentro ningún micrófono.";
            return;
        }
        // A short level meter: say something and look at the bar.
        var peak = 0f;
        void OnSamples(float[] samples)
        {
            peak = Math.Max(peak, VoiceActivityDetector.Rms(samples));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => MicrophoneLevel = Math.Min(100, peak * 400));
        }
        try
        {
            var wasRecording = _audioInput.IsRecording;
            _audioInput.SamplesAvailable += OnSamples;
            if (!wasRecording)
                _audioInput.Start(string.IsNullOrWhiteSpace(Draft.Voice.InputDevice) ? null : Draft.Voice.InputDevice);
            VoiceStatus = "Habla durante 3 segundos…";
            _ = Task.Delay(3000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _audioInput.SamplesAvailable -= OnSamples;
                if (!wasRecording)
                    _audioInput.Stop();
                VoiceStatus = peak > 0.02f ? "El micrófono funciona." : "Apenas se oye nada: revisa el micrófono elegido y su volumen en Windows.";
            }));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Microphone test failed");
            _audioInput.SamplesAvailable -= OnSamples;
            VoiceStatus = "No puedo usar el micrófono. Mira que esté conectado y que Windows deje usarlo (Configuración → Privacidad → Micrófono).";
        }
    }

    [ObservableProperty]
    private double _microphoneLevel;
}
