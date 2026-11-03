using System.IO.Compression;
using System.Text.Json.Nodes;
using Carvis.Core.Context;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Files;

[BuiltInTool]
public sealed class ZipTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "comprimir";
    public string Description => "Crea un archivo ZIP con los archivos o carpetas indicados.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("rutas", StringArray("Rutas completas de lo que se comprime")),
        Required("zip", String("Ruta completa del ZIP a crear, p. ej. C:\\Users\\Ana\\Desktop\\fotos.zip")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (items, zip) = Read(arguments);
        return new ToolPreview($"Comprimir {items.Count} elemento(s) en «{FileToolHelpers.Display(zip, folders)}»", ToolRisk.Modify)
        {
            Details = items.Take(15).Select(i => "  " + FileToolHelpers.Display(i, folders)).ToList(),
            PermissionScope = $"{Name}:{Path.GetDirectoryName(zip)}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (items, zip) = Read(arguments);
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var item in items)
                {
                    if (File.Exists(item))
                    {
                        archive.CreateEntryFromFile(item, Path.GetFileName(item), CompressionLevel.Optimal);
                        continue;
                    }
                    var root = Path.GetDirectoryName(item)!;
                    foreach (var file in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        archive.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
                    }
                }
            }

            var size = new FileInfo(zip).Length;
            var entry = journal.Record(Name, $"Crear {FileToolHelpers.Display(zip, folders)}", true, [new UndoStep(UndoKind.RemoveCreated, zip)]);
            return ToolResult.Ok($"ZIP creado: {zip} ({FileToolHelpers.FormatSize(size)})") with { JournalEntryId = entry.Id };
        }, cancellationToken);
    }

    private (IReadOnlyList<string> Items, string Zip) Read(ToolArguments arguments)
    {
        var items = arguments.StringList("rutas").Select(paths.Resolve).ToList();
        foreach (var item in items)
        {
            paths.EnsureAllowed(item);
            FileToolHelpers.EnsureExists(item);
        }

        var zip = paths.Resolve(arguments.String("zip"));
        if (!zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            zip += ".zip";
        paths.EnsureAllowed(zip);
        if (File.Exists(zip))
            throw new ToolArgumentException($"Ya existe {zip}. Elige otro nombre.");
        return (items, zip);
    }
}

[BuiltInTool]
public sealed class UnzipTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "descomprimir";
    public string Description => "Extrae un archivo ZIP en una carpeta nueva (por defecto, con el mismo nombre que el ZIP).";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("zip", String("Ruta completa del archivo ZIP")),
        Optional("destino", String("Carpeta donde extraer (se crea si no existe)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (zip, destination) = Read(arguments);
        return new ToolPreview($"Descomprimir «{FileToolHelpers.Display(zip, folders)}» en «{FileToolHelpers.Display(destination, folders)}»", ToolRisk.Modify)
        {
            PermissionScope = $"{Name}:{Path.GetDirectoryName(destination)}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (zip, destination) = Read(arguments);
        return Task.Run(() =>
        {
            var undo = new List<UndoStep>();
            var existed = Directory.Exists(destination);
            Directory.CreateDirectory(destination);
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(zip);
            var count = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                // Zip slip: entries must stay inside the destination.
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                    continue;
                entry.ExtractToFile(target);
                if (existed)
                    undo.Add(new UndoStep(UndoKind.RemoveCreated, target));
                count++;
            }

            if (!existed)
                undo.Add(new UndoStep(UndoKind.RemoveCreated, destination));
            var journalEntry = journal.Record(Name, $"Descomprimir {Path.GetFileName(zip)}", true, undo);
            return ToolResult.Ok($"Extraídos {count} archivos en {destination}") with { JournalEntryId = journalEntry.Id };
        }, cancellationToken);
    }

    private (string Zip, string Destination) Read(ToolArguments arguments)
    {
        var zip = paths.Resolve(arguments.String("zip"));
        paths.EnsureAllowed(zip);
        if (!File.Exists(zip))
            throw new ToolArgumentException($"No existe {zip}.");

        var destination = arguments.OptionalString("destino") is { Length: > 0 } d
            ? paths.Resolve(d)
            : FileToolHelpers.UniquePath(Path.Combine(Path.GetDirectoryName(zip)!, Path.GetFileNameWithoutExtension(zip)));
        paths.EnsureAllowed(destination);
        return (zip, destination);
    }
}
