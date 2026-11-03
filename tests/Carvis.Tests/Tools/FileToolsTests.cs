using Carvis.Core.Indexing;
using Carvis.Core.Indexing.Readers;
using Carvis.Core.Platform;
using Carvis.Core.Tools;
using Carvis.Core.Tools.Files;
using Carvis.Tests.Fakes;
using static Carvis.Tests.Fakes.TempWorkspace;

namespace Carvis.Tests.Tools;

public sealed class FileToolsTests : IDisposable
{
    private readonly TempWorkspace _ws = new();
    private readonly ToolContext _context = ToolContext.Default;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task CreateFolder_CreatesItAndCanBeUndone()
    {
        var tool = new CreateFolderTool(_ws.Paths, _ws.Folders, _ws.Journal);
        var target = Path.Combine(_ws.Desktop, "MaikPedernal");
        var args = Args($$"""{"ruta": {{Json(target)}}}""");

        var preview = tool.Preview(args, _context);
        var result = await tool.ExecuteAsync(args, _context);

        Assert.Equal("Crear la carpeta «Escritorio" + Path.DirectorySeparatorChar + "MaikPedernal»", preview.Summary);
        Assert.Equal(ToolRisk.Modify, preview.Risk);
        Assert.True(result.Success);
        Assert.True(Directory.Exists(target));

        await _ws.Journal.UndoAsync(result.JournalEntryId!);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void CreateFolder_RefusesForbiddenPlaces()
    {
        var tool = new CreateFolderTool(_ws.Paths, _ws.Folders, _ws.Journal);
        var args = Args($$"""{"ruta": {{Json(Path.Combine(_ws.Profile, "AppData", "Malo"))}}}""");

        Assert.Throws<ToolArgumentException>(() => tool.Preview(args, _context));
    }

    [Fact]
    public async Task CreateFile_WritesContentAndRestoresTheOldVersionOnUndo()
    {
        var tool = new CreateFileTool(_ws.Paths, _ws.Folders, _ws.Journal, _ws.AppPaths);
        var path = _ws.File("Desktop/lista.txt", "versión vieja");
        var args = Args($$"""{"ruta": {{Json(path)}}, "contenido": "pan\nleche", "sobrescribir": true}""");

        Assert.Equal(ToolRisk.Dangerous, tool.Preview(args, _context).Risk);
        var result = await tool.ExecuteAsync(args, _context);
        Assert.Equal("pan\nleche", File.ReadAllText(path));

        await _ws.Journal.UndoAsync(result.JournalEntryId!);
        Assert.Equal("versión vieja", File.ReadAllText(path));
    }

    [Fact]
    public void CreateFile_WithoutOverwriteFailsWhenTheFileExists()
    {
        var tool = new CreateFileTool(_ws.Paths, _ws.Folders, _ws.Journal, _ws.AppPaths);
        var path = _ws.File("Desktop/lista.txt");

        Assert.Throws<ToolArgumentException>(() => tool.Preview(Args($$"""{"ruta": {{Json(path)}}, "contenido": "x"}"""), _context));
    }

    [Fact]
    public void CreateFile_RefusesExecutables()
    {
        var tool = new CreateFileTool(_ws.Paths, _ws.Folders, _ws.Journal, _ws.AppPaths);
        var args = Args($$"""{"ruta": {{Json(Path.Combine(_ws.Desktop, "virus.bat"))}}, "contenido": "del *"}""");

        Assert.Throws<ToolArgumentException>(() => tool.Preview(args, _context));
    }

    [Fact]
    public async Task Move_MovesSeveralFilesAndUndoPutsThemBack()
    {
        var tool = new MoveTool(_ws.Paths, _ws.Folders, _ws.Journal);
        var a = _ws.File("Downloads/a.pdf");
        var b = _ws.File("Downloads/b.pdf");
        var destination = Path.Combine(_ws.Desktop, "PDFs");
        var args = Args($$"""{"origen": [{{Json(a)}}, {{Json(b)}}], "destino": {{Json(destination)}}}""");

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(File.Exists(Path.Combine(destination, "a.pdf")));
        Assert.False(File.Exists(a));

        await _ws.Journal.UndoAsync(result.JournalEntryId!);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Move_KeepsBothFilesWhenTheNameIsTaken()
    {
        var tool = new MoveTool(_ws.Paths, _ws.Folders, _ws.Journal);
        var source = _ws.File("Downloads/foto.jpg", "nueva");
        _ws.File("Desktop/foto.jpg", "vieja");

        await tool.ExecuteAsync(Args($$"""{"origen": {{Json(source)}}, "destino": {{Json(_ws.Desktop)}}}"""), _context);

        Assert.Equal("vieja", File.ReadAllText(Path.Combine(_ws.Desktop, "foto.jpg")));
        Assert.Equal("nueva", File.ReadAllText(Path.Combine(_ws.Desktop, "foto (2).jpg")));
    }

    [Fact]
    public async Task Rename_KeepsTheExtensionIfMissingAndCanBeUndone()
    {
        var tool = new RenameTool(_ws.Paths, _ws.Folders, _ws.Journal);
        var path = _ws.File("Desktop/informe.docx");

        var result = await tool.ExecuteAsync(Args($$"""{"ruta": {{Json(path)}}, "nuevo_nombre": "informe final"}"""), _context);

        Assert.True(File.Exists(Path.Combine(_ws.Desktop, "informe final.docx")));
        await _ws.Journal.UndoAsync(result.JournalEntryId!);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Recycle_IsDangerousAndRestorableFromCarvisTrash()
    {
        var tool = new RecycleTool(_ws.Paths, _ws.Folders, _ws.Journal, _ws.RecycleBin);
        var path = _ws.File("Desktop/viejo.txt");
        var args = Args($$"""{"rutas": [{{Json(path)}}]}""");

        Assert.Equal(ToolRisk.Dangerous, tool.Preview(args, _context).Risk);
        var result = await tool.ExecuteAsync(args, _context);
        Assert.False(File.Exists(path));

        await _ws.Journal.UndoAsync(result.JournalEntryId!);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Recycle_RefusesTheUserMainFolders()
    {
        var tool = new RecycleTool(_ws.Paths, _ws.Folders, _ws.Journal, _ws.RecycleBin);

        Assert.Throws<ToolArgumentException>(() => tool.Preview(Args($$"""{"rutas": [{{Json(_ws.Desktop)}}]}"""), _context));
    }

    [Fact]
    public async Task ListFolder_ShowsFoldersAndFiles()
    {
        Directory.CreateDirectory(Path.Combine(_ws.Desktop, "Clase"));
        _ws.File("Desktop/notas.txt", "hola");
        var tool = new ListFolderTool(_ws.Paths, _ws.Folders);

        var result = await tool.ExecuteAsync(Args($$"""{"ruta": "escritorio"}"""), _context);

        Assert.Contains("[carpeta] Clase", result.Output);
        Assert.Contains("notas.txt", result.Output);
    }

    [Fact]
    public async Task SearchFiles_FiltersByNameAndExtension()
    {
        _ws.File("Desktop/Tema1.pdf");
        _ws.File("Desktop/Clase/Tema2.pdf");
        _ws.File("Desktop/Tema3.docx");
        var tool = new SearchFilesTool(_ws.Paths, _ws.Folders);

        var result = await tool.ExecuteAsync(Args("""{"carpeta": "escritorio", "nombre": "tema", "extensiones": ["pdf"]}"""), _context);

        Assert.Contains("Tema1.pdf", result.Output);
        Assert.Contains("Tema2.pdf", result.Output);
        Assert.DoesNotContain("Tema3.docx", result.Output);
    }

    [Fact]
    public async Task ReadFile_MarksTheContentAsExternal()
    {
        var path = _ws.File("Desktop/apuntes.md", "# Tema 1\nLas clases en C#");
        var tool = new ReadFileTool(_ws.Paths, _ws.Folders, new DocumentTextExtractor([new TextDocumentReader()]));

        var result = await tool.ExecuteAsync(Args($$"""{"ruta": {{Json(path)}}}"""), _context);

        Assert.True(result.ContainsExternalContent);
        Assert.Contains("Las clases en C#", result.Output);
    }

    [Fact]
    public async Task ZipAndUnzip_RoundTrip()
    {
        _ws.File("Desktop/Fotos/a.jpg", "A");
        _ws.File("Desktop/Fotos/b.jpg", "B");
        var zip = Path.Combine(_ws.Desktop, "fotos.zip");

        await new ZipTool(_ws.Paths, _ws.Folders, _ws.Journal)
            .ExecuteAsync(Args($$"""{"rutas": [{{Json(Path.Combine(_ws.Desktop, "Fotos"))}}], "zip": {{Json(zip)}}}"""), _context);
        await new UnzipTool(_ws.Paths, _ws.Folders, _ws.Journal)
            .ExecuteAsync(Args($$"""{"zip": {{Json(zip)}}, "destino": {{Json(Path.Combine(_ws.Downloads, "x"))}}}"""), _context);

        Assert.Equal("B", File.ReadAllText(Path.Combine(_ws.Downloads, "x", "Fotos", "b.jpg")));
    }

    [Fact]
    public async Task OrganizeFolder_GroupsByTypeAndUndoRestoresEverything()
    {
        var photo = _ws.File("Downloads/foto.jpg");
        var doc = _ws.File("Downloads/trabajo.pdf");
        var tool = new OrganizeFolderTool(_ws.Paths, _ws.Folders, _ws.Journal);
        var args = Args("""{"carpeta": "descargas"}""");

        Assert.Contains(tool.Preview(args, _context).Details, d => d.Contains("Imágenes: 1"));
        var result = await tool.ExecuteAsync(args, _context);
        Assert.True(File.Exists(Path.Combine(_ws.Downloads, "Imágenes", "foto.jpg")));
        Assert.True(File.Exists(Path.Combine(_ws.Downloads, "Documentos", "trabajo.pdf")));

        await _ws.Journal.UndoAsync(result.JournalEntryId!);
        Assert.True(File.Exists(photo));
        Assert.True(File.Exists(doc));
        Assert.False(Directory.Exists(Path.Combine(_ws.Downloads, "Imágenes")));
    }

    [Fact]
    public async Task FindDuplicates_FindsFilesWithTheSameContent()
    {
        _ws.File("Desktop/a.txt", "mismo contenido");
        _ws.File("Desktop/Copia/a.txt", "mismo contenido");
        _ws.File("Desktop/b.txt", "otro contenido!!");

        var result = await new FindDuplicatesTool(_ws.Paths, _ws.Folders).ExecuteAsync(Args("""{"carpeta": "escritorio"}"""), _context);

        Assert.Contains("1 grupos", result.Output);
        Assert.DoesNotContain("b.txt", result.Output);
    }

    [Fact]
    public async Task UndoTool_UndoesTheLastAction()
    {
        var folder = Path.Combine(_ws.Desktop, "Temporal");
        await new CreateFolderTool(_ws.Paths, _ws.Folders, _ws.Journal).ExecuteAsync(Args($$"""{"ruta": {{Json(folder)}}}"""), _context);
        var undo = new UndoTool(_ws.Journal);

        Assert.Contains("Temporal", undo.Preview(Args("{}"), _context).Summary);
        await undo.ExecuteAsync(Args("{}"), _context);

        Assert.False(Directory.Exists(folder));
        Assert.Throws<ToolArgumentException>(() => undo.Preview(Args("{}"), _context));
    }
}
