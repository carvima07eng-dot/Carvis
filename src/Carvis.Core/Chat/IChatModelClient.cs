using Carvis.Core.Tools;

namespace Carvis.Core.Chat;

public sealed record ModelRequest(IReadOnlyList<ChatMessage> Messages)
{
    public IReadOnlyList<ITool>? Tools { get; init; }
    public double? Temperature { get; init; }

    /// <summary>Overrides the chat model (e.g. the vision model).</summary>
    public string? Model { get; init; }
}

/// <summary>A piece of the streamed answer: text, tool calls or both.</summary>
public sealed record ModelChunk(string? Text, IReadOnlyList<ToolCall>? ToolCalls = null);

/// <summary>Low-level access to a chat LLM. Stateless: the caller sends the whole conversation.</summary>
public interface IChatModelClient
{
    IAsyncEnumerable<ModelChunk> StreamAsync(ModelRequest request, CancellationToken cancellationToken = default);

    /// <summary>Loads the model into memory so the first answer doesn't wait for it.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
