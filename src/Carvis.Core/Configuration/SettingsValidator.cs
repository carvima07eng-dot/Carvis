using Carvis.Core.Input;

namespace Carvis.Core.Configuration;

/// <summary>Fixes impossible values (falling back to defaults) and explains what was wrong.</summary>
public static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(CarvisSettings settings)
    {
        var problems = new List<string>();
        var defaults = new CarvisSettings();

        if (!Uri.TryCreate(settings.Ollama.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            problems.Add($"La URL de Ollama «{settings.Ollama.BaseUrl}» no es válida; uso {defaults.Ollama.BaseUrl}.");
            settings.Ollama.BaseUrl = defaults.Ollama.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(settings.Ollama.ChatModel))
        {
            problems.Add($"No hay modelo de chat configurado; uso {defaults.Ollama.ChatModel}.");
            settings.Ollama.ChatModel = defaults.Ollama.ChatModel;
        }

        Clamp(problems, "La ventana de contexto", settings.Ollama.ContextLength, 2048, 262144,
            v => settings.Ollama.ContextLength = v, defaults.Ollama.ContextLength);
        Clamp(problems, "El tiempo de espera", settings.Ollama.RequestTimeoutSeconds, 10, 3600,
            v => settings.Ollama.RequestTimeoutSeconds = v, defaults.Ollama.RequestTimeoutSeconds);
        Clamp(problems, "El historial máximo", settings.Assistant.MaxHistoryMessages, 2, 1000,
            v => settings.Assistant.MaxHistoryMessages = v, defaults.Assistant.MaxHistoryMessages);

        if (settings.Ollama.Temperature is < 0 or > 2)
        {
            problems.Add("La temperatura debe estar entre 0 y 2.");
            settings.Ollama.Temperature = defaults.Ollama.Temperature;
        }
        if (settings.Ollama.ToolTemperature is < 0 or > 2)
        {
            problems.Add("La temperatura de herramientas debe estar entre 0 y 2.");
            settings.Ollama.ToolTemperature = defaults.Ollama.ToolTemperature;
        }

        if (!IsValidKeepAlive(settings.Ollama.KeepAlive))
        {
            problems.Add($"KeepAlive «{settings.Ollama.KeepAlive}» no es válido (ejemplos: 30m, 1h, -1); uso {defaults.Ollama.KeepAlive}.");
            settings.Ollama.KeepAlive = defaults.Ollama.KeepAlive;
        }

        if (!HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out _))
        {
            problems.Add($"El atajo «{settings.Hotkey.ToggleWindow}» no es válido; uso {defaults.Hotkey.ToggleWindow}.");
            settings.Hotkey.ToggleWindow = defaults.Hotkey.ToggleWindow;
        }

        if (settings.Window.Backdrop is not ("Solid" or "Acrylic" or "Mica"))
        {
            problems.Add($"El fondo «{settings.Window.Backdrop}» no existe (Solid, Acrylic o Mica); uso Solid.");
            settings.Window.Backdrop = "Solid";
        }

        if (settings.Window.FontSize is < 10 or > 28)
        {
            problems.Add("El tamaño de letra debe estar entre 10 y 28.");
            settings.Window.FontSize = defaults.Window.FontSize;
        }

        return problems;
    }

    public static bool IsValidKeepAlive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (int.TryParse(value, out _))
            return true;
        var number = value[..^1];
        return value[^1] is 's' or 'm' or 'h' && double.TryParse(number, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 0;
    }

    private static void Clamp(List<string> problems, string name, int value, int min, int max, Action<int> set, int fallback)
    {
        if (value >= min && value <= max)
            return;
        problems.Add($"{name} debe estar entre {min} y {max}; uso {fallback}.");
        set(fallback);
    }
}
