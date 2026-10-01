using System.Text.Json;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Ollama;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Ollama;

public class OllamaChatModelClientTests
{
    private readonly OllamaSettings _settings = new() { ChatModel = "qwen3:8b", KeepAlive = "30m" };

    [Fact]
    public async Task StreamAsync_YieldsTheContentOfEachStreamedLine()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text(
            """
            {"model":"qwen3:8b","message":{"role":"assistant","content":"¡Ho"},"done":false}
            {"model":"qwen3:8b","message":{"role":"assistant","content":"la!"},"done":false}
            {"model":"qwen3:8b","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}
            """, "application/x-ndjson"));
        var client = new OllamaChatModelClient(OllamaClientFactory.Create(_settings, handler), _settings);

        var chunks = await client.StreamAsync(new ModelRequest([new ChatMessage(ChatRole.User, "hola")])).ToListAsync();

        Assert.Equal(["¡Ho", "la!"], chunks.Select(c => c.Text));
    }

    [Fact]
    public async Task StreamAsync_SendsModelMessagesAndThinkFlag()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text(
            """{"model":"qwen3:8b","message":{"role":"assistant","content":""},"done":true}"""));
        var client = new OllamaChatModelClient(OllamaClientFactory.Create(_settings, handler), _settings);

        await client.StreamAsync(new ModelRequest(
        [
            new ChatMessage(ChatRole.System, "Eres Carvis."),
            new ChatMessage(ChatRole.User, "hola"),
        ])).ToListAsync();

        var (request, body) = handler.Requests.Single();
        Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("qwen3:8b", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal("30m", root.GetProperty("keep_alive").GetString());
        Assert.Equal(16384, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(
            [("system", "Eres Carvis."), ("user", "hola")],
            root.GetProperty("messages").EnumerateArray()
                .Select(m => (m.GetProperty("role").GetString(), m.GetProperty("content").GetString())));
    }

    [Fact]
    public async Task WarmUpAsync_SendsAnEmptyChatSoOllamaLoadsTheModel()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text(
            """{"model":"qwen3:8b","message":{"role":"assistant","content":""},"done":true,"done_reason":"load"}"""));
        var client = new OllamaChatModelClient(OllamaClientFactory.Create(_settings, handler), _settings);

        await client.WarmUpAsync();

        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.Equal("qwen3:8b", json.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("messages").GetArrayLength());
        Assert.Equal("30m", json.RootElement.GetProperty("keep_alive").GetString());
    }

    [Fact]
    public async Task StreamAsync_SendsToolDefinitionsAndReadsToolCalls()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text(
            """
            {"model":"qwen3:8b","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"crear_carpeta","arguments":{"ruta":"C:\\Users\\Ana\\Desktop\\X"}}}]},"done":false}
            {"model":"qwen3:8b","message":{"role":"assistant","content":""},"done":true}
            """, "application/x-ndjson"));
        var client = new OllamaChatModelClient(OllamaClientFactory.Create(_settings, handler), _settings);
        var tool = new Carvis.Core.Tools.Files.CreateFolderTool(null!, null!, null!);

        var chunks = await client.StreamAsync(new ModelRequest([new ChatMessage(ChatRole.User, "crea X")]) { Tools = [tool] }).ToListAsync();

        var call = chunks.SelectMany(c => c.ToolCalls ?? []).Single();
        Assert.Equal("crear_carpeta", call.Name);
        Assert.Equal(@"C:\Users\Ana\Desktop\X", call.Arguments["ruta"]!.GetValue<string>());

        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        var function = json.RootElement.GetProperty("tools")[0].GetProperty("function");
        Assert.Equal("crear_carpeta", function.GetProperty("name").GetString());
        Assert.Equal("string", function.GetProperty("parameters").GetProperty("properties").GetProperty("ruta").GetProperty("type").GetString());
    }

    [Fact]
    public async Task StreamAsync_SendsToolResultsWithTheirToolName()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text(
            """{"model":"qwen3:8b","message":{"role":"assistant","content":"Hecho"},"done":true}"""));
        var client = new OllamaChatModelClient(OllamaClientFactory.Create(_settings, handler), _settings);
        var call = new Carvis.Core.Tools.ToolCall("crear_carpeta", new System.Text.Json.Nodes.JsonObject { ["ruta"] = "C:/x" });

        await client.StreamAsync(new ModelRequest(
        [
            new ChatMessage(ChatRole.User, "crea x"),
            new ChatMessage(ChatRole.Assistant, "") { ToolCalls = [call] },
            new ChatMessage(ChatRole.Tool, "Carpeta creada") { ToolName = "crear_carpeta" },
        ])).ToListAsync();

        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        var messages = json.RootElement.GetProperty("messages");
        Assert.Equal("crear_carpeta", messages[1].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("C:/x", messages[1].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetProperty("ruta").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("crear_carpeta", messages[2].GetProperty("tool_name").GetString());
    }
}
