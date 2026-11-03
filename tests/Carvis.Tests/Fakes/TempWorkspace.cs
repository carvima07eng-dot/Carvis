using Carvis.Core.Configuration;
using Carvis.Core.Context;
using Carvis.Core.Platform;
using Carvis.Core.Tools;

namespace Carvis.Tests.Fakes;

/// <summary>A fake user profile in a temp folder, so tool tests never touch real files.</summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Directory.CreateTempSubdirectory("carvis-ws").FullName;
        Profile = Path.Combine(Root, "Carlos");
        Desktop = Path.Combine(Profile, "Desktop");
        Downloads = Path.Combine(Profile, "Downloads");
        Directory.CreateDirectory(Desktop);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Path.Combine(Profile, "AppData"));

        Folders = new UserFolders(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Perfil"] = Profile,
            ["Escritorio"] = Desktop,
            ["Descargas"] = Downloads,
        });
        AppPaths = new AppPaths(Path.Combine(Root, "data"), Path.Combine(Root, "settings"));
        Permissions = new PermissionsSettings();
        Paths = new PathPolicy(Folders, Permissions, AppPaths);
        RecycleBin = new TrashFolderRecycleBin(AppPaths);
        Journal = new ActionJournal(new InMemoryJournalStore(), RecycleBin, TimeProvider.System);
    }

    public string Root { get; }
    public string Profile { get; }
    public string Desktop { get; }
    public string Downloads { get; }
    public UserFolders Folders { get; }
    public AppPaths AppPaths { get; }
    public PermissionsSettings Permissions { get; }
    public PathPolicy Paths { get; }
    public TrashFolderRecycleBin RecycleBin { get; }
    public ActionJournal Journal { get; }

    public string File(string relative, string content = "x")
    {
        var path = Path.Combine(Profile, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public static ToolArguments Args(string json) => new(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());

    public static string Json(string path) => System.Text.Json.JsonSerializer.Serialize(path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
