using Carvis.Core.Chat;
using Carvis.Core.Configuration;

namespace Carvis.Core.Vision;

public enum CaptureArea
{
    /// <summary>The user drags a rectangle.</summary>
    Region,

    /// <summary>The monitor under the mouse.</summary>
    Screen,

    /// <summary>The window that was in front before Carvis.</summary>
    Window,
}

/// <summary>Screenshots as PNG, taken with Carvis' window out of the way. Never saved unless the user wants.</summary>
public interface IScreenCapture
{
    bool IsAvailable { get; }

    /// <summary>Null when the user cancelled the region selection.</summary>
    Task<byte[]?> CaptureAsync(CaptureArea area, CancellationToken cancellationToken = default);
}

public sealed class NoScreenCapture : IScreenCapture
{
    public bool IsAvailable => false;
    public Task<byte[]?> CaptureAsync(CaptureArea area, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Las capturas de pantalla solo funcionan en Windows.");
}

public interface IVisionService
{
    /// <summary>Asks the vision model about a picture; the answer is plain text.</summary>
    Task<string> DescribeAsync(byte[] png, string question, CancellationToken cancellationToken = default);
}

/// <summary>The vision model (qwen2.5vl) through the same Ollama client as the chat.</summary>
public sealed class OllamaVisionService(IChatModelClient client, OllamaSettings settings) : IVisionService
{
    public async Task<string> DescribeAsync(byte[] png, string question, CancellationToken cancellationToken = default)
    {
        var request = new ModelRequest(
        [
            new ChatMessage(ChatRole.System, "Describe con precisión lo que se ve en la imagen y responde a la pregunta en español. " +
                "Copia literalmente los textos y mensajes de error importantes."),
            new ChatMessage(ChatRole.User, question) { Images = [png] },
        ])
        {
            Model = settings.VisionModel,
            KeepAlive = "2m",
            Temperature = 0.2,
        };

        var text = new System.Text.StringBuilder();
        var filter = new ThinkTagFilter();
        await foreach (var chunk in client.StreamAsync(request, cancellationToken))
            text.Append(filter.Process(chunk.Text ?? string.Empty));
        text.Append(filter.Flush());
        return text.ToString().Trim();
    }
}
