using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string FlashAttention = "OLLAMA_FLASH_ATTENTION";
    private const string KvCacheType = "OLLAMA_KV_CACHE_TYPE";

    [ObservableProperty]
    private string? _ollamaTuningStatus = DescribeOllamaTuning();

    /// <summary>
    /// Flash attention and an 8-bit KV cache roughly halve the memory a long context needs.
    /// They are Ollama server settings: user environment variables read when Ollama starts.
    /// </summary>
    [RelayCommand]
    private void OptimizeOllama()
    {
        if (!OperatingSystem.IsWindows())
        {
            OllamaTuningStatus = "Pon OLLAMA_FLASH_ATTENTION=1 y OLLAMA_KV_CACHE_TYPE=q8_0 en el entorno del servicio de Ollama.";
            return;
        }
        Environment.SetEnvironmentVariable(FlashAttention, "1", EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(KvCacheType, "q8_0", EnvironmentVariableTarget.User);
        OllamaTuningStatus = "Hecho. Cierra Ollama desde su icono de la bandeja y vuelve a abrirlo para que lo use.";
    }

    [RelayCommand]
    private void ResetOllamaTuning()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Environment.SetEnvironmentVariable(FlashAttention, null, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(KvCacheType, null, EnvironmentVariableTarget.User);
        OllamaTuningStatus = "Quitado. Reinicia Ollama para volver a su configuración normal.";
    }

    private static string DescribeOllamaTuning()
    {
        if (!OperatingSystem.IsWindows())
            return "Disponible en Windows.";
        var flash = Environment.GetEnvironmentVariable(FlashAttention, EnvironmentVariableTarget.User);
        var kv = Environment.GetEnvironmentVariable(KvCacheType, EnvironmentVariableTarget.User);
        return flash == "1" && !string.IsNullOrEmpty(kv)
            ? $"Activado (flash attention y caché {kv})."
            : "Sin activar: Ollama usa más memoria de la gráfica con contextos largos.";
    }
}

public sealed partial class SettingsViewModel
{
    /// <summary>The swatch of each accent preset (the empty one shows the theme's).</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter AccentToBrush =
        new Avalonia.Data.Converters.FuncValueConverter<string?, Avalonia.Media.IBrush>(color =>
            Avalonia.Media.Color.TryParse(color, out var c) ? new Avalonia.Media.SolidColorBrush(c) : Avalonia.Media.Brush.Parse("#22D3EE"));

    public string AccentText => string.IsNullOrEmpty(Draft.Window.AccentColor)
        ? "Acento: el del tema."
        : $"Acento: {Draft.Window.AccentColor}.";

    [RelayCommand]
    private void SetAccent(string? color)
    {
        Draft.Window.AccentColor = color ?? string.Empty;
        OnPropertyChanged(nameof(AccentText));
    }
}
