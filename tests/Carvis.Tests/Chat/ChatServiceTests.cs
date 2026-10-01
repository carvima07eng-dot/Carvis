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
    public async Task SendAsync_DropsOldestMessagesBeyondTheLimit()
    {
        _settings.MaxHistoryMessages = 4;
        var service = CreateService();

        for (var i = 1; i <= 3; i++)
        {
            _client.Reply($"respuesta {i}");
            await service.SendAsync($"pregunta {i}").ToListAsync();
        }

        Assert.Equal(4, service.History.Count);
        Assert.Equal("pregunta 2", service.History[0].Content);
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
