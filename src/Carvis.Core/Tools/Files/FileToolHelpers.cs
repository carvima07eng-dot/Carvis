using Carvis.Core.Context;

namespace Carvis.Core.Tools.Files;

internal static class FileToolHelpers
{
    public static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".hta", ".cpl", ".jar", ".reg",
    };

    private static readonly string[] SkippedFolders = ["node_modules", ".git", "bin", "obj", "AppData", "$Recycle.Bin", ".vs", ".idea", "__pycache__"];

    /// <summary>"C:\Users\Carlos\Desktop\x" → "Escritorio\x", easier to read in confirmations.</summary>
    public static string Display(string path, IUserFolders folders)
    {
        foreach (var (name, root) in folders.All.Where(f => f.Key != "Perfil").OrderByDescending(f => f.Value.Length))
        {
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
                return name;
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return name + path[root.Length..];
        }
        return path;
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static void EnsureExists(string path)
    {
        if (!Exists(path))
            throw new ToolArgumentException($"No existe «{path}». Comprueba la ruta (puedes listar la carpeta para ver qué hay).");
    }

    /// <summary>"informe.pdf" → "informe (2).pdf" when the name is taken.</summary>
    public static string UniquePath(string path)
    {
        if (!Exists(path))
            return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Directory.Exists(path) ? string.Empty : Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!Exists(candidate))
                return candidate;
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToArray()) >= 0)
            throw new ToolArgumentException($"«{name}» no es un nombre válido (no puede contener \\ / : * ? \" < > |).");
    }

    /// <summary>Walks a folder tree skipping system and dependency folders.</summary>
    public static IEnumerable<FileInfo> EnumerateFiles(string root, bool recursive, int maxEntries = 100_000)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        var count = 0;

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (++count > maxEntries)
                    yield break;
                if (entry.Attributes.HasFlag(FileAttributes.Hidden) || entry.Attributes.HasFlag(FileAttributes.System))
                    continue;

                if (entry is DirectoryInfo sub)
                {
                    if (recursive && !SkippedFolders.Contains(sub.Name, StringComparer.OrdinalIgnoreCase) && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        pending.Push(sub);
                }
                else if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }
    }

    public static string Category(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".svg" or ".tiff" or ".raw" => "Imágenes",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".flv" => "Vídeos",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" or ".wma" => "Música",
        ".pdf" or ".doc" or ".docx" or ".odt" or ".rtf" or ".txt" or ".md" or ".xls" or ".xlsx" or ".ods" or ".csv" or ".ppt" or ".pptx" or ".odp" => "Documentos",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "Comprimidos",
        ".exe" or ".msi" or ".msix" or ".appx" => "Instaladores",
        ".cs" or ".java" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".sql" or ".json" or ".xml" or ".cpp" or ".c" or ".php" => "Código",
        _ => "Otros",
    };
}
