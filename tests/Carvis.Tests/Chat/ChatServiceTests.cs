using System.Runtime.CompilerServices;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Chat;

public class ChatServiceTests
{
    private readonly FakeChatModelClient _client = new();
    private readonly AssistantSettings _settings = new() { SystemPrompt = "Eres Carvis.", MaxHistoryMessages = 40 };

    private ChatService CreateService(params IChatContextProvider[] providers) => new(_client, _settings, providers);

    [Fact]
    public async Task SendAsync_WithImage_UsesVisionModelThenFreesItAndReloadsChat()
    {
        _client.Reply("Se ve un error.");
        var ollama = new OllamaSettings { VisionModel = "qwen2.5vl:7b" };
        var service = new ChatService(_client, _settings, ollamaSettings: ollama);

        await service.SendAsync(new ChatInput("¿qué es esto?") { Images = [[1, 2, 3]] }).ToListAsync();

        var request = _client.Requests.Single();
        Assert.Equal("qwen2.5vl:7b", request.Model);
        Assert.Equal("0", request.KeepAlive);
        Assert.Equal(1, _client.WarmUps);
    }

    [Fact]
    public async Task SendAsync_WithImage_KeepsAMultimodalChatModelLoaded()
    {
        _client.Reply("Se ve un error.");
        var ollama = new OllamaSettings { ChatModel = "qwen3-vl:8b", VisionModel = "qwen3-vl:8b" };
        var service = new ChatService(_client, _settings, ollamaSettings: ollama);

        await service.SendAsync(new ChatInput("¿qué es esto?") { Images = [[1, 2, 3]] }).ToListAsync();

        var request = _client.Requests.Single();
        Assert.Null(request.Model);
        Assert.Null(request.KeepAlive);
        Assert.Equal(0, _client.WarmUps);
    }

    [Fact]
    public async Task SendAsync_StreamsChunksInOrder()
    {
        _client.Reply("Ho", "la", ", ¿qué tal?");

        var chunks = await CreateService().SendAsync("hola").TextAsync();

        Assert.Equal(["Ho", "la", ", ¿qué tal?"], chunks);
    }

    [Fact]
    public async Task SendAsync_SendsSystemPromptAndUserMessage()
    {
        _client.Reply("Hola");

        await CreateService().SendAsync("  hola  ").ToListAsync();

        Assert.Equal(
            [new ChatMessage(ChatRole.System, "Eres Carvis."), new ChatMessage(ChatRole.User, "hola")],
            _client.Requests.Single().Messages);
    }

    [Fact]
    public async Task SendAsync_KeepsHistoryBetweenMessages()
    {
        _client.Reply("¡Hola!").Reply("Bien, gracias.");
        var service = CreateService();

        await service.SendAsync("hola").ToListAsync();
        await service.SendAsync("¿qué tal?").ToListAsync();

        Assert.Equal(
            [
                new ChatMessage(ChatRole.System, "Eres Carvis."),
                new ChatMessage(ChatRole.User, "hola"),
                new ChatMessage(ChatRole.Assistant, "¡Hola!"),
                new ChatMessage(ChatRole.User, "¿qué tal?"),
            ],
            _client.Requests[1].Messages);
        Assert.Equal(4, service.History.Count);
        Assert.Equal(new ChatMessage(ChatRole.Assistant, "Bien, gracias."), service.History[^1]);
    }

    [Fact]
    public async Task SendAsync_HidesThinkBlocksAndLeadingBlankLines()
    {
        _client.Reply("<thi", "nk>\nLet me think", "...</th", "ink>\n\n", "Hola", " Carlos");
        var service = CreateService();

        var answer = string.Concat(await service.SendAsync("hola").TextAsync());

        Assert.Equal("Hola Carlos", answer);
        Assert.Equal("Hola Carlos", service.History[^1].Content);
    }

    [Fact]
    public async Task SendAsync_WhenModelFails_ThrowsAndDoesNotStoreTheExchange()
    {
        _client.Reply(_ => Failing());
        var service = CreateService();

        await Assert.ThrowsAsync<HttpRequestException>(() => service.SendAsync("hola").ToListAsync());

        Assert.Empty(service.History);
    }

    [Fact]
    public async Task SendAsync_WhenCancelled_KeepsThePartialAnswer()
    {
        _client.Reply(ct => SlowAfterFirstChunk(ct));
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();

        var received = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in service.SendAsync("cuéntame algo", cancellation.Token))
            {
                received.Add(((TextDelta)chunk).Text);
                cancellation.Cancel();
            }
        });

        Assert.Equal(["Érase una vez"], received);
        Assert.Equal(new ChatMessage(ChatRole.Assistant, "Érase una vez"), service.History[^1]);
    }

    [Fact]
    public async Task SendAsync_SummarizesOldMessagesInsteadOfForgettingThem()
    {
        _settings.MaxHistoryMessages = 4;
        var service = CreateService();
        string? savedSummary = null;
        service.SummaryUpdated += s => savedSummary = s;

        for (var i = 1; i <= 3; i++)
        {
            _client.Reply($"respuesta {i}");
            await service.SendAsync($"pregunta {i}").ToListAsync();
        }

        _client.Reply("El usuario hizo la pregunta 1.").Reply("respuesta 4");
        await service.SendAsync("pregunta 4").ToListAsync();

        var summaryRequest = _client.Requests[3];
        Assert.Contains("pregunta 1", summaryRequest.Messages[^1].Content);
        Assert.Equal("El usuario hizo la pregunta 1.", savedSummary);
        Assert.Contains("Resumen de la parte anterior", _client.Requests[4].Messages[0].Content);
        Assert.Equal("pregunta 2", service.History[0].Content);
    }

    [Fact]
    public async Task RemoveLastTurn_TakesBackTheLastExchange()
    {
        _client.Reply("uno").Reply("dos");
        var service = CreateService();
        await service.SendAsync("primera").ToListAsync();
        await service.SendAsync("segunda").ToListAsync();

        Assert.Equal("segunda", service.RemoveLastTurn());
        Assert.Equal(2, service.History.Count);
    }

    [Fact]
    public async Task TurnCommitted_DeliversTheWholeExchange()
    {
        _client.Reply("hola");
        var service = CreateService();
        IReadOnlyList<ChatMessage>? turn = null;
        service.TurnCommitted += t => turn = t;

        await service.SendAsync("buenas").ToListAsync();

        Assert.Equal([ChatRole.User, ChatRole.Assistant], turn!.Select(m => m.Role));
    }

    [Fact]
    public async Task SendAsync_ReportsThinkingSeparately()
    {
        _client.Reply("<think>pienso</think>", "Hola");
        var events = await CreateService().SendAsync("hola").ToListAsync();

        Assert.Equal("pienso", string.Concat(events.OfType<ThinkingDelta>().Select(t => t.Text)));
        Assert.Equal("Hola", string.Concat(events.OfType<TextDelta>().Select(t => t.Text)));
    }

    [Fact]
    public async Task SendAsync_AddsMessagesFromContextProviders()
    {
        _client.Reply("Según tus notas...");
        var context = new ChatMessage(ChatRole.System, "Fragmento de notas.txt");

        await CreateService(new FixedContextProvider(context)).SendAsync("¿qué dicen mis notas?").ToListAsync();

        // System text from providers is merged into the single system message.
        var request = _client.Requests.Single().Messages;
        Assert.Equal(new ChatMessage(ChatRole.System, "Eres Carvis.\n\nFragmento de notas.txt"), request[0]);
        Assert.Equal(2, request.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_RejectsEmptyMessages(string message)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().SendAsync(message).ToListAsync());
    }

    [Fact]
    public async Task SendAsync_RejectsAMessageWhileAnotherIsStreaming()
    {
        _client.Reply(ct => SlowAfterFirstChunk(ct));
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();

        await using var first = service.SendAsync("primera", cancellation.Token).GetAsyncEnumerator();
        await first.MoveNextAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync("segunda").ToListAsync());
        cancellation.Cancel();
    }

    [Fact]
    public async Task ClearHistory_StartsANewConversation()
    {
        _client.Reply("¡Hola!").Reply("¿En qué te ayudo?");
        var service = CreateService();
        await service.SendAsync("hola").ToListAsync();

        service.ClearHistory();
        await service.SendAsync("otra vez").ToListAsync();

        Assert.Equal(2, _client.Requests[1].Messages.Count); // system prompt + new message
    }

    [Fact]
    public async Task WarmUpAsync_LoadsTheModelWithoutTouchingTheHistory()
    {
        var service = CreateService();

        await service.WarmUpAsync();

        Assert.Equal(1, _client.WarmUps);
        Assert.Empty(service.History);
    }

    private static async IAsyncEnumerable<string> Failing()
    {
        await Task.Yield();
        throw new HttpRequestException("Connection refused");
#pragma warning disable CS0162 // makes this an iterator
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<string> SlowAfterFirstChunk([EnumeratorCancellation] CancellationToken ct)
    {
        yield return "Érase una vez";
        await Task.Delay(Timeout.Infinite, ct);
        yield return " un dragón";
    }

    private sealed class FixedContextProvider(ChatMessage message) : IChatContextProvider
    {
        public Task<IReadOnlyList<ChatMessage>> GetContextAsync(string userMessage, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([message]);
    }
}
