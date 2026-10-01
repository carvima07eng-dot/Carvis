namespace Carvis.Core.Text;

public enum TokenKind
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Type,
}

public readonly record struct CodeToken(string Text, TokenKind Kind);

/// <summary>
/// A small, forgiving highlighter for code blocks in answers: keywords, strings, comments and
/// numbers for the languages that usually show up. Not a parser; good enough to read code.
/// </summary>
public static class CodeTokenizer
{
    private static readonly Dictionary<string, (HashSet<string> Keywords, string LineComment, bool BlockComments, bool CaseInsensitive)> Languages = new(StringComparer.OrdinalIgnoreCase);

    static CodeTokenizer()
    {
        var cLike = "if else for foreach while do switch case break continue return new class struct interface enum public private protected internal static readonly const void using namespace try catch finally throw this base null true false var async await in out ref is as get set override virtual abstract sealed partial record yield default typeof sizeof params where select from let";
        Add(["cs", "csharp", "c#"], cLike + " int long string bool double float decimal char object byte short uint ulong dynamic event delegate operator implicit explicit lock goto checked unchecked fixed unsafe nameof init required file global with", "//", true);
        Add(["java", "kotlin", "kt"], cLike + " int long boolean double float char byte short extends implements import package final synchronized throws instanceof fun val when", "//", true);
        Add(["js", "javascript", "ts", "typescript", "jsx", "tsx"], cLike + " function let const of export import from undefined typeof instanceof delete extends implements type declare module require constructor super", "//", true);
        Add(["c", "cpp", "c++", "h", "hpp"], cLike + " int long char double float unsigned signed include define ifdef endif auto template typename std nullptr", "//", true);
        Add(["py", "python"], "def class if elif else for while return import from as with try except finally raise pass break continue lambda yield None True False and or not in is global nonlocal async await self print", "#", false);
        Add(["ps1", "powershell", "pwsh", "ps"], "function param if else elseif foreach for while do until switch return try catch finally throw begin process end in true false null", "#", false, caseInsensitive: true);
        Add(["sh", "bash", "shell", "zsh"], "if then else elif fi for while do done case esac function return in export local echo exit", "#", false);
        Add(["sql"], "select from where insert into values update set delete create table index view drop alter add join left right inner outer on group by order having limit as and or not null is in like between distinct primary key foreign references default count sum avg min max", "--", true, caseInsensitive: true);
        Add(["json"], "true false null", "", false);
        Add(["html", "xml", "xaml", "axaml"], "", "", false);
        Add(["css", "scss"], "", "", true);
    }

    public static bool IsKnown(string? language) => language is not null && Languages.ContainsKey(language.Trim());

    public static IReadOnlyList<CodeToken> Tokenize(string code, string? language)
    {
        if (!IsKnown(language))
            return [new CodeToken(code, TokenKind.Plain)];

        var (keywords, lineComment, blockComments, caseInsensitive) = Languages[language!.Trim()];
        var isPowerShell = language.Trim().ToLowerInvariant() is "ps1" or "powershell" or "pwsh" or "ps";
        var tokens = new List<CodeToken>();
        var plain = new System.Text.StringBuilder();
        var i = 0;

        void Flush()
        {
            if (plain.Length > 0)
            {
                tokens.Add(new CodeToken(plain.ToString(), TokenKind.Plain));
                plain.Clear();
            }
        }

        void Emit(int length, TokenKind kind)
        {
            Flush();
            tokens.Add(new CodeToken(code.Substring(i, length), kind));
            i += length;
        }

        while (i < code.Length)
        {
            var c = code[i];

            if (lineComment.Length > 0 && string.CompareOrdinal(code, i, lineComment, 0, lineComment.Length) == 0)
            {
                var end = code.IndexOf('\n', i);
                Emit((end < 0 ? code.Length : end) - i, TokenKind.Comment);
            }
            else if (blockComments && c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                Emit((end < 0 ? code.Length : end + 2) - i, TokenKind.Comment);
            }
            else if (c is '<' && i + 3 < code.Length && code.AsSpan(i).StartsWith("<!--"))
            {
                var end = code.IndexOf("-->", i, StringComparison.Ordinal);
                Emit((end < 0 ? code.Length : end + 3) - i, TokenKind.Comment);
            }
            else if (c is '"' or '\'' or '`')
            {
                var end = i + 1;
                while (end < code.Length && code[end] != c && code[end] != '\n')
                    end += code[end] == '\\' ? 2 : 1;
                Emit(Math.Min(code.Length, end + 1) - i, TokenKind.String);
            }
            else if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(code[i - 1]) && code[i - 1] != '_'))
            {
                var end = i;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] is '.' or '_'))
                    end++;
                Emit(end - i, TokenKind.Number);
            }
            else if (char.IsLetter(c) || c == '_' || (isPowerShell && c is '$' or '-' && i + 1 < code.Length && char.IsLetter(code[i + 1])))
            {
                var end = i + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_' || (isPowerShell && code[end] == '-')))
                    end++;
                var word = code[i..end];
                var lookup = caseInsensitive ? word.ToLowerInvariant() : word;
                if (keywords.Contains(lookup))
                    Emit(end - i, TokenKind.Keyword);
                else if (char.IsUpper(word[0]) && word.Length > 1 && !caseInsensitive && language is not ("py" or "python"))
                    Emit(end - i, TokenKind.Type);
                else
                {
                    plain.Append(word);
                    i = end;
                }
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }

        Flush();
        return tokens;
    }

    private static void Add(string[] names, string keywords, string lineComment, bool blockComments, bool caseInsensitive = false)
    {
        var set = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var name in names)
            Languages[name] = (set, lineComment, blockComments, caseInsensitive);
    }
}
