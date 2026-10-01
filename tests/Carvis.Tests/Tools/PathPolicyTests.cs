using Carvis.Core.Tools;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Tools;

public sealed class PathPolicyTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void Resolve_UnderstandsFolderNamesAndHome()
    {
        Assert.Equal(Path.Combine(_ws.Desktop, "Notas"), _ws.Paths.Resolve("escritorio/Notas"));
        Assert.Equal(Path.Combine(_ws.Downloads, "a.pdf"), _ws.Paths.Resolve("Descargas\\a.pdf"));
        Assert.Equal(_ws.Desktop, _ws.Paths.Resolve("Escritorio"));
    }

    [Fact]
    public void Resolve_RejectsRelativePaths()
    {
        var error = Assert.Throws<ToolArgumentException>(() => _ws.Paths.Resolve("carpeta/x"));
        Assert.Contains("ruta completa", error.Message);
    }

    [Fact]
    public void Allows_TheProfileButNotAppDataOrCarvisData()
    {
        Assert.True(_ws.Paths.IsAllowed(Path.Combine(_ws.Desktop, "x")));
        Assert.True(_ws.Paths.IsAllowed(Path.Combine(_ws.Profile, "Proyectos", "y")));
        Assert.False(_ws.Paths.IsAllowed(Path.Combine(_ws.Profile, "AppData", "Roaming")));
        Assert.False(_ws.Paths.IsAllowed(_ws.AppPaths.DatabaseFile));
        Assert.False(_ws.Paths.IsAllowed(Path.Combine(_ws.Root, "otro-usuario")));
    }

    [Fact]
    public void Allows_OnlyTheConfiguredFoldersWhenThereAreAny()
    {
        _ws.Permissions.AllowedFolders.Add(_ws.Downloads);

        Assert.True(_ws.Paths.IsAllowed(Path.Combine(_ws.Downloads, "a")));
        Assert.False(_ws.Paths.IsAllowed(Path.Combine(_ws.Desktop, "a")));
    }

    [Fact]
    public void Rejects_PathsThatEscapeWithDotDot()
    {
        var sneaky = _ws.Paths.Resolve(Path.Combine(_ws.Desktop, "..", "AppData", "x"));
        Assert.False(_ws.Paths.IsAllowed(sneaky));
    }
}
