using Carvis.Core.Configuration;
using OllamaSharp;

namespace Carvis.Core.Ollama;

public static class OllamaClientFactory
{
    public static OllamaApiClient Create(OllamaSettings settings, HttpMessageHandler? handler = null)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = new Uri(settings.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(10, settings.RequestTimeoutSeconds));
        return new OllamaApiClient(http, settings.ChatModel);
    }
}
