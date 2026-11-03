using System.Globalization;
using Carvis.Core.Ollama;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carvis.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    private bool _warnedAboutCpu;

    /// <summary>"GPU 5,8 GB" in the footer, or "CPU 40 %" when part of the model doesn't fit.</summary>
    [ObservableProperty]
    private string? _gpuText;

    /// <summary>Reads `ollama ps`: warns once if the chat model isn't fully on the graphics card.</summary>
    public async Task CheckGpuAsync(bool report)
    {
        IReadOnlyList<LoadedModel> loaded;
        try
        {
            loaded = await _models.LoadedAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (report)
                Say("No he podido preguntar a Ollama qué modelos tiene cargados.");
            return;
        }

        var vram = loaded.Sum(m => m.VramBytes);
        var chat = loaded.FirstOrDefault(m => ModelNames.AreSame(m.Name, _settings.Ollama.ChatModel));
        GpuText = chat is { GpuShare: < 0.99 }
            ? $"CPU {(1 - chat.GpuShare) * 100:0} %"
            : vram > 0 ? $"GPU {(vram / 1e9).ToString("0.0", Spanish)} GB" : null;

        if (report)
        {
            Say(loaded.Count == 0
                ? "Ahora mismo no hay ningún modelo cargado en memoria."
                : "Modelos cargados:\n" + string.Join("\n", loaded.Select(m =>
                    $"- **{m.Name}**: {(m.SizeBytes / 1e9).ToString("0.0", Spanish)} GB, {m.GpuShare * 100:0} % en la gráfica, contexto {m.ContextLength}")));
        }

        if (chat is not null && chat.GpuShare < 0.99 && !_warnedAboutCpu)
        {
            _warnedAboutCpu = true;
            AddNotice(chat.GpuShare <= 0.01
                ? $"Ollama está ejecutando {chat.Name} en el procesador, no en la tarjeta gráfica: irá muy lento. Revisa que el driver de NVIDIA esté actualizado y reinicia Ollama."
                : $"{chat.Name} no cabe entero en la gráfica ({chat.GpuShare * 100:0} % en GPU): irá más lento. Escribe /liberar, cierra juegos o programas que usen la gráfica, o baja la ventana de contexto en Ajustes.");
        }
    }

    /// <summary>Frees the graphics card: every model Ollama has loaded (they load again when needed).</summary>
    private async Task FreeMemoryAsync()
    {
        try
        {
            var loaded = await _models.LoadedAsync();
            foreach (var model in loaded)
                await _models.UnloadAsync(model.Name);
            _isModelLoaded = false;
            GpuText = null;
            Say(loaded.Count == 0 ? "No había nada cargado." : $"He liberado la memoria de {string.Join(", ", loaded.Select(m => m.Name))}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Say("No he podido liberar la memoria de la gráfica. Comprueba que Ollama está abierto.");
        }
    }
}

public sealed partial class MainWindowViewModel
{
    private Action? _installUpdate;

    /// <summary>"Carvis 1.1.0 está lista" with a button, when an update has been downloaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(HasAnyNotice))]
    private string? _updateText;

    public bool HasUpdate => UpdateText is not null;

    public void OfferUpdate(string version, Action install)
    {
        _installUpdate = install;
        UpdateText = $"Carvis {version} está descargada. Se instalará al reiniciar.";
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void InstallUpdate() => _installUpdate?.Invoke();
}

public sealed partial class MainWindowViewModel
{
    /// <summary>The report of a crash in the previous session, offered once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrashReport))]
    [NotifyPropertyChangedFor(nameof(HasAnyNotice))]
    private string? _crashReport;

    public bool HasCrashReport => CrashReport is not null;

    /// <summary>Copies text to the clipboard (set by the window).</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task CopyCrashReportAsync()
    {
        if (CrashReport is not null && CopyToClipboard is not null)
            await CopyToClipboard(CrashReport);
        AddNotice("He copiado el informe. Pégalo donde quieras o en un issue de GitHub.");
        CrashReport = null;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task ReportCrashAsync()
    {
        var url = Services.CrashReporter.NewIssueUrl(CrashReport);
        CrashReport = null;
        return _shell.OpenAsync(url);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void DismissCrashReport() => CrashReport = null;
}
