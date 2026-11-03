using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Carvis.Core.Chat;
using Carvis.Core.Configuration;

namespace Carvis.Core.Context;

/// <summary>Tells the model what it can't know by itself: date, time, who the user is and where their folders are.</summary>
public sealed class SystemContextProvider(TimeProvider time, AssistantSettings settings, IUserFolders folders) : IChatContextProvider
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public Task<IReadOnlyList<ChatMessage>> GetContextAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow();
        var text = new StringBuilder("Contexto actual:\n");
        text.Append("- Fecha y hora: ").Append(now.ToString("dddd, d 'de' MMMM 'de' yyyy, HH:mm", Spanish)).Append('\n');
        text.Append("- Zona horaria: ").Append(time.LocalTimeZone.Id).Append('\n');
        text.Append("- Usuario: ").Append(UserName).Append('\n');
        text.Append("- Sistema: ").Append(OperatingSystemName()).Append('\n');

        if (folders.All.Count > 0)
        {
            text.Append("- Carpetas del usuario:\n");
            foreach (var (name, path) in folders.All)
                text.Append("  - ").Append(name).Append(": ").Append(path).Append('\n');
        }

        return Task.FromResult<IReadOnlyList<ChatMessage>>([new ChatMessage(ChatRole.System, text.ToString().TrimEnd())]);
    }

    private string UserName => string.IsNullOrWhiteSpace(settings.UserName) ? Environment.UserName : settings.UserName;

    private static string OperatingSystemName()
    {
        if (!OperatingSystem.IsWindows())
            return RuntimeInformation.OSDescription;
        var version = Environment.OSVersion.Version;
        var name = version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} (compilación {version.Build})";
    }
}
