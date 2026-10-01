using System.Text;

namespace Carvis.Core.Chat;

/// <summary>
/// Removes &lt;think&gt;...&lt;/think&gt; blocks from a streamed answer. Reasoning models such as
/// qwen3 may emit them inline, and a tag can be split across several chunks.
/// </summary>
internal sealed class ThinkTagFilter
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    private readonly StringBuilder _pending = new();
    private bool _insideThink;

    public string Process(string chunk)
    {
        _pending.Append(chunk);
        var text = _pending.ToString();
        _pending.Clear();

        var output = new StringBuilder();
        var position = 0;

        while (position < text.Length)
        {
            var tag = _insideThink ? CloseTag : OpenTag;
            var index = text.IndexOf(tag, position, StringComparison.Ordinal);

            if (index >= 0)
            {
                if (!_insideThink)
                    output.Append(text, position, index - position);
                position = index + tag.Length;
                _insideThink = !_insideThink;
                continue;
            }

            // Hold back a trailing fragment that could be the start of the tag.
            var keep = PartialTagLength(text, position, tag);
            var end = text.Length - keep;
            if (!_insideThink)
                output.Append(text, position, end - position);
            _pending.Append(text, end, keep);
            break;
        }

        return output.ToString();
    }

    /// <summary>Returns whatever was held back once the stream has ended.</summary>
    public string Flush()
    {
        var rest = _insideThink ? string.Empty : _pending.ToString();
        _pending.Clear();
        return rest;
    }

    private static int PartialTagLength(string text, int start, string tag)
    {
        for (var length = Math.Min(tag.Length - 1, text.Length - start); length > 0; length--)
        {
            if (string.CompareOrdinal(text, text.Length - length, tag, 0, length) == 0)
                return length;
        }
        return 0;
    }
}
