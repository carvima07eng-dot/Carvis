using System.Text;

namespace Carvis.Core.Indexing;

/// <summary>
/// Splits documents into fragments of about 1000 characters that end at paragraph or sentence
/// boundaries, with some overlap so an idea cut in two is still found.
/// </summary>
public sealed class TextChunker(int targetSize = 1000, int overlap = 150)
{
    public IReadOnlyList<TextChunk> Split(string sourcePath, DocumentText document)
    {
        var chunks = new List<TextChunk>();
        foreach (var part in document.Parts)
        {
            foreach (var piece in SplitText(Normalize(part.Text)))
                chunks.Add(new TextChunk(sourcePath, chunks.Count, piece, part.Page, part.Section));
        }
        return chunks;
    }

    private IEnumerable<string> SplitText(string text)
    {
        if (text.Length == 0)
            yield break;
        if (text.Length <= targetSize)
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var unit in Units(text))
        {
            if (current.Length > 0 && current.Length + unit.Length > targetSize)
            {
                var chunk = current.ToString().Trim();
                yield return chunk;
                current.Clear();
                current.Append(Tail(chunk));
            }
            current.Append(unit);
        }
        if (current.ToString().Trim() is { Length: > 0 } last)
            yield return last;
    }

    // Paragraphs, and sentences when a paragraph is too long, and hard cuts as a last resort.
    private IEnumerable<string> Units(string text)
    {
        foreach (var paragraph in text.Split("\n\n"))
        {
            var withBreak = paragraph + "\n\n";
            if (withBreak.Length <= targetSize)
            {
                yield return withBreak;
                continue;
            }

            foreach (var sentence in Sentences(paragraph))
            {
                if (sentence.Length <= targetSize)
                {
                    yield return sentence;
                    continue;
                }
                for (var i = 0; i < sentence.Length; i += targetSize - overlap)
                    yield return sentence.Substring(i, Math.Min(targetSize - overlap, sentence.Length - i));
            }
            yield return "\n\n";
        }
    }

    private static IEnumerable<string> Sentences(string paragraph)
    {
        var start = 0;
        for (var i = 0; i < paragraph.Length - 1; i++)
        {
            if (paragraph[i] is '.' or '!' or '?' or ';' && char.IsWhiteSpace(paragraph[i + 1]))
            {
                yield return paragraph[start..(i + 2)];
                start = i + 2;
            }
        }
        if (start < paragraph.Length)
            yield return paragraph[start..];
    }

    // The end of the previous chunk, from a word boundary, to start the next one.
    private string Tail(string chunk)
    {
        if (chunk.Length <= overlap)
            return chunk + " ";
        var tail = chunk[^overlap..];
        var space = tail.IndexOf(' ');
        return (space > 0 ? tail[(space + 1)..] : tail) + " ";
    }

    private static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
        text = System.Text.RegularExpressions.Regex.Replace(text, "[  ]{2,}", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, "\n{3,}", "\n\n").Trim();
    }
}
