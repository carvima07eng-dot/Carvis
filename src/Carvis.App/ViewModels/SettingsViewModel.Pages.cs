using System.Collections.ObjectModel;
using System.Globalization;
using Carvis.Core.Configuration;
using Carvis.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carvis.App.ViewModels;

public sealed record SettingsPage(string Key, string Title, string IconKey);

public sealed record ReminderRow(long Id, string Text, string When);

public sealed partial class SettingsViewModel
{
    private IReminderStore? _reminders;
    private IRoutineStore? _routines;

    public IReadOnlyList<SettingsPage> Pages { get; } =
    [
        new("General", "General", "Settings"),
        new("Apariencia", "Apariencia", "Personalize"),
        new("Modelo", "Modelo de IA", "Model"),
        new("Documentos", "Documentos", "DocumentSearch"),
        new("Recordatorios", "Recordatorios y rutinas", "Alarm"),
        new("Voz", "Voz", "Mic"),
        new("Capturas", "Capturas de pantalla", "Screenshot"),
        new("Permisos", "Permisos", "Shield"),
        new("Privacidad", "Privacidad", "Privacy"),
        new("Memoria", "Memoria", "Memory"),
        new("Experimental", "Experimental", "Beaker"),
        new("Acerca", "Acerca de Carvis", "Info"),
    ];

    [ObservableProperty]
    private SettingsPage? _selectedPage;

    public void OpenPage(string key) => SelectedPage = Pages.FirstOrDefault(p => p.Key == key) ?? Pages[0];

    // Appearance ---------------------------------------------------------------------------------

    public string[] Backdrops { get; } = ["Mica (Windows 11)", "Acrílico", "Sólido"];
    private static readonly string[] BackdropValues = ["Mica", "Acrylic", "Solid"];

    public int BackdropIndex
    {
        get => Math.Max(0, Array.IndexOf(BackdropValues, Draft.Window.Backdrop));
        set => Draft.Window.Backdrop = BackdropValues[Math.Clamp(value, 0, BackdropValues.Length - 1)];
    }

    /// <summary>Accents of the Windows palette; the empty one means "the Windows accent".</summary>
    public string[] AccentColors { get; } = ["", "#0078D4", "#8764B8", "#00B7C3", "#00CC6A", "#FF8C00", "#E3008C", "#E81123"];

    // Reminders and routines ----------------------------------------------------------------------

    public ObservableCollection<ReminderRow> Reminders { get; } = [];
    public ObservableCollection<Routine> Routines { get; } = [];

    public bool HasNoReminders => Reminders.Count == 0;
    public bool HasNoRoutines => Routines.Count == 0;
    public bool HasNoMemories => Memories.Count == 0;
    public bool HasNoDocumentFolders => DocumentFolders.Count == 0;

    public void AttachPersonalStores(IReminderStore reminders, IRoutineStore routines)
    {
        _reminders = reminders;
        _routines = routines;
        RefreshReminders();
    }

    private void RefreshReminders()
    {
        Reminders.Clear();
        Routines.Clear();
        if (_reminders is not null)
        {
            var spanish = CultureInfo.GetCultureInfo("es-ES");
            foreach (var r in _reminders.Pending())
            {
                var when = r.DueAt.ToString("dddd d 'de' MMMM, HH:mm", spanish) + r.Recurrence switch
                {
                    Recurrence.Hourly => " · cada hora",
                    Recurrence.Daily => " · todos los días",
                    Recurrence.Weekdays => " · de lunes a viernes",
                    Recurrence.Weekly => " · cada semana",
                    _ => string.Empty,
                };
                Reminders.Add(new ReminderRow(r.Id, r.Routine is null ? r.Text : $"Rutina «{r.Routine}»", when));
            }
        }
        foreach (var routine in _routines?.All() ?? [])
            Routines.Add(routine);
        OnPropertyChanged(nameof(HasNoReminders));
        OnPropertyChanged(nameof(HasNoRoutines));
    }

    [RelayCommand]
    private void DeleteReminder(ReminderRow row)
    {
        _reminders?.Delete(row.Id);
        RefreshReminders();
    }

    [RelayCommand]
    private void DeleteRoutine(Routine routine)
    {
        _routines?.Delete(routine.Name);
        RefreshReminders();
    }

    // MCP servers (experimental) ---------------------------------------------------------------

    public ObservableCollection<McpServerSettings> McpServers { get; } = [];
    public string[] McpTransports { get; } = ["stdio", "http"];
    public string[] McpPermissions { get; } = ["ask", "read"];

    [ObservableProperty]
    private string _newMcpName = string.Empty;

    [ObservableProperty]
    private string _newMcpTransport = "stdio";

    [ObservableProperty]
    private string _newMcpTarget = string.Empty;

    [RelayCommand]
    private void AddMcpServer()
    {
        var name = NewMcpName.Trim();
        var target = NewMcpTarget.Trim();
        if (name.Length == 0 || target.Length == 0)
        {
            Message = "Pon un nombre y el programa (o la dirección) del servidor.";
            return;
        }
        var server = new McpServerSettings { Name = name, Transport = NewMcpTransport };
        if (server.Transport == "http")
        {
            server.Url = target;
        }
        else
        {
            var parts = target.Split(' ', 2, StringSplitOptions.TrimEntries);
            server.Command = parts[0];
            server.Arguments = parts.Length > 1 ? parts[1] : string.Empty;
        }
        McpServers.Add(server);
        NewMcpName = NewMcpTarget = string.Empty;
    }

    [RelayCommand]
    private void RemoveMcpServer(McpServerSettings server) => McpServers.Remove(server);
}
