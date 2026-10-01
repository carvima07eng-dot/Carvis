using System.Runtime.CompilerServices;
using System.Text;
using Carvis.Core.Configuration;

namespace Carvis.Core.Chat;

public sealed class ChatService : IChatService
{
    private readonly IChatModelClient _client;
    private readonly AssistantSettings _settings;
    private readonly IReadOnlyList<IChatContextProvider> _contextProviders;
    private readonly List<ChatMessage> _history = [];
    private readonly object _lock = new();
    private int _isSending;

    public ChatService(
        IChatModelClient client,
        AssistantSettings settings,
        IEnumerable<IChatContextProvider>? contextProviders = null)
    {
        _client = client;
        _settings = settings;
        _contextProviders = contextProviders?.ToList() ?? [];
    }

    public IReadOnlyList<ChatMessage> History
    {
        get { lock (_lock) return _history.ToList(); }
    }

    public void ClearHistory()
    {
        lock (_lock) _history.Clear();
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default) => _client.WarmUpAsync(cancellationToken);

    public async IAsyncEnumerable<string> SendAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            throw new ArgumentException("El mensaje está vacío.", nameof(userMessage));

        if (Interlocked.Exchange(ref _isSending, 1) == 1)
            throw new InvalidOperationException("Ya hay una respuesta en curso.");

        var user = new ChatMessage(ChatRole.User, userMessage.Trim());
        var answer = new StringBuilder();
        var failed = false;

        try
        {
            var messages = await BuildRequestAsync(user, cancellationToken);
            var filter = new ThinkTagFilter();

            await using var stream = _client.StreamAsync(messages, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                string visible;
                try
                {
                    if (!await stream.MoveNextAsync())
                        break;
                    visible = filter.Process(stream.Current);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed = true;
                    throw;
                }

                visible = TrimLeadingWhitespace(visible, answer);
                if (visible.Length == 0)
                    continue;

                answer.Append(visible);
                yield return visible;
            }

            var tail = TrimLeadingWhitespace(filter.Flush(), answer);
            if (tail.Length > 0)
            {
                answer.Append(tail);
                yield return tail;
            }
        }
        finally
        {
            // A cancelled answer is kept so the history matches what the user saw.
            if (!failed && answer.Length > 0)
                AddToHistory(user, new ChatMessage(ChatRole.Assistant, answer.ToString().TrimEnd()));

            Volatile.Write(ref _isSending, 0);
        }
    }

    private async Task<List<ChatMessage>> BuildRequestAsync(ChatMessage user, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(_settings.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, _settings.SystemPrompt));

        foreach (var provider in _contextProviders)
            messages.AddRange(await provider.GetContextAsync(user.Content, cancellationToken));

        lock (_lock) messages.AddRange(_history);
        messages.Add(user);
        return messages;
    }

    private void AddToHistory(ChatMessage user, ChatMessage assistant)
    {
        lock (_lock)
        {
            _history.Add(user);
            _history.Add(assistant);

            var max = Math.Max(2, _settings.MaxHistoryMessages);
            if (_history.Count > max)
                _history.RemoveRange(0, _history.Count - max);
        }
    }

    // Models usually start with blank lines (e.g. after an empty think block).
    private static string TrimLeadingWhitespace(string text, StringBuilder answerSoFar) =>
        answerSoFar.Length == 0 ? text.TrimStart() : text;
}
