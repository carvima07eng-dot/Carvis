namespace Carvis.Core.Chat;

/// <summary>A conversation with the assistant that remembers its history.</summary>
public interface IChatService
{
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>Sends a message and streams the answer as text fragments.</summary>
    IAsyncEnumerable<string> SendAsync(string userMessage, CancellationToken cancellationToken = default);

    void ClearHistory();

    /// <summary>Loads the model in the background so the first answer is fast.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
