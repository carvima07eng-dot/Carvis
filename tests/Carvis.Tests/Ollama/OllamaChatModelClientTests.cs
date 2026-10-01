using System.Text.Json;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Ollama;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Ollama;

public class OllamaChatModelClientTests
{
    private readonly OllamaSettings _settings = new() { ChatModel = "qwen3:8b" };

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

        var chunks = await client.StreamAsync([new ChatMessage(ChatRole.User, "hola")]).ToListAsync();

        Assert.Equal(["¡Ho", "la!"], chunks);
    }

    [Fact]
    public async Task StreamAsync_SendsModelMessagesAndThinkFlag()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Text(
            """{"model":"qwen3:8b","message":{"role":"assistant","content":""},"done":true}"""));
        var client = new OllamaChatModelClient(OllamaClientFactory.Create(_settings, handler), _settings);

        await client.StreamAsync(
        [
            new ChatMessage(ChatRole.System, "Eres Carvis."),
            new ChatMessage(ChatRole.User, "hola"),
        ]).ToListAsync();

        var (request, body) = handler.Requests.Single();
        Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("qwen3:8b", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal(
            [("system", "Eres Carvis."), ("user", "hola")],
            root.GetProperty("messages").EnumerateArray()
                .Select(m => (m.GetProperty("role").GetString(), m.GetProperty("content").GetString())));
    }
}
