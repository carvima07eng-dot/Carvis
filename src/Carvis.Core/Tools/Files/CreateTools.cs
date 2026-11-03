using System.Text.Json.Nodes;
using Carvis.Core.Configuration;
using Carvis.Core.Context;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Files;

[BuiltInTool]
public sealed class CreateFolderTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "crear_carpeta";
    public string Description => "Crea una carpeta nueva (y las carpetas intermedias que falten).";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(Required("ruta", String("Ruta completa de la carpeta a crear, p. ej. C:\\Users\\Ana\\Desktop\\Proyectos")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var path = Resolve(arguments);
        return new ToolPreview($"Crear la carpeta «{FileToolHelpers.Display(path, folders)}»", ToolRisk.Modify)
        {
            PermissionScope = $"{Name}:{Path.GetDirectoryName(path)}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var path = Resolve(arguments);
        if (Directory.Exists(path))
            return Task.FromResult(ToolResult.Ok($"La carpeta {path} ya existía; no he cambiado nada."));
        if (File.Exists(path))
            return Task.FromResult(ToolResult.Fail($"Ya hay un archivo con ese nombre: {path}."));

        // Remember the first folder that didn't exist, so undo removes everything this created.
        var firstNew = path;
        for (var parent = Path.GetDirectoryName(path); parent is not null && !Directory.Exists(parent); parent = Path.GetDirectoryName(parent))
            firstNew = parent;

        Directory.CreateDirectory(path);
        var entry = journal.Record(Name, $"Crear la carpeta {FileToolHelpers.Display(path, folders)}", true, [new UndoStep(UndoKind.RemoveCreated, firstNew)]);
        return Task.FromResult(ToolResult.Ok($"Carpeta creada: {path}") with { JournalEntryId = entry.Id });
    }

    private string Resolve(ToolArguments arguments)
    {
        var path = paths.Resolve(arguments.String("ruta"));
        paths.EnsureAllowed(path);
        FileToolHelpers.ValidateName(Path.GetFileName(path));
        return path;
    }
}

[BuiltInTool]
public sealed class CreateFileTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal, AppPaths appPaths) : ITool
{
    private const int MaxLength = 2_000_000;

    public string Name => "crear_archivo";
    public string Description => "Crea un archivo de texto con el contenido indicado (notas, listas, código, .txt, .md, .csv...).";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("ruta", String("Ruta completa del archivo, con extensión, p. ej. C:\\Users\\Ana\\Desktop\\lista.txt")),
        Required("contenido", String("Texto que se escribirá en el archivo")),
        Optional("sobrescribir", Boolean("true para reemplazar el archivo si ya existe (por defecto false)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (path, content, overwrite) = Read(arguments);
        var exists = File.Exists(path);
        if (exists && !overwrite)
            throw new ToolArgumentException($"El archivo {path} ya existe. Usa otro nombre o sobrescribir=true si el usuario quiere reemplazarlo.");

        var lines = content.Split('\n');
        var preview = lines.Take(8).Select(l => "  " + l.TrimEnd('\r')).ToList();
        if (lines.Length > 8)
            preview.Add($"  … ({lines.Length - 8} líneas más)");

        return new ToolPreview(
            exists ? $"Reemplazar el archivo «{FileToolHelpers.Display(path, folders)}»" : $"Crear el archivo «{FileToolHelpers.Display(path, folders)}»",
            exists ? ToolRisk.Dangerous : ToolRisk.Modify)
        {
            Details = preview,
            PermissionScope = exists ? null : $"{Name}:{Path.GetDirectoryName(path)}",
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (path, content, overwrite) = Read(arguments);
        var undo = new List<UndoStep>();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path))
        {
            if (!overwrite)
                return ToolResult.Fail($"El archivo {path} ya existe.");
            // Keep the old version so the change can be undone.
            var backup = Path.Combine(appPaths.TrashDirectory, "reemplazados", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Path.GetFileName(path)}");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(path, backup);
            undo.Add(new UndoStep(UndoKind.RestoreReplaced, backup, path));
        }
        else
        {
            undo.Add(new UndoStep(UndoKind.RemoveCreated, path));
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
        var entry = journal.Record(Name, $"Escribir {FileToolHelpers.Display(path, folders)}", true, undo);
        return ToolResult.Ok($"Archivo guardado: {path} ({content.Length} caracteres)") with { JournalEntryId = entry.Id };
    }

    private (string Path, string Content, bool Overwrite) Read(ToolArguments arguments)
    {
        var path = paths.Resolve(arguments.String("ruta"));
        paths.EnsureAllowed(path);
        FileToolHelpers.ValidateName(Path.GetFileName(path));
        if (FileToolHelpers.ExecutableExtensions.Contains(Path.GetExtension(path)))
            throw new ToolArgumentException("Por seguridad no creo archivos ejecutables (.exe, .bat, .ps1...). Para scripts usa la herramienta de PowerShell.");

        var content = arguments.OptionalString("contenido") ?? string.Empty;
        if (content.Length > MaxLength)
            throw new ToolArgumentException("El contenido es demasiado largo.");
        return (path, content.Replace("\\n", "\n"), arguments.Bool("sobrescribir"));
    }
}
