using Carvis.Core.Configuration;
using Carvis.Core.Context;

namespace Carvis.Core.Tools;

public interface IPathPolicy
{
    IReadOnlyList<string> AllowedRoots { get; }

    /// <summary>Turns what the model wrote ("escritorio\notas", "~/x", "%USERPROFILE%\y") into a full path.</summary>
    string Resolve(string path);

    /// <summary>Throws <see cref="ToolArgumentException"/> when Carvis must not touch the path.</summary>
    void EnsureAllowed(string fullPath);

    bool IsAllowed(string fullPath);
}

public sealed class PathPolicy : IPathPolicy
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IUserFolders _folders;
    private readonly PermissionsSettings _settings;
    private readonly List<string> _forbidden;

    public PathPolicy(IUserFolders folders, PermissionsSettings settings, AppPaths appPaths)
    {
        _folders = folders;
        _settings = settings;

        var profile = folders.Resolve("perfil") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _forbidden = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                string.IsNullOrEmpty(profile) ? string.Empty : Path.Combine(profile, "AppData"),
                appPaths.DataRoot,
                appPaths.SettingsRoot,
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .ToList();
    }

    public IReadOnlyList<string> AllowedRoots
    {
        get
        {
            var configured = _settings.AllowedFolders.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (configured.Count == 0 && _folders.Resolve("perfil") is { } profile)
                configured.Add(profile);
            return configured.Select(f => Normalize(Expand(f))).ToList();
        }
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ToolArgumentException("Falta la ruta.");

        var expanded = Expand(path.Trim().Trim('"'));
        if (!Path.IsPathRooted(expanded))
        {
            throw new ToolArgumentException(
                $"La ruta «{path}» no es completa. Usa una ruta completa a partir de las carpetas del usuario " +
                $"(por ejemplo {_folders.Resolve("escritorio") ?? "C:\\Users\\...\\Desktop"}).");
        }

        return Normalize(expanded);
    }

    public void EnsureAllowed(string fullPath)
    {
        if (!IsAllowed(fullPath))
        {
            throw new ToolArgumentException(
                $"No tengo permiso para tocar «{fullPath}». Solo puedo trabajar en: {string.Join(", ", AllowedRoots)}. " +
                "Se pueden añadir carpetas en Ajustes → Permisos.");
        }
    }

    public bool IsAllowed(string fullPath)
    {
        var path = Normalize(fullPath);
        if (_forbidden.Any(f => IsInside(path, f)))
            return false;
        return AllowedRoots.Any(root => IsInside(path, root));
    }

    private string Expand(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
            path = (_folders.Resolve("perfil") ?? string.Empty) + path[1..];

        // "escritorio\notas" or "Descargas/fotos": first segment is a known folder name.
        var separator = path.IndexOfAny(['/', '\\']);
        var first = separator < 0 ? path : path[..separator];
        if (!Path.IsPathRooted(path) && _folders.Resolve(first) is { } folder)
            path = separator < 0 ? folder : Path.Combine(folder, path[(separator + 1)..]);

        return path;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        return full.Length > Path.GetPathRoot(full)!.Length ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
    }

    private static bool IsInside(string path, string root) =>
        path.Equals(root, PathComparison) ||
        path.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, PathComparison);
}
