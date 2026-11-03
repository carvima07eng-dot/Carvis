using Carvis.Core.Chat;

namespace Carvis.Tests.Chat;

public class ThinkTagFilterTests
{
    [Theory]
    [InlineData(new[] { "Hola" }, "Hola")]
    [InlineData(new[] { "<think>razono</think>Hola" }, "Hola")]
    [InlineData(new[] { "<", "think>", "x", "</", "think>", "Hola" }, "Hola")]
    [InlineData(new[] { "a < b" }, "a < b")]
    [InlineData(new[] { "Usa <b>negrita</b>" }, "Usa <b>negrita</b>")]
    [InlineData(new[] { "termina en <" }, "termina en <")]
    [InlineData(new[] { "<think>sin cerrar" }, "")]
    public void RemovesThinkBlocks(string[] chunks, string expected)
    {
        var filter = new ThinkTagFilter();

        var output = string.Concat(chunks.Select(filter.Process)) + filter.Flush();

        Assert.Equal(expected, output);
    }
}
