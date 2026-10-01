using System.Text.Json.Nodes;
using Carvis.Core.Chat;
using Carvis.Core.Context;
using Carvis.Core.Platform;
using Carvis.Core.Storage;
using Carvis.Core.Tools;
using Carvis.Core.Tools.Memory;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Storage;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("carvis-db").FullName;
    private readonly CarvisDatabase _db;
    private readonly ContentProtector _protector = new();

    public StorageTests()
    {
        _db = new CarvisDatabase(Path.Combine(_dir, "carvis.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Database_CreatesTheLatestSchemaAndLoadsVectorSearch()
    {
        Assert.Equal(4, _db.SchemaVersion);
        Assert.True(_db.HasVectorSearch);
    }

    [Fact]
    public void Conversations_RoundTripMessagesWithToolCalls()
    {
        var store = new SqliteConversationStore(_db, _protector, TimeProvider.System);
        var conversation = store.Create("Carpetas del escritorio");
        var call = new ToolCall("crear_carpeta", new JsonObject { ["ruta"] = @"C:\Users\Carlos\Desktop\X" }, "1");

        store.Append(conversation.Id,
        [
            new ChatMessage(ChatRole.User, "crea X"),
            new ChatMessage(ChatRole.Assistant, "") { ToolCalls = [call] },
            new ChatMessage(ChatRole.Tool, "Carpeta creada") { ToolName = "crear_carpeta" },
            new ChatMessage(ChatRole.Assistant, "Hecho."),
        ]);

        var messages = store.Messages(conversation.Id);
        Assert.Equal(4, messages.Count);
        Assert.Equal(call, messages[1].ToolCalls![0]);
        Assert.Equal("crear_carpeta", messages[2].ToolName);
        Assert.Equal(4, store.Find(conversation.Id)!.MessageCount);
    }

    [Fact]
    public void Conversations_ListPinnedFirstAndSearchContents()
    {
        var store = new SqliteConversationStore(_db, _protector, TimeProvider.System);
        var a = store.Create("Apuntes de redes");
        var b = store.Create("Recetas");
        store.Append(b.Id, [new ChatMessage(ChatRole.User, "receta de paella valenciana")]);
        store.SetPinned(a.Id, true);

        Assert.Equal(a.Id, store.List()[0].Id);
        Assert.Equal(b.Id, Assert.Single(store.List("PAELLA")).Id);
    }

    [Fact]
    public void Conversations_RemoveLastTurnDeletesFromTheLastUserMessage()
    {
        var store = new SqliteConversationStore(_db, _protector, TimeProvider.System);
        var c = store.Create("x");
        store.Append(c.Id, [new(ChatRole.User, "1"), new(ChatRole.Assistant, "a"), new(ChatRole.User, "2"), new(ChatRole.Assistant, "b")]);

        store.RemoveLastTurn(c.Id);

        Assert.Equal(["1", "a"], store.Messages(c.Id).Select(m => m.Content));
    }

    [Fact]
    public void Conversations_DeleteRemovesTheirMessages()
    {
        var store = new SqliteConversationStore(_db, _protector, TimeProvider.System);
        var c = store.Create("x");
        store.Append(c.Id, [new(ChatRole.User, "1")]);

        store.Delete(c.Id);

        Assert.Null(store.Find(c.Id));
        Assert.Empty(store.Messages(c.Id));
    }

    [Fact]
    public async Task Journal_SurvivesRestartsAndKeepsUndoInformation()
    {
        var journal = new ActionJournal(new SqliteJournalStore(_db), new TrashFolderRecycleBin(new Core.Configuration.AppPaths(_dir, _dir)), TimeProvider.System);
        var folder = Path.Combine(_dir, "Nueva");
        Directory.CreateDirectory(folder);
        var entry = journal.Record("crear_carpeta", "Crear Nueva", true, [new UndoStep(UndoKind.RemoveCreated, folder)]);

        var reopened = new ActionJournal(new SqliteJournalStore(new CarvisDatabase(_db.Path)), new TrashFolderRecycleBin(new Core.Configuration.AppPaths(_dir, _dir)), TimeProvider.System);
        Assert.Equal(entry.Id, reopened.LastUndoable()!.Id);

        await reopened.UndoAsync(entry.Id);
        Assert.False(Directory.Exists(folder));
        Assert.True(reopened.Find(entry.Id)!.Undone);
    }

    [Fact]
    public async Task Memory_IsUsedInTheContextAndManagedByTools()
    {
        var memories = new SqliteMemoryStore(_db, _protector, TimeProvider.System);
        var remember = new RememberTool(memories);

        await remember.ExecuteAsync(TempWorkspace.Args("""{"dato": "Se llama Carlos y estudia DAM"}"""), ToolContext.Default);
        await remember.ExecuteAsync(TempWorkspace.Args("""{"dato": "se llama carlos y estudia dam"}"""), ToolContext.Default);

        var item = Assert.Single(memories.All());
        var context = (await new MemoryContextProvider(memories).GetContextAsync("hola")).Single().Content;
        Assert.Contains($"[{item.Id}] Se llama Carlos y estudia DAM", context);

        await new ForgetTool(memories).ExecuteAsync(TempWorkspace.Args($$"""{"id": {{item.Id}}}"""), ToolContext.Default);
        Assert.Empty(memories.All());
    }

    [Fact]
    public void DeleteAllUserData_EmptiesEveryTable()
    {
        var conversations = new SqliteConversationStore(_db, _protector, TimeProvider.System);
        var memories = new SqliteMemoryStore(_db, _protector, TimeProvider.System);
        conversations.Append(conversations.Create("x").Id, [new(ChatRole.User, "hola")]);
        memories.Add("dato");

        _db.DeleteAllUserData();

        Assert.Empty(conversations.List());
        Assert.Empty(memories.All());
    }

    [Fact]
    public void Protector_RoundTripsAndReadsPlainText()
    {
        Assert.Equal("hola", _protector.Unprotect(_protector.Protect("hola")));
        Assert.Equal("texto sin cifrar", _protector.Unprotect("texto sin cifrar"));
    }
}

public sealed class ConversationExporterTests
{
    [Fact]
    public void Markdown_HasQuestionsAnswersAndActions()
    {
        var info = new ConversationInfo("1", "Carpetas: clase/2º", false, new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.FromHours(2)), DateTimeOffset.Now, 4);
        var messages = new List<Carvis.Core.Chat.ChatMessage>
        {
            new(Carvis.Core.Chat.ChatRole.User, "crea la carpeta Clase"),
            new(Carvis.Core.Chat.ChatRole.Assistant, "") { ToolCalls = [new ToolCall("crear_carpeta", new System.Text.Json.Nodes.JsonObject { ["ruta"] = "Clase" })] },
            new(Carvis.Core.Chat.ChatRole.Tool, "Carpeta creada: C:\\Users\\Carlos\\Desktop\\Clase") { ToolName = "crear_carpeta" },
            new(Carvis.Core.Chat.ChatRole.Assistant, "Hecho, ya tienes la carpeta."),
        };

        var markdown = ConversationExporter.ToMarkdown(info, messages, "Carlos");

        Assert.StartsWith("# Carpetas: clase/2º", markdown);
        Assert.Contains("1 de octubre de 2026", markdown);
        Assert.Contains("## Carlos\n\ncrea la carpeta Clase", markdown);
        Assert.Contains("> Acción: `crear_carpeta` {\"ruta\":\"Clase\"}", markdown);
        Assert.Contains("> Resultado: Carpeta creada", markdown);
        Assert.Contains("## Carvis\n\nHecho, ya tienes la carpeta.", markdown);

        var dir = Directory.CreateTempSubdirectory("carvis-export").FullName;
        try
        {
            var path = ConversationExporter.Save(info, messages, dir);
            Assert.Equal("Carpetas_ clase_2º (2026-10-01).md", Path.GetFileName(path).Replace('\\', '_'));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
