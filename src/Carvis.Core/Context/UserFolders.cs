using System.Runtime.InteropServices;

namespace Carvis.Core.Context;

/// <summary>The user's well-known folders with their real paths (OneDrive redirection included).</summary>
public interface IUserFolders
{
    IReadOnlyDictionary<string, string> All { get; }

    /// <summary>Resolves "escritorio", "descargas"... to a path; null if the name is unknown.</summary>
    string? Resolve(string name);
}

public sealed class UserFolders : IUserFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["escritorio"] = "Escritorio", ["desktop"] = "Escritorio",
        ["documentos"] = "Documentos", ["mis documentos"] = "Documentos", ["documents"] = "Documentos",
        ["descargas"] = "Descargas", ["downloads"] = "Descargas",
        ["imágenes"] = "Imágenes", ["imagenes"] = "Imágenes", ["fotos"] = "Imágenes", ["pictures"] = "Imágenes",
        ["música"] = "Música", ["musica"] = "Música", ["music"] = "Música",
        ["vídeos"] = "Vídeos", ["videos"] = "Vídeos",
        ["perfil"] = "Perfil", ["carpeta personal"] = "Perfil", ["home"] = "Perfil", ["~"] = "Perfil",
    };

    public UserFolders(IReadOnlyDictionary<string, string>? folders = null)
    {
        All = folders ?? Detect();
    }

    public IReadOnlyDictionary<string, string> All { get; }

    public string? Resolve(string name)
    {
        var key = name.Trim().Trim('/', '\\');
        if (Aliases.TryGetValue(key, out var canonical))
            key = canonical;
        return All.TryGetValue(key, out var path) ? path : null;
    }

    private static Dictionary<string, string> Detect()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Perfil"] = profile,
            ["Escritorio"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ["Documentos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["Descargas"] = DownloadsFolder(profile),
            ["Imágenes"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            ["Música"] = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            ["Vídeos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };

        foreach (var key in folders.Where(f => string.IsNullOrEmpty(f.Value)).Select(f => f.Key).ToList())
            folders.Remove(key);
        return folders;
    }

    private static string DownloadsFolder(string profile)
    {
        if (OperatingSystem.IsWindows() && SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var path) == 0)
            return path;
        return Path.Combine(profile, "Downloads");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out string path);
}
