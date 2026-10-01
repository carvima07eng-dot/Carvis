using System.Runtime.CompilerServices;
using Carvis.Core.Chat;

namespace Carvis.Tests.Fakes;

/// <summary>Replays canned chunks and records what the service sent.</summary>
internal sealed class FakeChatModelClient : IChatModelClient
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<string>>> _responses = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
    public int WarmUps { get; private set; }

    public FakeChatModelClient Reply(params string[] chunks)
    {
        _responses.Enqueue(_ => ToAsync(chunks));
        return this;
    }

    public FakeChatModelClient Reply(Func<CancellationToken, IAsyncEnumerable<string>> stream)
    {
        _responses.Enqueue(stream);
        return this;
    }

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        return _responses.Dequeue()(cancellationToken);
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        WarmUps++;
        return Task.CompletedTask;
    }

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
