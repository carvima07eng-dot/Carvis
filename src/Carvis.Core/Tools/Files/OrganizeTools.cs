using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Carvis.Core.Context;
using static Carvis.Core.Tools.Schema;

namespace Carvis.Core.Tools.Files;

[BuiltInTool]
public sealed class OrganizeFolderTool(IPathPolicy paths, IUserFolders folders, IActionJournal journal) : ITool
{
    public string Name => "organizar_carpeta";
    public string Description =>
        "Ordena los archivos sueltos de una carpeta en subcarpetas por tipo (Imágenes, Documentos, Vídeos...) o por fecha (año-mes).";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("carpeta", String("Ruta completa de la carpeta a ordenar")),
        Optional("criterio", String("tipo o fecha (por defecto tipo)", "tipo", "fecha")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context)
    {
        var (folder, plan) = Plan(arguments);
        if (plan.Count == 0)
            throw new ToolArgumentException($"No hay archivos sueltos que ordenar en {folder}.");

        return new ToolPreview($"Ordenar {plan.Count} archivos de «{FileToolHelpers.Display(folder, folders)}»", ToolRisk.Modify)
        {
            Details = plan.GroupBy(p => p.Group).OrderByDescending(g => g.Count())
                .Select(g => $"  {g.Key}: {g.Count()} archivos").ToList(),
            PermissionScope = $"{Name}:{folder}",
        };
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var (folder, plan) = Plan(arguments);
        var undo = new List<UndoStep>();

        foreach (var group in plan.GroupBy(p => p.Group))
        {
            var target = Path.Combine(folder, group.Key);
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
                undo.Add(new UndoStep(UndoKind.RemoveCreated, target));
            }
            foreach (var (file, _) in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = FileToolHelpers.UniquePath(Path.Combine(target, Path.GetFileName(file)));
                File.Move(file, destination);
                undo.Add(new UndoStep(UndoKind.MoveBack, destination, file));
            }
        }

        var summary = string.Join(", ", plan.GroupBy(p => p.Group).Select(g => $"{g.Key} ({g.Count()})"));
        var entry = journal.Record(Name, $"Ordenar {FileToolHelpers.Display(folder, folders)}", true, undo);
        return Task.FromResult(ToolResult.Ok($"Ordenados {plan.Count} archivos en {folder}: {summary}") with { JournalEntryId = entry.Id });
    }

    private (string Folder, List<(string File, string Group)> Plan) Plan(ToolArguments arguments)
    {
        var folder = paths.Resolve(arguments.String("carpeta"));
        paths.EnsureAllowed(folder);
        if (!Directory.Exists(folder))
            throw new ToolArgumentException($"No existe la carpeta {folder}.");

        var byDate = string.Equals(arguments.OptionalString("criterio"), "fecha", StringComparison.OrdinalIgnoreCase);
        var plan = new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(f => (f.FullName, byDate ? f.LastWriteTime.ToString("yyyy-MM") : FileToolHelpers.Category(f.Extension)))
            .ToList();
        return (folder, plan);
    }
}

[BuiltInTool]
public sealed class FindDuplicatesTool(IPathPolicy paths, IUserFolders folders) : ITool
{
    public string Name => "buscar_duplicados";
    public string Description => "Busca archivos repetidos (mismo contenido) dentro de una carpeta y sus subcarpetas.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(Required("carpeta", String("Ruta completa de la carpeta")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Buscar duplicados en «{FileToolHelpers.Display(Resolve(arguments), folders)}»", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var folder = Resolve(arguments);
        return Task.Run(() =>
        {
            // Same size first (cheap), then same hash.
            var groups = FileToolHelpers.EnumerateFiles(folder, recursive: true)
                .Where(f => f.Length > 0)
                .GroupBy(f => f.Length)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.GroupBy(f => Hash(f.FullName, cancellationToken)).Where(h => h.Count() > 1))
                .OrderByDescending(g => g.First().Length * (g.Count() - 1))
                .Take(30)
                .ToList();

            if (groups.Count == 0)
                return ToolResult.Ok("No hay archivos duplicados.");

            var wasted = groups.Sum(g => g.First().Length * (g.Count() - 1));
            var text = new StringBuilder($"{groups.Count} grupos de duplicados (se liberarían {FileToolHelpers.FormatSize(wasted)}):\n");
            foreach (var group in groups)
            {
                text.Append($"- {FileToolHelpers.FormatSize(group.First().Length)}:\n");
                foreach (var file in group)
                    text.Append("    ").Append(file.FullName).Append('\n');
            }
            return ToolResult.Ok(text.ToString().TrimEnd());
        }, cancellationToken);
    }

    private static string Hash(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Guid.NewGuid().ToString();
        }
    }

    private string Resolve(ToolArguments arguments)
    {
        var folder = paths.Resolve(arguments.String("carpeta"));
        paths.EnsureAllowed(folder);
        return folder;
    }
}

[BuiltInTool]
public sealed class LargestFilesTool(IPathPolicy paths, IUserFolders folders) : ITool
{
    public string Name => "archivos_grandes";
    public string Description => "Lista los archivos que más espacio ocupan dentro de una carpeta.";
    public string Category => "archivos";
    public JsonObject Parameters { get; } = Object(
        Required("carpeta", String("Ruta completa de la carpeta")),
        Optional("cantidad", Integer("Cuántos mostrar (por defecto 15)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Buscar los archivos más grandes de «{FileToolHelpers.Display(Resolve(arguments), folders)}»", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default)
    {
        var folder = Resolve(arguments);
        var count = arguments.Int("cantidad", 15, 1, 100);
        return Task.Run(() =>
        {
            var files = FileToolHelpers.EnumerateFiles(folder, recursive: true).OrderByDescending(f => f.Length).Take(count).ToList();
            var text = new StringBuilder($"Los {files.Count} archivos más grandes de {folder}:\n");
            foreach (var file in files)
                text.Append(FileToolHelpers.FormatSize(file.Length)).Append(" — ").Append(file.FullName).Append('\n');
            return ToolResult.Ok(text.ToString().TrimEnd());
        }, cancellationToken);
    }

    private string Resolve(ToolArguments arguments)
    {
        var folder = paths.Resolve(arguments.String("carpeta"));
        paths.EnsureAllowed(folder);
        return folder;
    }
}
