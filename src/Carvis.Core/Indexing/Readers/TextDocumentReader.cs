using System.Text;

namespace Carvis.Core.Indexing.Readers;

/// <summary>Plain text, Markdown, code and other text formats.</summary>
public sealed class TextDocumentReader : IDocumentReader
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".ini", ".cfg", ".conf", ".json", ".xml", ".yaml", ".yml", ".toml",
        ".cs", ".csproj", ".sln", ".axaml", ".xaml", ".java", ".kt", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css",
        ".scss", ".sql", ".php", ".rb", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".swift", ".dart", ".sh", ".ps1", ".bat", ".cmd",
        ".vb", ".lua", ".r", ".m", ".tex", ".srt", ".vtt", ".gitignore", ".env.example", ".properties", ".gradle",
    };

    public bool CanRead(string path) => Extensions.Contains(Path.GetExtension(path)) || Path.GetExtension(path).Length == 0;

    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (LooksBinary(bytes))
            throw new InvalidDataException("El archivo no es de texto.");

        var text = Decode(bytes);
        return Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm" ? HtmlToText(text) : text;
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
}
