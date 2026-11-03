using System.Text.Json.Nodes;
using Carvis.Core.Context;
using Carvis.Core.Platform;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Files;

[BuiltInTool]
public sealed class MoveTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "mover";
    public string Description => "Mueve uno o varios archivos o carpetas a otra carpeta (la crea si no existe).";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("origen", StringArray("Rutas completas de los archivos o carpetas a mover")),
        Required("destino", String("Ruta completa de la carpeta de destino")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (sources, destination) = Read(arguments);
        return new ToolPreview(
            sources.Count == 1
                ? $"Mover «{FileToolHelpers.Display(sources[0], folders)}» a «{FileToolHelpers.Display(destination, folders)}»"
                : $"Mover {sources.Count} elementos a «{FileToolHelpers.Display(destination, folders)}»",
            ToolRisk.Modify)
        {
            Details = sources.Take(15).Select(s => "  " + FileToolHelpers.Display(s, folders)).ToList(),
            PermissionScope = $"{Name}:{destination}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (sources, destination) = Read(arguments);
        var undo = new List<UndoStep>();
        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(destination);
            undo.Add(new UndoStep(UndoKind.RemoveCreated, destination));
        }

        var moved = new List<string>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = FileToolHelpers.UniquePath(Path.Combine(destination, Path.GetFileName(source)));
            if (Directory.Exists(source))
                Directory.Move(source, target);
            else
                File.Move(source, target);
            undo.Add(new UndoStep(UndoKind.MoveBack, target, source));
            moved.Add(target);
        }

        var entry = journal.Record(Name, $"Mover {moved.Count} elementos a {FileToolHelpers.Display(destination, folders)}", true, undo);
        return Task.FromResult(ToolResult.Ok($"Movidos {moved.Count} elementos a {destination}:\n{string.Join('\n', moved)}") with { JournalEntryId = entry.Id });
    }

    private (IReadOnlyList<string> Sources, string Destination) Read(ToolArguments arguments)
    {
        var sources = arguments.StringList("origen").Select(paths.Resolve).ToList();
        var destination = paths.Resolve(arguments.String("destino"));
        foreach (var source in sources)
        {
            paths.EnsureAllowed(source);
            FileToolHelpers.EnsureExists(source);
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination == source)
                throw new ToolArgumentException("No puedo mover una carpeta dentro de sí misma.");
        }
        paths.EnsureAllowed(destination);
        return (sources, destination);
    }
}

[BuiltInTool]
public sealed class CopyTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "copiar";
    public string Description => "Copia uno o varios archivos o carpetas a otra carpeta.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("origen", StringArray("Rutas completas de lo que se copia")),
        Required("destino", String("Ruta completa de la carpeta de destino")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (sources, destination) = Read(arguments);
        return new ToolPreview($"Copiar {sources.Count} elemento(s) a «{FileToolHelpers.Display(destination, folders)}»", ToolRisk.Modify)
        {
            Details = sources.Take(15).Select(s => "  " + FileToolHelpers.Display(s, folders)).ToList(),
            PermissionScope = $"{Name}:{destination}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (sources, destination) = Read(arguments);
        return Task.Run(() =>
        {
            var undo = new List<UndoStep>();
            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
                undo.Add(new UndoStep(UndoKind.RemoveCreated, destination));
            }

            var copies = new List<string>();
            foreach (var source in sources)
            {
                var target = FileToolHelpers.UniquePath(Path.Combine(destination, Path.GetFileName(source)));
                if (Directory.Exists(source))
                    CopyDirectory(source, target, cancellationToken);
                else
                    File.Copy(source, target);
                undo.Add(new UndoStep(UndoKind.RemoveCreated, target));
                copies.Add(target);
            }

            var entry = journal.Record(Name, $"Copiar {copies.Count} elementos a {FileToolHelpers.Display(destination, folders)}", true, undo);
            return ToolResult.Ok($"Copiados {copies.Count} elementos:\n{string.Join('\n', copies)}") with { JournalEntryId = entry.Id };
        }, cancellationToken);
    }

    private static void CopyDirectory(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), cancellationToken);
    }

    private (IReadOnlyList<string> Sources, string Destination) Read(ToolArguments arguments)
    {
        var sources = arguments.StringList("origen").Select(paths.Resolve).ToList();
        var destination = paths.Resolve(arguments.String("destino"));
        foreach (var source in sources)
        {
            paths.EnsureAllowed(source);
            FileToolHelpers.EnsureExists(source);
        }
        paths.EnsureAllowed(destination);
        return (sources, destination);
    }
}

[BuiltInTool]
public sealed class RenameTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "renombrar";
    public string Description => "Cambia el nombre de un archivo o carpeta sin moverlo de sitio.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("ruta", String("Ruta completa del archivo o carpeta")),
        Required("nuevo_nombre", String("Nombre nuevo, solo el nombre (con extensión si es un archivo)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (path, target) = Read(arguments);
        return new ToolPreview($"Renombrar «{FileToolHelpers.Display(path, folders)}» a «{Path.GetFileName(target)}»", ToolRisk.Modify)
        {
            PermissionScope = $"{Name}:{Path.GetDirectoryName(path)}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (path, target) = Read(arguments);
        if (Directory.Exists(path))
            Directory.Move(path, target);
        else
            File.Move(path, target);

        var entry = journal.Record(Name, $"Renombrar {Path.GetFileName(path)} a {Path.GetFileName(target)}", true, [new UndoStep(UndoKind.MoveBack, target, path)]);
        return Task.FromResult(ToolResult.Ok($"Renombrado: {path} → {target}") with { JournalEntryId = entry.Id });
    }

    private (string Path, string Target) Read(ToolArguments arguments)
    {
        var path = paths.Resolve(arguments.String("ruta"));
        paths.EnsureAllowed(path);
        FileToolHelpers.EnsureExists(path);
        var name = arguments.String("nuevo_nombre");
        FileToolHelpers.ValidateName(name);

        // Keep the extension if the model forgot it.
        if (File.Exists(path) && Path.GetExtension(name).Length == 0)
            name += Path.GetExtension(path);

        var target = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (FileToolHelpers.Exists(target) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
            throw new ToolArgumentException($"Ya existe «{name}» en esa carpeta.");
        return (path, target);
    }
}

[BuiltInTool]
public sealed class RecycleTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal, IRecycleBin recycleBin) : ITool
{
    public string Name => "enviar_a_papelera";
    public string Description => "Envía archivos o carpetas a la papelera de reciclaje (se pueden recuperar desde allí). Nunca borra definitivamente.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(Required("rutas", StringArray("Rutas completas de lo que se envía a la papelera")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var items = Read(arguments);
        return new ToolPreview(
            items.Count == 1 ? $"Enviar «{FileToolHelpers.Display(items[0], folders)}» a la papelera" : $"Enviar {items.Count} elementos a la papelera",
            ToolRisk.Dangerous)
        {
            Details = items.Take(20).Select(i => "  " + FileToolHelpers.Display(i, folders) + (Directory.Exists(i) ? " (carpeta con todo su contenido)" : string.Empty)).ToList(),
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var items = Read(arguments);
        var result = await recycleBin.SendAsync(items, cancellationToken);
        var undo = result.TrashLocations?.Select(p => new UndoStep(UndoKind.MoveBack, p.Value, p.Key)).ToList() ?? [];
        var entry = journal.Record(Name, $"Enviar {items.Count} elementos a la papelera", true, undo);
        var note = recycleBin.CanRestore ? string.Empty : " Se pueden recuperar desde la Papelera de reciclaje.";
        return ToolResult.Ok($"Enviados a la papelera: {string.Join(", ", items)}.{note}") with { JournalEntryId = entry.Id };
    }

    private IReadOnlyList<string> Read(ToolArguments arguments)
    {
        var items = arguments.StringList("rutas").Select(paths.Resolve).ToList();
        foreach (var item in items)
        {
            paths.EnsureAllowed(item);
            FileToolHelpers.EnsureExists(item);
            if (folders.All.Values.Any(f => string.Equals(f, item, StringComparison.OrdinalIgnoreCase)))
                throw new ToolArgumentException($"No voy a borrar una carpeta principal del usuario ({item}).");
        }
        return items;
    }
}

[BuiltInTool]
public sealed class OpenFileTool(IPathPolicy paths, IUserFolders folders, IShell shell) : ITool
{
    public string Name => "abrir_archivo";
    public string Description => "Abre un archivo o carpeta con el programa predeterminado (un PDF con el lector, una carpeta en el Explorador...).";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(Required("ruta", String("Ruta completa del archivo o carpeta")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var path = Resolve(arguments);
        var executable = FileToolHelpers.ExecutableExtensions.Contains(Path.GetExtension(path));
        return new ToolPreview(
            executable ? $"Ejecutar «{FileToolHelpers.Display(path, folders)}»" : $"Abrir «{FileToolHelpers.Display(path, folders)}»",
            executable ? ToolRisk.Dangerous : ToolRisk.Low)
        {
            Details = executable ? ["  Es un programa o script: se ejecutará con tus permisos."] : [],
            PermissionScope = executable ? null : Name,
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var path = Resolve(arguments);
        await shell.OpenAsync(path, cancellationToken);
        return ToolResult.Ok($"Abierto: {path}");
    }

    private string Resolve(ToolArguments arguments)
    {
        var path = paths.Resolve(arguments.String("ruta"));
        paths.EnsureAllowed(path);
        FileToolHelpers.EnsureExists(path);
        return path;
    }
}

[BuiltInTool]
public sealed class RevealTool(IPathPolicy paths, IUserFolders folders, IShell shell) : ITool
{
    public string Name => "mostrar_en_explorador";
    public string Description => "Abre el Explorador de archivos con el archivo o carpeta seleccionado.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(Required("ruta", String("Ruta completa")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Mostrar «{FileToolHelpers.Display(Resolve(arguments), folders)}» en el Explorador", ToolRisk.Low) { PermissionScope = Name };

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var path = Resolve(arguments);
        await shell.RevealAsync(path, cancellationToken);
        return ToolResult.Ok($"Mostrado en el Explorador: {path}");
    }

    private string Resolve(ToolArguments arguments)
    {
        var path = paths.Resolve(arguments.String("ruta"));
        paths.EnsureAllowed(path);
        FileToolHelpers.EnsureExists(path);
        return path;
    }
}
