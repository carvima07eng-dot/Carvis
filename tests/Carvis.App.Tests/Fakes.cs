using System.Runtime.CompilerServices;
using Carvis.Core.Chat;
using Carvis.Core.Ollama;
using Carvis.Core.Tools;

namespace Carvis.App.Tests;

internal sealed class ScriptedModel : IChatModelClient
{
    private readonly Queue<ModelChunk[]> _answers = new();
    public List<ModelRequest> Requests { get; } = [];

    public ScriptedModel Answer(string text)
    {
        _answers.Enqueue([new ModelChunk(text)]);
        return this;
    }

    public ScriptedModel CallTool(string name, string json)
    {
        _answers.Enqueue([new ModelChunk(null, [new ToolCall(name, System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject())])]);
        return this;
    }

    public async IAsyncEnumerable<ModelChunk> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var answer = _answers.Count > 0 ? _answers.Dequeue() : [new ModelChunk("(sin guion)")];
        foreach (var chunk in answer)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class ReadyOllama : IOllamaHealthCheck
{
    public Task<OllamaStatus> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OllamaStatus(OllamaState.Ready, "http://localhost:11434", "qwen3:8b"));
}

internal sealed class FakeModels : IModelManager
{
    public Task<IReadOnlyList<LocalModel>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalModel>>([new("qwen3:8b", 1), new("qwen3:14b", 1), new("nomic-embed-text:latest", 1)]);
    public Task<IReadOnlyList<LoadedModel>> LoadedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LoadedModel>>([]);
    public Task<bool> IsInstalledAsync(string model, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task PullAsync(string model, IProgress<(string Status, double Progress)>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnloadAsync(string model, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
