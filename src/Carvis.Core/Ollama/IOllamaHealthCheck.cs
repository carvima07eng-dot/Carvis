namespace Carvis.Core.Ollama;

public interface IOllamaHealthCheck
{
    /// <summary>Checks that Ollama answers and that the chat model is downloaded.</summary>
    Task<OllamaStatus> CheckAsync(CancellationToken cancellationToken = default);
}
