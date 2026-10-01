using System.Net;
using System.Text;

namespace Carvis.Tests.Fakes;

/// <summary>Answers HTTP requests like a tiny Ollama server.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Text(string content, string mediaType = "application/json") =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, mediaType) };

    public static HttpResponseMessage Tags(params string[] models) =>
        Text("{\"models\":[" + string.Join(",", models.Select(m => $"{{\"name\":\"{m}\",\"model\":\"{m}\"}}")) + "]}");
}
