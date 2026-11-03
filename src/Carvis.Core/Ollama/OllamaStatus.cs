namespace Carvis.Core.Ollama;

public enum OllamaState
{
    Ready,
    ServerUnavailable,
    ModelMissing,
}

public sealed record OllamaStatus(OllamaState State, string BaseUrl, string Model, string? Detail = null)
{
    public bool IsReady => State == OllamaState.Ready;
}
