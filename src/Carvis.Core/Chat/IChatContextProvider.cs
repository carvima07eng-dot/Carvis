namespace Carvis.Core.Chat;

/// <summary>
/// Adds extra context for a user question before it reaches the model.
/// The RAG provider adds the relevant document fragments here; others add the date, the system and the memory.
/// </summary>
public interface IChatContextProvider
{
    Task<IReadOnlyList<ChatMessage>> GetContextAsync(string userMessage, CancellationToken cancellationToken = default);
}
