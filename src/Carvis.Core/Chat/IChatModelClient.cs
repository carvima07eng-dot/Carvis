namespace Carvis.Core.Chat;

/// <summary>Low-level access to a chat LLM. Stateless: the caller sends the whole conversation.</summary>
public interface IChatModelClient
{
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Loads the model into memory so the first answer doesn't wait for it.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
