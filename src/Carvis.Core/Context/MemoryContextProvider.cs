using System.Text;
using Carvis.Core.Chat;
using Carvis.Core.Storage;

namespace Carvis.Core.Context;

/// <summary>Adds what Carvis remembers about the user to every conversation.</summary>
public sealed class MemoryContextProvider(IMemoryStore memories) : IChatContextProvider
{
    public Task<IReadOnlyList<ChatMessage>> GetContextAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        var items = memories.All();
        var text = new StringBuilder();
        if (items.Count > 0)
        {
            text.Append("Lo que recuerdas del usuario (guardado por él):\n");
            foreach (var item in items.TakeLast(60))
                text.Append("- [").Append(item.Id).Append("] ").Append(item.Text).Append('\n');
        }
        text.Append("Si el usuario te cuenta un dato estable sobre sí mismo (nombre, estudios, gustos, carpetas importantes) " +
                    "o te pide que recuerdes algo, guárdalo con la herramienta recordar.");
        return Task.FromResult<IReadOnlyList<ChatMessage>>([new ChatMessage(ChatRole.System, text.ToString())]);
    }
}
