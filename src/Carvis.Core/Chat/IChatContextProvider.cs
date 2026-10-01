namespace Carvis.Core.Chat;

/// <summary>
/// Adds extra context for a user question before it reaches the model.
/// Phase 2 (RAG) plugs in here with the relevant document fragments.
/// </summary>
public interface IChatContextProvider
{
    Task<IReadOnlyList<ChatMessage>> GetContextAsync(string userMessage, CancellationToken cancellationToken = default);
}
