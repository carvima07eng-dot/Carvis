using System.Net;
using Carvis.Core.Configuration;
using Carvis.Core.Ollama;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Ollama;

public class OllamaHealthCheckTests
{
    private readonly OllamaSettings _settings = new() { BaseUrl = "http://localhost:11434", ChatModel = "qwen3:8b" };

    private OllamaHealthCheck CreateCheck(Func<HttpRequestMessage, string, HttpResponseMessage> respond) =>
        new(OllamaClientFactory.Create(_settings, new StubHttpHandler(respond)), _settings);

    private static HttpResponseMessage RunningWith(HttpRequestMessage request, params string[] models) =>
        request.RequestUri!.AbsolutePath == "/api/tags"
            ? StubHttpHandler.Tags(models)
            : StubHttpHandler.Text("Ollama is running", "text/plain");

    [Fact]
    public async Task CheckAsync_IsReadyWhenTheModelIsDownloaded()
    {
        var status = await CreateCheck((r, _) => RunningWith(r, "nomic-embed-text:latest", "qwen3:8b")).CheckAsync();

        Assert.Equal(OllamaState.Ready, status.State);
        Assert.True(status.IsReady);
    }

    [Fact]
    public async Task CheckAsync_ReportsAMissingModel()
    {
        var status = await CreateCheck((r, _) => RunningWith(r, "llama3.2:latest")).CheckAsync();

        Assert.Equal(OllamaState.ModelMissing, status.State);
        Assert.Equal("qwen3:8b", status.Model);
    }

    [Fact]
    public async Task CheckAsync_ReportsTheServerAsUnavailableWhenItCannotConnect()
    {
        var status = await CreateCheck((_, _) => throw new HttpRequestException("Connection refused")).CheckAsync();

        Assert.Equal(OllamaState.ServerUnavailable, status.State);
        Assert.Equal("http://localhost:11434", status.BaseUrl);
    }

    [Fact]
    public async Task CheckAsync_ReportsTheServerAsUnavailableOnAnErrorResponse()
    {
        var status = await CreateCheck((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)).CheckAsync();

        Assert.Equal(OllamaState.ServerUnavailable, status.State);
    }

    [Theory]
    [InlineData("nomic-embed-text", "nomic-embed-text:latest", true)]
    [InlineData("QWEN3:8B", "qwen3:8b", true)]
    [InlineData("qwen3:8b", "qwen3:14b", false)]
    [InlineData("qwen3", "qwen3:8b", false)]
    public void ModelNames_TreatAMissingTagAsLatest(string a, string b, bool same)
    {
        Assert.Equal(same, ModelNames.AreSame(a, b));
    }
}
