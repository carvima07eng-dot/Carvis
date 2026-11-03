using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Platform;
using Carvis.Core.Tools;
using Carvis.Core.Tools.Files;
using Carvis.Core.Tools.Programs;
using Carvis.Tests.Fakes;
using static Carvis.Tests.Fakes.TempWorkspace;

namespace Carvis.Tests.Tools;

public class ToolArgumentsTests
{
    [Fact]
    public void ToleratesWhatSmallModelsGetWrong()
    {
        var args = Args("""{"n": "7", "flag": "sí", "list": "solo.pdf", "json": "[\"a\",\"b\"]", "date": "2026-10-01"}""");

        Assert.Equal(7, args.Int("n", 0));
        Assert.True(args.Bool("flag"));
        Assert.Equal(["solo.pdf"], args.StringList("list"));
        Assert.Equal(["a", "b"], args.StringList("json"));
        Assert.Equal(new DateTime(2026, 10, 1), args.OptionalDate("date")!.Value.Date);
    }

    [Fact]
    public void ExplainsMissingArguments()
    {
        var error = Assert.Throws<ToolArgumentException>(() => Args("{}").String("ruta"));
        Assert.Contains("ruta", error.Message);
    }
}

public class ToolPolicyTests
{
    private static readonly ToolContext Clean = ToolContext.Default;
    private static readonly ToolContext AfterReading = new() { ExternalContentInTurn = true };

    [Theory]
    [InlineData(ToolRisk.Read, false)]
    [InlineData(ToolRisk.Low, false)]
    [InlineData(ToolRisk.Modify, true)]
    [InlineData(ToolRisk.Dangerous, true)]
    public void DefaultsAskForChangesOnly(ToolRisk risk, bool asks)
    {
        Assert.Equal(asks, new ToolPolicy(new PermissionsSettings()).NeedsConfirmation(new ToolPreview("x", risk), Clean));
    }

    [Fact]
    public void DangerousActionsAlwaysAsk()
    {
        var policy = new ToolPolicy(new PermissionsSettings { ConfirmChanges = false });
        var preview = new ToolPreview("borrar", ToolRisk.Dangerous) { PermissionScope = "x" };
        policy.ApproveForSession(preview);

        Assert.True(policy.NeedsConfirmation(preview, Clean));
        Assert.False(policy.CanApproveForSession(preview));
    }

    [Fact]
    public void ExternalContentForcesConfirmationOfChangesAndLowRiskActions()
    {
        var policy = new ToolPolicy(new PermissionsSettings { ConfirmChanges = false });

        Assert.True(policy.NeedsConfirmation(new ToolPreview("abrir web", ToolRisk.Low), AfterReading));
        Assert.True(policy.NeedsConfirmation(new ToolPreview("mover", ToolRisk.Modify), AfterReading));
        Assert.False(policy.NeedsConfirmation(new ToolPreview("leer", ToolRisk.Read), AfterReading));
    }
}

public sealed class ToolSelectorTests : IDisposable
{
    private readonly TempWorkspace _ws = new();
    private readonly ToolRegistry _registry;

    public ToolSelectorTests()
    {
        _registry = new ToolRegistry(
        [
            new CreateFolderTool(_ws.Paths, _ws.Folders, _ws.Journal),
            new UndoTool(_ws.Journal),
            new OpenUrlTool(new DefaultShell()),
            new OpenProgramTool(new DefaultAppCatalog(new DefaultShell())),
            new InstalledProgramsTool(new DefaultAppCatalog(new DefaultShell())),
        ]);
    }

    [Fact]
    public async Task KeywordAloneBringsInTheTool()
    {
        // No category word and no embeddings: only the exact phrase "tengo instalado" points to it.
        var tools = await new ToolSelector(_registry).SelectAsync("¿tengo instalado android studio?", []);

        Assert.Contains(tools, t => t.Name == "programas_instalados");
    }

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task OffersNoToolsForSmallTalk()
    {
        Assert.Empty(await new ToolSelector(_registry).SelectAsync("hola, ¿qué tal estás?", []));
    }

    [Fact]
    public async Task PicksToolsByKeywordIgnoringAccents()
    {
        var tools = await new ToolSelector(_registry).SelectAsync("Créame una CARPETA en el escritorio", []);

        Assert.Contains(tools, t => t.Name == "crear_carpeta");
        Assert.Contains(tools, t => t.Name == "deshacer");
        Assert.DoesNotContain(tools, t => t.Name == "abrir_web");
    }

    [Fact]
    public async Task MatchesWholeWordsOnly()
    {
        // "ordena" (sort) must not match "ordenador" (computer).
        var tools = await new ToolSelector(_registry).SelectAsync("bloquea el ordenador", []);

        Assert.DoesNotContain(tools, t => t.Category == "archivos");
        Assert.True(ToolSelector.Matches("hay archivos duplicados", "duplicad*"));
        Assert.False(ToolSelector.Matches("bloquea el ordenador", "ordena"));
    }

    [Fact]
    public async Task PutsTheMostSpecificToolFirst()
    {
        var tools = await new ToolSelector(_registry).SelectAsync("créame una carpeta llamada Clase", []);

        Assert.Equal("crear_carpeta", tools[0].Name);
    }

    [Fact]
    public async Task KeepsThePreviousTurnCategoriesForFollowUps()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "crea una carpeta X"),
            new(ChatRole.Assistant, "") { ToolCalls = [new ToolCall("crear_carpeta", [])] },
            new(ChatRole.Tool, "ok") { ToolName = "crear_carpeta" },
            new(ChatRole.Assistant, "Hecho"),
        };

        var tools = await new ToolSelector(_registry).SelectAsync("y otra llamada Y", history);

        Assert.Contains(tools, t => t.Name == "crear_carpeta");
    }
}

public class AppMatcherTests
{
    private static readonly AppEntry[] Apps =
    [
        new("Spotify", "s"),
        new("Google Chrome", "c"),
        new("Notepad", "n"),
        new("Visual Studio Code", "v"),
        new("Microsoft Word", "w"),
    ];

    [Theory]
    [InlineData("spotify", "Spotify")]
    [InlineData("el spoti", "Spotify")]
    [InlineData("spoti", "Spotify")]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("bloc de notas", "Notepad")]
    [InlineData("vs code", "Visual Studio Code")]
    [InlineData("word", "Microsoft Word")]
    public void FindsTheRightProgram(string query, string expected)
    {
        var cleaned = query.StartsWith("el ") ? query[3..] : query;
        Assert.Equal(expected, AppMatcher.Rank(cleaned, Apps)[0].App.Name);
    }

    [Fact]
    public void FindsNothingForUnrelatedNames()
    {
        Assert.Empty(AppMatcher.Rank("photoshop", Apps));
    }
}
