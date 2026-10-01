namespace Carvis.Core.Configuration;

/// <summary>Root of appsettings.json.</summary>
public sealed class CarvisSettings
{
    public OllamaSettings Ollama { get; set; } = new();
    public HotkeySettings Hotkey { get; set; } = new();
    public AssistantSettings Assistant { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public PermissionsSettings Permissions { get; set; } = new();
}

public sealed class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:8b";

    /// <summary>Used from phase 2 (document indexing).</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>qwen3 can "think" before answering; off by default for snappier replies.</summary>
    public bool EnableThinking { get; set; }

    /// <summary>How long Ollama keeps the model loaded after the last message (e.g. "30m", "-1" = forever).</summary>
    public string KeepAlive { get; set; } = "30m";

    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>Context window in tokens (num_ctx). Ollama's default is small and silently truncates long chats.</summary>
    public int ContextLength { get; set; } = 16384;

    /// <summary>Creativity for normal conversation.</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Lower temperature while the model is choosing and filling in tools.</summary>
    public double ToolTemperature { get; set; } = 0.2;
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

    /// <summary>How Carvis addresses the user; empty = the Windows user name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Older messages are dropped from the context beyond this limit.</summary>
    public int MaxHistoryMessages { get; set; } = 40;

    /// <summary>Let the assistant act on the PC through tools.</summary>
    public bool EnableTools { get; set; } = true;

    /// <summary>Most tool rounds in one answer, so a confused model can't loop forever.</summary>
    public int MaxToolSteps { get; set; } = 8;
}

public sealed class WindowSettings
{
    /// <summary>Start in the system tray without showing the window.</summary>
    public bool StartHidden { get; set; }

    /// <summary>Spotlight mode: always on top and hidden when it loses focus. Off = normal app window.</summary>
    public bool HideOnFocusLost { get; set; }

    /// <summary>"Solid", "Acrylic" or "Mica" (the last two need Windows 11).</summary>
    public string Backdrop { get; set; } = "Solid";

    public double FontSize { get; set; } = 14;

    public bool RememberPosition { get; set; } = true;
}

public sealed class LoggingSettings
{
    /// <summary>Trace, Debug, Information, Warning or Error.</summary>
    public string Level { get; set; } = "Information";

    /// <summary>Also write message contents to the log (only for debugging; off for privacy).</summary>
    public bool IncludeContent { get; set; }

    public int RetainDays { get; set; } = 14;
}

public sealed class PermissionsSettings
{
    /// <summary>Folders Carvis may read and change. Empty = the whole user profile except AppData.</summary>
    public List<string> AllowedFolders { get; set; } = [];

    /// <summary>Ask before changing files (create, move, rename...). Deleting and running code always asks.</summary>
    public bool ConfirmChanges { get; set; } = true;

    /// <summary>Ask also before harmless actions such as opening a program or a web page.</summary>
    public bool ConfirmLowRisk { get; set; }

    /// <summary>Tools that need Internet (weather, currencies, web search) stay off unless this is on.</summary>
    public bool AllowInternet { get; set; }
}
