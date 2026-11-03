using System.Text;

namespace Carvis.Core.Chat;

public interface ITitleGenerator
{
    Task<string> GenerateAsync(string userMessage, string answer, CancellationToken cancellationToken = default);
}

/// <summary>Asks the model for a short title for a conversation (falls back to the first words).</summary>
public sealed class TitleGenerator(IChatModelClient client) : ITitleGenerator
{
    public async Task<string> GenerateAsync(string userMessage, string answer, CancellationToken cancellationToken = default)
    {
        var request = new ModelRequest(
        [
            new ChatMessage(ChatRole.System, "Pones títulos muy cortos a conversaciones. Respondes solo con el título."),
            new ChatMessage(ChatRole.User,
                $"Escribe un título de 2 a 6 palabras en español para esta conversación, sin comillas ni punto final.\n\nUsuario: {Cut(userMessage, 400)}\nAsistente: {Cut(answer, 400)}"),
        ])
        { Temperature = 0.3 };

        try
        {
            var filter = new ThinkTagFilter();
            var text = new StringBuilder();
            await foreach (var chunk in client.StreamAsync(request, cancellationToken))
                text.Append(filter.Process(chunk.Text ?? string.Empty));
            text.Append(filter.Flush());
            var title = Clean(text.ToString());
            return title.Length > 0 ? title : Fallback(userMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fallback(userMessage);
        }
    }

    public static string Fallback(string userMessage)
    {
        var text = userMessage.ReplaceLineEndings(" ").Trim();
        return text.Length <= 48 ? text : text[..48].TrimEnd() + "…";
    }

    private static string Clean(string title)
    {
        title = title.ReplaceLineEndings(" ").Trim().Trim('"', '«', '»', '\'', '.', '*', '#', ' ');
        if (title.StartsWith("Título:", StringComparison.OrdinalIgnoreCase))
            title = title[7..].Trim();
        return title.Length > 60 ? title[..60].TrimEnd() : title;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
