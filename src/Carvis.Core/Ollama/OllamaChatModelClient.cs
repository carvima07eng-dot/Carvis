using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Tools;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using ChatMessage = Carvis.Core.Chat.ChatMessage;
using ChatRole = Carvis.Core.Chat.ChatRole;
using OllamaRole = OllamaSharp.Models.Chat.ChatRole;

namespace Carvis.Core.Ollama;

public sealed class OllamaChatModelClient(IOllamaApiClient ollama, OllamaSettings settings, ILogger<OllamaChatModelClient>? logger = null) : IChatModelClient
{
    public async IAsyncEnumerable<ModelChunk> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatRequest = new ChatRequest
        {
            Model = request.Model ?? settings.ChatModel,
            Messages = request.Messages.Select(ToOllama).ToList(),
            Stream = true,
            Think = settings.EnableThinking,
            KeepAlive = request.KeepAlive ?? settings.KeepAlive,
            Tools = request.Tools?.Select(ToDefinition).ToList(),
            Options = new RequestOptions
            {
                NumCtx = settings.ContextLength,
                Temperature = (float)(request.Temperature ?? settings.Temperature),
                TopP = (float)settings.TopP,
            },
        };

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan? firstToken = null;
        await foreach (var response in WithRetriesAsync(chatRequest, cancellationToken))
        {
            // Thinking tokens arrive in Message.Thinking and are not shown.
            var message = response?.Message;
            if (message is null || response is null)
                continue;

            var calls = message.ToolCalls?
                .Where(c => c.Function?.Name is { Length: > 0 })
                .Select(c => new ToolCall(c.Function!.Name!, ToJson(c.Function.Arguments), string.IsNullOrEmpty(c.Id) ? null : c.Id))
                .ToList();

            var stats = response is ChatDoneResponseStream { EvalCount: > 0 } done
                ? new GenerationStats(done.EvalCount, TimeSpan.FromTicks(done.EvalDuration / 100))
                : null;

            if (firstToken is null && (!string.IsNullOrEmpty(message.Content) || calls is { Count: > 0 } || !string.IsNullOrEmpty(message.Thinking)))
                firstToken = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (response is ChatDoneResponseStream finished)
                LogTimings(chatRequest.Model, firstToken, finished);

            if (!string.IsNullOrEmpty(message.Content) || calls is { Count: > 0 } || !string.IsNullOrEmpty(message.Thinking) || stats is not null)
            {
                yield return new ModelChunk(message.Content, calls is { Count: > 0 } ? calls : null)
                {
                    Thinking = string.IsNullOrEmpty(message.Thinking) ? null : message.Thinking,
                    Stats = stats,
                };
            }
        }
    }

    // Only numbers: never the prompt or the answer.
    private void LogTimings(string? model, TimeSpan? firstToken, ChatDoneResponseStream done)
    {
        if (logger is null)
            return;
        var seconds = done.EvalDuration / 1e9;
        logger.LogInformation(
            "Perf: {Model} first token after {FirstToken:0} ms (load {Load:0} ms, prompt {PromptTokens} tokens in {Prompt:0} ms), {Tokens} tokens at {Speed:0.0} tokens/s",
            model, firstToken?.TotalMilliseconds, done.LoadDuration / 1e6, done.PromptEvalCount, done.PromptEvalDuration / 1e6,
            done.EvalCount, seconds > 0 ? done.EvalCount / seconds : 0);
    }

    // Ollama sometimes refuses a connection while it is still loading a model: retry, but only
    // before the first piece of the answer arrived (never repeat half an answer).
    private async IAsyncEnumerable<ChatResponseStream?> WithRetriesAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TimeSpan[] delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
        for (var attempt = 0; ; attempt++)
        {
            var started = false;
            var enumerator = ollama.ChatAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    ChatResponseStream? current;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            yield break;
                        current = enumerator.Current;
                    }
                    catch (HttpRequestException) when (!started && attempt < delays.Length)
                    {
                        break;
                    }
                    started = true;
                    yield return current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            await Task.Delay(delays[attempt], cancellationToken);
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

    // Ollama's tool format: { type: "function", function: { name, description, parameters } }.
    private static object ToDefinition(ITool tool) => new JsonObject
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.Parameters.DeepClone(),
        },
    };

    private static JsonObject ToJson(IDictionary<string, object?>? arguments)
    {
        if (arguments is null)
            return [];
        return JsonSerializer.SerializeToNode(arguments) as JsonObject ?? [];
    }

    private static Message ToOllama(ChatMessage message)
    {
        var result = new Message(ToOllama(message.Role), message.Content)
        {
            ToolName = message.ToolName,
            Images = message.Images?.Select(Convert.ToBase64String).ToArray(),
        };

        if (message.ToolCalls is { Count: > 0 } calls)
        {
            result.ToolCalls = calls.Select(c => new Message.ToolCall
            {
                Id = c.Id,
                Function = new Message.Function
                {
                    Name = c.Name,
                    Arguments = c.Arguments.ToDictionary(p => p.Key, p => (object?)p.Value?.DeepClone()),
                },
            }).ToList();
        }

        return result;
    }

    private static OllamaRole ToOllama(ChatRole role) => role switch
    {
        ChatRole.System => OllamaRole.System,
        ChatRole.Assistant => OllamaRole.Assistant,
        ChatRole.Tool => OllamaRole.Tool,
        _ => OllamaRole.User,
    };
}
