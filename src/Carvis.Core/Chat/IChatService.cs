namespace Carvis.Core.Chat;

/// <summary>A conversation with the assistant that remembers its history and can use tools.</summary>
public interface IChatService
{
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>Sends a message and streams what happens: text, tool calls and their results.</summary>
    IAsyncEnumerable<ChatEvent> SendAsync(string userMessage, CancellationToken cancellationToken = default);

    void ClearHistory();

    /// <summary>Replaces the history, e.g. when reopening a saved conversation.</summary>
    void LoadHistory(IEnumerable<ChatMessage> messages);

    /// <summary>Loads the model in the background so the first answer is fast.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
