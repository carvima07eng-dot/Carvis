namespace Carvis.Core.Configuration;

/// <summary>Root of appsettings.json.</summary>
public sealed class CarvisSettings
{
    public OllamaSettings Ollama { get; set; } = new();
    public HotkeySettings Hotkey { get; set; } = new();
    public AssistantSettings Assistant { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
}

public sealed class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:8b";

    /// <summary>Used from phase 2 (document indexing).</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>qwen3 can "think" before answering; off by default for snappier replies.</summary>
    public bool EnableThinking { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 300;
}

public sealed class HotkeySettings
{
    /// <summary>Global shortcut that shows/hides the window, e.g. "Alt+Space" or "Ctrl+Shift+J".</summary>
    public string ToggleWindow { get; set; } = "Alt+Space";
}

public sealed class AssistantSettings
{
    public string SystemPrompt { get; set; } =
        "Eres Carvis, un asistente personal que se ejecuta en local en el PC del usuario. " +
        "Responde siempre en español, de forma clara y concisa.";

    /// <summary>Older messages are dropped from the context beyond this limit.</summary>
    public int MaxHistoryMessages { get; set; } = 40;
}

public sealed class WindowSettings
{
    public bool StartHidden { get; set; }
    public bool HideOnFocusLost { get; set; } = true;
}
