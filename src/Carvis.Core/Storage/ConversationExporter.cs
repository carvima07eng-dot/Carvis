using System.Globalization;
using System.Text;
using Carvis.Core.Chat;

namespace Carvis.Core.Storage;

/// <summary>A saved conversation as a Markdown file (what was said and which actions ran).</summary>
public static class ConversationExporter
{
    public static string ToMarkdown(ConversationInfo info, IReadOnlyList<ChatMessage> messages, string userName = "Yo")
    {
        var text = new StringBuilder();
        text.Append("# ").Append(info.Title).Append("\n\n");
        text.Append(CultureInfo.GetCultureInfo("es-ES"), $"_Conversación con Carvis del {info.CreatedAt.ToLocalTime():d 'de' MMMM 'de' yyyy, HH:mm}_\n\n");

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case ChatRole.User:
                    text.Append("## ").Append(userName).Append("\n\n").Append(message.Content.Trim()).Append("\n\n");
                    break;
                case ChatRole.Assistant:
                    foreach (var call in message.ToolCalls ?? [])
                        text.Append("> Acción: `").Append(call.Name).Append("` ").Append(call.Arguments.ToJsonString()).Append('\n');
                    if (message.ToolCalls is { Count: > 0 })
                        text.Append('\n');
                    if (message.Content.Trim().Length > 0)
                        text.Append("## Carvis\n\n").Append(message.Content.Trim()).Append("\n\n");
                    break;
                case ChatRole.Tool:
                    var first = message.Content.Split('\n')[0];
                    text.Append("> Resultado: ").Append(first.Length > 200 ? first[..200] + "…" : first).Append("\n\n");
                    break;
            }
        }
        return text.ToString().TrimEnd() + "\n";
    }

    /// <summary>Writes it to Documents\Carvis\Conversaciones and returns the path.</summary>
    public static string Save(ConversationInfo info, IReadOnlyList<ChatMessage> messages, string? folder = null, string userName = "Yo")
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify), "Carvis", "Conversaciones");
        Directory.CreateDirectory(folder);
        // The Windows rules, also when exporting elsewhere: the file may end up on a Windows PC.
        var name = string.Concat(info.Title.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim(' ', '.');
        if (name.Length == 0)
            name = "Conversación";
        var path = Path.Combine(folder, $"{name} ({info.CreatedAt.ToLocalTime():yyyy-MM-dd}).md");
        File.WriteAllText(path, ToMarkdown(info, messages, userName), new UTF8Encoding(false));
        return path;
    }
}
