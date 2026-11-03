using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Carvis.App.ViewModels;

/// <summary>Which Fluent icon stands for each tool (keys of Styles/Icons.axaml without "Icon.").</summary>
public static class ToolIcons
{
    private static readonly Dictionary<string, string> ByTool = new()
    {
        ["crear_carpeta"] = "Folder",
        ["crear_archivo"] = "Document",
        ["listar_carpeta"] = "FolderOpen",
        ["buscar_archivos"] = "Search",
        ["leer_archivo"] = "Document",
        ["mover"] = "Folder",
        ["copiar"] = "Copy",
        ["renombrar"] = "Edit",
        ["enviar_a_papelera"] = "Delete",
        ["abrir_archivo"] = "Open",
        ["mostrar_en_explorador"] = "FolderOpen",
        ["comprimir"] = "Folder",
        ["descomprimir"] = "FolderOpen",
        ["organizar_carpeta"] = "Sparkle",
        ["buscar_duplicados"] = "Copy",
        ["archivos_grandes"] = "Search",
        ["deshacer"] = "Undo",
        ["historial_acciones"] = "History",
        ["abrir_programa"] = "App",
        ["programas_instalados"] = "App",
        ["programas_abiertos"] = "Window",
        ["ventana"] = "Window",
        ["cerrar_programa"] = "Dismiss",
        ["abrir_web"] = "Globe",
        ["buscar_en_web"] = "Globe",
        ["recordar"] = "Memory",
        ["olvidar"] = "Memory",
        ["listar_recuerdos"] = "Memory",
        ["buscar_en_documentos"] = "DocumentSearch",
        ["indexar_documentos"] = "Sync",
        ["resumir_documento"] = "Document",
        ["comparar_documentos"] = "Document",
        ["volumen"] = "Speaker",
        ["multimedia"] = "Media",
        ["info_sistema"] = "Desktop",
        ["energia"] = "Power",
        ["portapapeles"] = "Clipboard",
        ["brillo"] = "Brightness",
        ["tema_windows"] = "DarkTheme",
        ["ajustes_windows"] = "Settings",
        ["crear_recordatorio"] = "Alarm",
        ["temporizador"] = "Timer",
        ["listar_recordatorios"] = "Alert",
        ["borrar_recordatorio"] = "Alert",
        ["crear_rutina"] = "Flash",
        ["ejecutar_rutina"] = "Flash",
        ["listar_rutinas"] = "Flash",
        ["borrar_rutina"] = "Flash",
        ["ejecutar_powershell"] = "Console",
        ["calcular"] = "Calculator",
        ["convertir_unidades"] = "Calculator",
        ["fecha_hora"] = "Calendar",
        ["crear_nota"] = "Note",
        ["listar_notas"] = "Note",
        ["borrar_nota"] = "Note",
        ["crear_tarea"] = "TaskList",
        ["listar_tareas"] = "TaskList",
        ["completar_tarea"] = "TaskList",
        ["borrar_tarea"] = "TaskList",
        ["tiempo"] = "Weather",
        ["divisas"] = "Money",
        ["leer_resultados_web"] = "Globe",
        ["ver_pantalla"] = "Eye",
    };

    private static readonly Dictionary<string, string> ByCategory = new()
    {
        ["archivos"] = "Folder",
        ["programas"] = "App",
        ["web"] = "Globe",
        ["sistema"] = "Desktop",
        ["recordatorios"] = "Alarm",
        ["notas"] = "Note",
        ["calculos"] = "Calculator",
        ["memoria"] = "Memory",
        ["documentos"] = "DocumentSearch",
        ["scripts"] = "Flash",
        ["vision"] = "Eye",
        ["internet"] = "Globe",
        ["acciones"] = "History",
        ["mcp"] = "Plug",
    };

    public static string For(string toolName, string? category = null) =>
        ByTool.TryGetValue(toolName, out var key) ? key
        : toolName.StartsWith("mcp_", StringComparison.Ordinal) ? "Plug"
        : category is not null && ByCategory.TryGetValue(category, out var byCategory) ? byCategory
        : "Wrench";

    /// <summary>"Mic" → the geometry Icon.Mic of the design system.</summary>
    public static readonly IValueConverter ToGeometry = new FuncValueConverter<string?, Geometry?>(key =>
        key is not null && Application.Current?.TryGetResource("Icon." + key, null, out var value) == true ? value as Geometry : null);
}
