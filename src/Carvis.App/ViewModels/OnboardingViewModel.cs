using System.Collections.ObjectModel;
using System.Diagnostics;
using Carvis.Core.Configuration;
using Carvis.Core.Ollama;
using Carvis.Core.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class ModelSetupViewModel(string name, string purpose) : ViewModelBase
{
    public string Name { get; } = name;
    public string Purpose { get; } = purpose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _status;

    public string StateText => IsInstalled ? "✓ Instalado" : "Sin descargar";
}

/// <summary>The first-run assistant: Ollama, models, document folders and the shortcut.</summary>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    public const int LastStep = 5;

    private readonly CarvisSettings _settings;
    private readonly SettingsStore _store;
    private readonly IOllamaHealthCheck _health;
    private readonly IModelManager _models;
    private readonly IShell _shell;

    public OnboardingViewModel(CarvisSettings settings, SettingsStore store, IOllamaHealthCheck health, IModelManager models, IShell shell, string hotkeyText)
    {
        _settings = settings;
        _store = store;
        _health = health;
        _models = models;
        _shell = shell;
        HotkeyText = hotkeyText;
        _userName = settings.Assistant.UserName.Length > 0 ? settings.Assistant.UserName : Environment.UserName;
        Models.Add(new ModelSetupViewModel(settings.Ollama.ChatModel, "Para conversar y actuar (unos 5 GB)"));
        Models.Add(new ModelSetupViewModel(settings.Ollama.EmbeddingModel, "Para buscar en tus documentos (unos 300 MB)"));
        foreach (var folder in settings.Documents.Folders)
            DocumentFolders.Add(folder);
    }

    public ObservableCollection<ModelSetupViewModel> Models { get; } = [];
    public ObservableCollection<string> DocumentFolders { get; } = [];
    public string HotkeyText { get; }
    public bool IsWindows => OperatingSystem.IsWindows();

    public event Action? Finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack), nameof(NextLabel), nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5))]
    private int _step;

    public bool IsStep0 => Step == 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool IsStep5 => Step == 5;

    [ObservableProperty]
    private string _userName;

    [ObservableProperty]
    private string _ollamaStatus = "Comprobando…";

    [ObservableProperty]
    private bool _isOllamaRunning;

    [ObservableProperty]
    private bool _hotkeyWorked;

    public bool CanGoBack => Step > 0;
    public string NextLabel => Step == LastStep ? "Empezar" : "Siguiente";
    public string StepText => $"Paso {Step + 1} de {LastStep + 1}";

    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step == LastStep)
        {
            Finish();
            return;
        }
        Step++;
        if (Step is 1 or 2)
            await CheckAsync();
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
            Step--;
    }

    [RelayCommand]
    public async Task CheckAsync()
    {
        OllamaStatus = "Comprobando…";
        var status = await _health.CheckAsync();
        IsOllamaRunning = status.State != OllamaState.ServerUnavailable;
        OllamaStatus = IsOllamaRunning ? "✓ Ollama está funcionando." : $"No encuentro Ollama en {status.BaseUrl}.";
        if (!IsOllamaRunning)
            return;

        try
        {
            var installed = await _models.ListAsync();
            foreach (var model in Models)
                model.IsInstalled = installed.Any(m => ModelNames.AreSame(m.Name, model.Name));
        }
        catch (HttpRequestException)
        {
        }
    }

    [RelayCommand]
    private Task OpenOllamaDownloadAsync() => _shell.OpenAsync("https://ollama.com/download");

    // winget opens its own console so the user sees what it does.
    [RelayCommand]
    private void InstallOllamaWithWinget()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Process.Start(new ProcessStartInfo("cmd.exe",
            "/k winget install --id Ollama.Ollama -e --accept-source-agreements --accept-package-agreements && echo. && echo Listo. Ya puedes cerrar esta ventana.")
        {
            UseShellExecute = true,
        })?.Dispose();
        OllamaStatus = "Instalando Ollama… cuando termine, pulsa «Comprobar».";
    }

    [RelayCommand]
    private async Task DownloadAsync(ModelSetupViewModel model)
    {
        if (model.IsDownloading || model.IsInstalled)
            return;
        model.IsDownloading = true;
        try
        {
            var progress = new Progress<(string Status, double Progress)>(p =>
            {
                model.Status = $"{p.Status} {p.Progress:P0}";
                model.Progress = p.Progress * 100;
            });
            await _models.PullAsync(model.Name, progress);
            model.IsInstalled = true;
            model.Status = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OllamaSharp.Models.Exceptions.OllamaException)
        {
            model.Status = $"Error: {ex.Message}";
        }
        finally
        {
            model.IsDownloading = false;
        }
    }

    public void AddDocumentFolder(string path)
    {
        if (!DocumentFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            DocumentFolders.Add(path);
    }

    [RelayCommand]
    private void RemoveDocumentFolder(string path) => DocumentFolders.Remove(path);

    public void OnHotkeyPressed() => HotkeyWorked = true;

    private void Finish()
    {
        _settings.Assistant.UserName = UserName.Trim() == Environment.UserName ? string.Empty : UserName.Trim();
        _settings.Documents.Folders = DocumentFolders.ToList();
        _settings.FirstRunCompleted = true;
        _store.Save(_settings);
        Finished?.Invoke();
    }
}
