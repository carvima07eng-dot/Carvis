using System.Text;
using System.Text.RegularExpressions;
using Carvis.Core.Tools;

namespace Carvis.Core.Voice;

/// <summary>
/// Turns the streaming Markdown answer into sentences that sound right: code blocks, symbols,
/// links and lists are not read literally. Sentences come out as soon as they are complete.
/// </summary>
public sealed partial class SpeechChunker
{
    private const int MinimumLength = 12;
    private readonly StringBuilder _buffer = new();
    private bool _inCode;
    private bool _codeAnnounced;

    public IEnumerable<string> Push(string delta)
    {
        _buffer.Append(delta);
        var results = new List<string>();
        while (TryTakeSentence(out var sentence))
        {
            if (Clean(sentence) is { Length: > 0 } clean)
                results.Add(clean);
        }
        return results;
    }

    public IEnumerable<string> Flush()
    {
        var rest = _buffer.ToString();
        _buffer.Clear();
        if (_inCode)
            return [];
        return Clean(rest) is { Length: > 0 } clean ? [clean] : [];
    }

    private bool TryTakeSentence(out string sentence)
    {
        sentence = string.Empty;
        var text = _buffer.ToString();
        for (var i = 0; i < text.Length; i++)
        {
            // Code fences: everything inside is skipped, and announced once.
            if (text.AsSpan(i).StartsWith("```"))
            {
                var lineEnd = text.IndexOf('\n', i);
                if (lineEnd < 0)
                    return false;
                var before = text[..i];
                _buffer.Remove(0, lineEnd + 1);
                if (!_inCode && !_codeAnnounced)
                {
                    sentence = before + " Te dejo el código en pantalla.";
                    _codeAnnounced = true;
                }
                else
                {
                    sentence = _inCode ? string.Empty : before;
                }
                _inCode = !_inCode;
                return true;
            }

            if (_inCode)
            {
                if (text[i] == '\n')
                {
                    _buffer.Remove(0, i + 1);
                    sentence = string.Empty;
                    return true;
                }
                continue;
            }

            var c = text[i];
            var end = c == '\n' ||
                (c is '.' or '!' or '?' or ';' or ':' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]) && i + 1 >= MinimumLength);
            if (!end)
                continue;

            sentence = text[..(i + 1)];
            _buffer.Remove(0, i + 1);
            return true;
        }
        return false;
    }

    public static string Clean(string text)
    {
        text = LinkRegex().Replace(text, "$1");
        text = UrlRegex().Replace(text, "el enlace");
        text = BulletRegex().Replace(text, string.Empty);
        text = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty)
            .Replace("#", string.Empty).Replace("*", string.Empty).Replace("|", ", ")
            .Replace("->", " a ").Replace("→", " a ").Replace("&", " y ")
            .Replace("€", " euros").Replace("%", " por ciento");
        text = EmojiRegex().Replace(text, string.Empty);
        text = SpaceRegex().Replace(text, " ").Trim();
        return text.Any(char.IsLetterOrDigit) ? text : string.Empty;
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?m)^\s*(?:[-*+]|\d+[.)])\s+")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"[\p{Cs}\p{So}]")]
    private static partial Regex EmojiRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}

public static class SpeechFilters
{
    // What Whisper "hears" in silence or noise, learned from subtitles.
    private static readonly string[] Hallucinations =
    [
        "subtitulos realizados por la comunidad de amara.org",
        "subtitulos por la comunidad de amara.org",
        "amara.org",
        "gracias por ver el video",
        "suscribete",
        "gracias por ver",
        "musica",
        "[musica]",
        "(musica)",
        "...",
        "gracias.",
    ];

    private static readonly char[] Separators = [' ', ',', '.', '!', '?', '¡', '¿'];

    private static readonly string[] WakeWords = ["carvis", "jarvis", "carbis", "garvis", "calvis", "carvi", "carvys", "karvis"];

    /// <summary>The transcription without the noise Whisper invents; empty if nothing real was said.</summary>
    public static string CleanTranscript(string text)
    {
        var trimmed = text.Trim();
        var normalized = ToolSelector.Normalize(trimmed).Trim(' ', '.', '!', '¡');
        if (normalized.Length == 0 || normalized.Contains("amara.org") || Hallucinations.Any(h => normalized == h.Trim('.', '[', ']', '(', ')')))
            return string.Empty;
        return trimmed;
    }

    /// <summary>
    /// Finds the wake word at the start of what was said. Returns false if it isn't there; when it
    /// is, <paramref name="command"/> is whatever came after it ("Carvis, abre Spotify").
    /// </summary>
    public static bool TryMatchWakeWord(string transcript, out string command)
    {
        command = string.Empty;
        var words = ToolSelector.Normalize(transcript)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Math.Min(words.Length, 3); i++)
        {
            if (!WakeWords.Any(w => Levenshtein(words[i], w) <= 1))
                continue;

            // Keep the original spelling of the rest of the sentence.
            var originalWords = transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            command = string.Join(' ', originalWords.Skip(i + 1)).Trim(' ', ',', '.', '!', '?');
            return true;
        }
        return false;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
