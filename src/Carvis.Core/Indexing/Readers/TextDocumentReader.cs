using System.Text;

namespace Carvis.Core.Indexing.Readers;

/// <summary>Plain text, Markdown, CSV, code and HTML. Markdown headings become sections.</summary>
public sealed class TextDocumentReader : IDocumentReader
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".ini", ".cfg", ".conf", ".json", ".xml", ".yaml", ".yml", ".toml", ".csv", ".tsv",
        ".cs", ".csproj", ".sln", ".axaml", ".xaml", ".java", ".kt", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css",
        ".scss", ".sql", ".php", ".rb", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".swift", ".dart", ".sh", ".ps1", ".bat", ".cmd",
        ".vb", ".lua", ".r", ".m", ".tex", ".srt", ".vtt", ".properties", ".gradle", ".rtf",
    };

    public bool CanRead(string path) => Extensions.Contains(Path.GetExtension(path));

    public async Task<DocumentText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (LooksBinary(bytes))
            throw new InvalidDataException("El archivo no es de texto.");

        var text = Decode(bytes);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => DocumentText.Single(HtmlToText(text)),
            ".rtf" => DocumentText.Single(RtfToText(text)),
            ".md" or ".markdown" => SplitMarkdown(text),
            _ => DocumentText.Single(text),
        };
    }

    private static DocumentText SplitMarkdown(string text)
    {
        var parts = new List<DocumentPart>();
        var section = (string?)null;
        var current = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('#'))
            {
                if (current.Length > 0)
                    parts.Add(new DocumentPart(current.ToString().Trim(), Section: section));
                current.Clear();
                section = line.TrimStart('#').Trim();
            }
            current.Append(line).Append('\n');
        }
        if (current.Length > 0)
            parts.Add(new DocumentPart(current.ToString().Trim(), Section: section));
        return new DocumentText(parts);
    }

    private static bool LooksBinary(byte[] bytes) => bytes.Take(4096).Count(b => b == 0) > 2;

    // UTF-8 first; old Windows files are often in Windows-1252 (Latin-1 is close enough for Spanish).
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    internal static string HtmlToText(string html)
    {
        var withoutScripts = System.Text.RegularExpressions.Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var withBreaks = System.Text.RegularExpressions.Regex.Replace(withoutScripts, "<(br|/p|/div|/li|/h[1-6]|/tr)[^>]*>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var text = System.Text.RegularExpressions.Regex.Replace(withBreaks, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, "[ \\t]+", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, "\\n\\s*\\n+", "\n\n").Trim();
    }

    // Enough RTF to read the words: drop control words and groups like fonts and colours.
    internal static string RtfToText(string rtf)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(rtf, @"\{\\(fonttbl|colortbl|stylesheet|info|\*)[^{}]*(\{[^{}]*\}[^{}]*)*\}", string.Empty);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\par[d]?\b ?", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\'([0-9a-fA-F]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?", string.Empty);
        return text.Replace("{", string.Empty).Replace("}", string.Empty).Trim();
    }
}
