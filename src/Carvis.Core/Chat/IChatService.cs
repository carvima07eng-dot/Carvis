namespace Carvis.Core.Chat;

/// <summary>A conversation with the assistant that remembers its history and can use tools.</summary>
public interface IChatService
{
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>Sends a message and streams what happens: text, tool calls and their results.</summary>
    IAsyncEnumerable<ChatEvent> SendAsync(string userMessage, CancellationToken cancellationToken = default);

    void ClearHistory();

    /// <summary>Replaces the history, e.g. when reopening a saved conversation.</summary>
    void LoadHistory(IEnumerable<ChatMessage> messages, string? summary = null);

    /// <summary>Takes back the last exchange (for "regenerate" or "edit"); returns the user's message.</summary>
    string? RemoveLastTurn();

    /// <summary>Summary of the older part of the conversation that no longer fits in the context.</summary>
    string? Summary { get; }

    /// <summary>A finished exchange (user message, tool calls, results and answer), ready to be saved.</summary>
    event Action<IReadOnlyList<ChatMessage>>? TurnCommitted;

    /// <summary>The summary of old messages changed.</summary>
    event Action<string>? SummaryUpdated;

    /// <summary>Loads the model in the background so the first answer is fast.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
