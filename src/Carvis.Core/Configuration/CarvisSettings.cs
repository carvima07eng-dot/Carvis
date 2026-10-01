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
    public PrivacySettings Privacy { get; set; } = new();
    public DocumentsSettings Documents { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public VisionSettings Vision { get; set; } = new();

    /// <summary>The first-run assistant has been completed.</summary>
    public bool FirstRunCompleted { get; set; }
}

public sealed class PrivacySettings
{
    /// <summary>Encrypt conversations and memories with the Windows account (DPAPI).</summary>
    public bool EncryptData { get; set; } = true;

    /// <summary>Save conversations so they can be reopened later.</summary>
    public bool SaveConversations { get; set; } = true;

    /// <summary>Look for new versions on GitHub once a day (only the list of releases is downloaded).</summary>
    public bool CheckForUpdates { get; set; } = true;
}

public sealed class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:8b";

    /// <summary>Turns documents into vectors for searching them.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Model for questions about images and screenshots.</summary>
    public string VisionModel { get; set; } = "qwen2.5vl:7b";

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

    /// <summary>Nucleus sampling: lower = more predictable wording.</summary>
    public double TopP { get; set; } = 0.9;
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

    /// <summary>"Dark", "Light", "System" (follows Windows) or "HighContrast".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Highlight colour (#RRGGBB). Empty = the theme's cyan.</summary>
    public string AccentColor { get; set; } = string.Empty;

    /// <summary>Opening and message animations.</summary>
    public bool Animations { get; set; } = true;
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

    /// <summary>Address of a SearXNG instance for web search (e.g. http://localhost:8080). Empty = no web search.</summary>
    public string SearchUrl { get; set; } = string.Empty;

    /// <summary>Load extra tools from %AppData%\Carvis\plugins. Only for plugins you trust.</summary>
    public bool EnablePlugins { get; set; }
}

public sealed class DocumentsSettings
{
    /// <summary>Folders whose documents Carvis indexes to answer questions about them.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>Folder names skipped while indexing.</summary>
    public List<string> ExcludedFolders { get; set; } = ["node_modules", ".git", "bin", "obj", ".vs", "__pycache__"];

    public int MaxFileSizeMb { get; set; } = 50;

    /// <summary>Re-index changed files automatically while Carvis runs.</summary>
    public bool WatchChanges { get; set; } = true;

    /// <summary>How many document fragments go into the answer.</summary>
    public int ResultsPerQuestion { get; set; } = 6;
}

public sealed class VoiceSettings
{
    /// <summary>Voice is off until the user turns it on and downloads the models.</summary>
    public bool Enabled { get; set; }

    /// <summary>Press once to talk; Carvis stops listening when you stop speaking.</summary>
    public string PushToTalkHotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Whisper model: tiny, base, small or medium. Bigger is more accurate and slower.</summary>
    public string WhisperModel { get; set; } = "small";

    public string Language { get; set; } = "es";

    /// <summary>Use the graphics card for Whisper (Vulkan); falls back to the CPU.</summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>Microphone and speakers by name. Empty = the Windows default.</summary>
    public string InputDevice { get; set; } = string.Empty;
    public string OutputDevice { get; set; } = string.Empty;

    /// <summary>Read the answer aloud when the question was spoken.</summary>
    public bool SpeakAnswers { get; set; } = true;

    /// <summary>Piper voice, e.g. es_ES-davefx-medium.</summary>
    public string PiperVoice { get; set; } = "es_ES-davefx-medium";

    /// <summary>1 = normal speed, 1.2 = faster.</summary>
    public double SpeechRate { get; set; } = 1.0;

    /// <summary>How long a pause ends what you are saying.</summary>
    public int SilenceMilliseconds { get; set; } = 900;

    /// <summary>Listen all the time for "Carvis" (never while the PC is locked).</summary>
    public bool WakeWord { get; set; }

    /// <summary>After answering, listen again until there is silence (hands-free conversation).</summary>
    public bool ContinuousConversation { get; set; }

    /// <summary>Talking while Carvis speaks interrupts it. Only reliable with headphones.</summary>
    public bool InterruptByVoice { get; set; }
}

public sealed class VisionSettings
{
    /// <summary>Shortcut to capture part of the screen and ask about it.</summary>
    public string CaptureHotkey { get; set; } = "Ctrl+Alt+S";

    /// <summary>Keep captures in Pictures\Carvis. Off = they only live in memory.</summary>
    public bool SaveCaptures { get; set; }
}
