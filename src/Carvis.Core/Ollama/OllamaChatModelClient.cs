using System.Runtime.CompilerServices;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using ChatMessage = Carvis.Core.Chat.ChatMessage;
using ChatRole = Carvis.Core.Chat.ChatRole;
using OllamaRole = OllamaSharp.Models.Chat.ChatRole;

namespace Carvis.Core.Ollama;

public sealed class OllamaChatModelClient(IOllamaApiClient ollama, OllamaSettings settings) : IChatModelClient
{
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Model = settings.ChatModel,
            Messages = messages.Select(ToOllama).ToList(),
            Stream = true,
            Think = settings.EnableThinking,
            KeepAlive = settings.KeepAlive,
            Options = new RequestOptions
            {
                NumCtx = settings.ContextLength,
                Temperature = (float)settings.Temperature,
            },
        };

        await foreach (var response in ollama.ChatAsync(request, cancellationToken))
        {
            // Thinking tokens arrive in Message.Thinking and are not shown.
            var content = response?.Message?.Content;
            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        // Ollama loads the model and returns straight away when there are no messages.
        var request = new ChatRequest
        {
            Model = settings.ChatModel,
            Messages = [],
            Stream = true,
            KeepAlive = settings.KeepAlive,
        };

        await foreach (var _ in ollama.ChatAsync(request, cancellationToken))
        {
        }
    }

    private static Message ToOllama(ChatMessage message) => new(ToOllama(message.Role), message.Content);

    private static OllamaRole ToOllama(ChatRole role) => role switch
    {
        ChatRole.System => OllamaRole.System,
        ChatRole.Assistant => OllamaRole.Assistant,
        _ => OllamaRole.User,
    };
}
