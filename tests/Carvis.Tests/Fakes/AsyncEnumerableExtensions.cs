using Carvis.Core.Chat;

namespace Carvis.Tests.Fakes;

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    /// <summary>The text chunks of a chat answer, ignoring tool events.</summary>
    public static async Task<List<string>> TextAsync(this IAsyncEnumerable<ChatEvent> source) =>
        (await source.ToListAsync()).OfType<TextDelta>().Select(d => d.Text).ToList();
}
