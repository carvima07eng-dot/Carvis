using Carvis.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp;

namespace Carvis.Core.Ollama;

public sealed class OllamaHealthCheck(
    IOllamaApiClient ollama,
    OllamaSettings settings,
    ILogger<OllamaHealthCheck>? logger = null) : IOllamaHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly ILogger _logger = logger ?? NullLogger<OllamaHealthCheck>.Instance;

    public async Task<OllamaStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            if (!await ollama.IsRunningAsync(timeout.Token))
                return Status(OllamaState.ServerUnavailable);

            var models = await ollama.ListLocalModelsAsync(timeout.Token);
            var installed = models.Select(m => m.Name).ToList();

            return installed.Any(name => ModelNames.AreSame(name, settings.ChatModel))
                ? Status(OllamaState.Ready)
                : Status(OllamaState.ModelMissing, string.Join(", ", installed));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ollama is not reachable at {Url}", settings.BaseUrl);
            return Status(OllamaState.ServerUnavailable, ex.Message);
        }
    }

    private OllamaStatus Status(OllamaState state, string? detail = null) =>
        new(state, settings.BaseUrl, settings.ChatModel, detail);
}
