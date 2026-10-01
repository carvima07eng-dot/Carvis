using System.Collections.ObjectModel;
using Carvis.App.Platform;
using Carvis.Core.Configuration;
using Carvis.Core.Input;
using Carvis.Core.Ollama;
using Carvis.Core.Platform;
using Carvis.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

/// <summary>Edits a copy of the settings; nothing changes until "Guardar".</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly CarvisSettings _live;
    private readonly SettingsStore _store;
    private readonly IModelManager _models;
    private readonly IMemoryStore _memories;
    private readonly CarvisDatabase _database;
    private readonly IShell _shell;
    private readonly AppPaths _paths;
    private readonly Carvis.Core.Indexing.IIndexService _index;
    private bool _confirmingDelete;

    public SettingsViewModel(CarvisSettings live, SettingsStore store, IModelManager models, IMemoryStore memories,
        CarvisDatabase database, IShell shell, AppPaths paths, Carvis.Core.Indexing.IIndexService index)
    {
        _index = index;
        _indexStatus = index.Status.Describe();
        index.StatusChanged += status => Avalonia.Threading.Dispatcher.UIThread.Post(() => IndexStatus = status.Describe() +
            (status.LastError is { } error ? $" · Último problema: {error}" : string.Empty));
        _live = live;
        _store = store;
        _models = models;
        _memories = memories;
        _database = database;
        _shell = shell;
        _paths = paths;
        Draft = SettingsApplier.Clone(live);
        AllowedFolders = new ObservableCollection<string>(Draft.Permissions.AllowedFolders);
        DocumentFolders = new ObservableCollection<string>(Draft.Documents.Folders);
        _startWithWindows = OperatingSystem.IsWindows() && WindowsStartup.IsEnabled();
        RefreshMemories();
    }

    public CarvisSettings Draft { get; }
    public ObservableCollection<string> AllowedFolders { get; }
    public ObservableCollection<string> DocumentFolders { get; }
    public ObservableCollection<string> InstalledModels { get; } = [];
    public ObservableCollection<MemoryItem> Memories { get; } = [];

    public string[] Backdrops { get; } = ["Solid", "Acrylic", "Mica"];
    public string[] Themes { get; } = ["Oscuro", "Claro", "Como Windows"];
    private static readonly string[] ThemeValues = ["Dark", "Light", "System"];

    public int ThemeIndex
    {
        get => Math.Max(0, Array.IndexOf(ThemeValues, Draft.Window.Theme));
        set => Draft.Window.Theme = ThemeValues[Math.Clamp(value, 0, ThemeValues.Length - 1)];
    }

    /// <summary>Accent presets; the first one is the theme's own.</summary>
    public string[] AccentColors { get; } = ["", "#22D3EE", "#A78BFA", "#34D399", "#F472B6", "#FBBF24", "#60A5FA", "#F87171"];
    public string[] WindowModes { get; } = ["Ventana normal", "Spotlight (siempre encima, se oculta al perder el foco)"];
    public bool IsWindows => OperatingSystem.IsWindows();
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
    public string DataFolder => _paths.DataRoot;

    /// <summary>Saved; the bool says whether a restart is needed for everything to apply.</summary>
    public event Action<bool>? Saved;

    public int WindowMode
    {
        get => Draft.Window.HideOnFocusLost ? 1 : 0;
        set => Draft.Window.HideOnFocusLost = value == 1;
    }

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _indexStatus;

    [RelayCommand]
    private void IndexNow()
    {
        _live.Documents.Folders = DocumentFolders.ToList();
        _store.Save(_live);
        _index.StartWatching();
        _ = Task.Run(() => _index.IndexAsync());
    }

    [RelayCommand]
    private void PauseIndex() => _index.Pause();

    [RelayCommand]
    private void ClearIndex() => _index.ClearIndex();

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string _deleteDataLabel = "Borrar todos mis datos";

    [ObservableProperty]
    private string? _downloadStatus;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [RelayCommand]
    public async Task LoadModelsAsync()
    {
        try
        {
            var models = await _models.ListAsync();
            InstalledModels.Clear();
            foreach (var model in models)
                InstalledModels.Add(model.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Message = "No he podido leer los modelos: ¿está Ollama abierto?";
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync(string? model)
    {
        model = model?.Trim();
        if (string.IsNullOrEmpty(model) || IsDownloading)
            return;

        IsDownloading = true;
        try
        {
            var progress = new Progress<(string Status, double Progress)>(p =>
            {
                DownloadStatus = $"{model}: {p.Status} {p.Progress:P0}";
                DownloadProgress = p.Progress * 100;
            });
            await _models.PullAsync(model, progress);
            DownloadStatus = $"{model} descargado.";
            await LoadModelsAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OllamaSharp.Models.Exceptions.OllamaException)
        {
            DownloadStatus = $"No se ha podido descargar {model}: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    public void AddAllowedFolder(string path)
    {
        if (!AllowedFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            AllowedFolders.Add(path);
    }

    [RelayCommand]
    private void RemoveAllowedFolder(string path) => AllowedFolders.Remove(path);

    public void AddDocumentFolder(string path)
    {
        if (!DocumentFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            DocumentFolders.Add(path);
    }

    [RelayCommand]
    private void RemoveDocumentFolder(string path) => DocumentFolders.Remove(path);

    [RelayCommand]
    private void DeleteMemory(MemoryItem item)
    {
        _memories.Delete(item.Id);
        RefreshMemories();
    }

    [RelayCommand]
    private void ClearMemories()
    {
        _memories.Clear();
        RefreshMemories();
    }

    [RelayCommand]
    private Task OpenDataFolderAsync() => _shell.OpenAsync(_paths.DataRoot);

    [RelayCommand]
    private Task OpenLogsAsync() => _shell.OpenAsync(_paths.LogsDirectory);

    // Two clicks: the first one asks, the second one deletes.
    [RelayCommand]
    private void DeleteAllData()
    {
        if (!_confirmingDelete)
        {
            _confirmingDelete = true;
            DeleteDataLabel = "¿Seguro? Pulsa otra vez para borrarlo todo";
            return;
        }
        _database.DeleteAllUserData();
        _confirmingDelete = false;
        DeleteDataLabel = "Borrar todos mis datos";
        RefreshMemories();
        Message = "He borrado las conversaciones, recuerdos, notas, recordatorios y el historial de acciones.";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _store.Reset();
        Message = "Ajustes de fábrica restaurados. Reinicia Carvis para aplicarlos.";
        Saved?.Invoke(true);
    }

    [RelayCommand]
    private void Save()
    {
        Draft.Permissions.AllowedFolders = AllowedFolders.ToList();
        Draft.Documents.Folders = DocumentFolders.ToList();

        var problems = SettingsValidator.Validate(Draft);
        var restart = Draft.Ollama.BaseUrl != _live.Ollama.BaseUrl
                      || Draft.Ollama.RequestTimeoutSeconds != _live.Ollama.RequestTimeoutSeconds
                      || Draft.Privacy.EncryptData != _live.Privacy.EncryptData;

        SettingsApplier.CopyInto(Draft, _live);
        _store.Save(_live);

        if (OperatingSystem.IsWindows() && StartWithWindows != WindowsStartup.IsEnabled())
            WindowsStartup.SetEnabled(StartWithWindows);

        Message = problems.Count > 0
            ? "Guardado con correcciones: " + string.Join(" ", problems)
            : restart ? "Guardado. Algunos cambios se aplican al reiniciar Carvis." : "Guardado.";
        Saved?.Invoke(restart);
    }

    public static bool IsValidHotkey(string? text) => HotkeyGesture.TryParse(text, out _);

    private void RefreshMemories()
    {
        Memories.Clear();
        foreach (var memory in _memories.All())
            Memories.Add(memory);
    }
}
