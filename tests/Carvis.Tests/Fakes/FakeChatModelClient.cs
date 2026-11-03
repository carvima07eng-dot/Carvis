using System.Runtime.CompilerServices;
using Carvis.Core.Chat;
using Carvis.Core.Tools;

namespace Carvis.Tests.Fakes;

/// <summary>Replays canned answers and records what the service sent.</summary>
internal sealed class FakeChatModelClient : IChatModelClient
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ModelChunk>>> _responses = new();

    public List<ModelRequest> Requests { get; } = [];
    public int WarmUps { get; private set; }

    public FakeChatModelClient Reply(params string[] chunks)
    {
        _responses.Enqueue(_ => ToAsync(chunks.Select(c => new ModelChunk(c))));
        return this;
    }

    public FakeChatModelClient CallTool(string name, string argumentsJson, string? text = null)
    {
        var call = new ToolCall(name, System.Text.Json.Nodes.JsonNode.Parse(argumentsJson)!.AsObject());
        _responses.Enqueue(_ => ToAsync([new ModelChunk(text, [call])]));
        return this;
    }

    public FakeChatModelClient Reply(Func<CancellationToken, IAsyncEnumerable<string>> stream)
    {
        _responses.Enqueue(ct => Map(stream(ct)));
        return this;
    }

    public IAsyncEnumerable<ModelChunk> StreamAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request with { Messages = request.Messages.ToList() });
        return _responses.Dequeue()(cancellationToken);
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        WarmUps++;
        return Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ModelChunk> ToAsync(IEnumerable<ModelChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<ModelChunk> Map(IAsyncEnumerable<string> source, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var text in source.WithCancellation(ct))
            yield return new ModelChunk(text);
    }
}
