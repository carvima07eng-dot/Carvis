using Carvis.Core.Chat;
using Carvis.Core.Configuration;
using Carvis.Core.Text;
using Carvis.Tests.Fakes;

namespace Carvis.Tests.Chat;

public class HelpersTests
{
    [Fact]
    public async Task TitleGenerator_CleansTheModelAnswer()
    {
        var client = new FakeChatModelClient().Reply("<think></think>", "\"Título: Carpetas del escritorio.\"");

        Assert.Equal("Carpetas del escritorio", await new TitleGenerator(client).GenerateAsync("crea carpetas", "hecho"));
    }

    [Fact]
    public void TitleGenerator_FallsBackToTheFirstWords()
    {
        Assert.Equal("hola", TitleGenerator.Fallback("hola"));
        Assert.EndsWith("…", TitleGenerator.Fallback(new string('a', 100)));
    }

    [Fact]
    public void CodeTokenizer_FindsKeywordsStringsCommentsAndNumbers()
    {
        var tokens = CodeTokenizer.Tokenize("var _x = \"hola\"; // saludo\nreturn 42;", "csharp");

        Assert.Contains(new CodeToken("var", TokenKind.Keyword), tokens);
        Assert.Contains(new CodeToken("\"hola\"", TokenKind.String), tokens);
        Assert.Contains(new CodeToken("// saludo", TokenKind.Comment), tokens);
        Assert.Contains(new CodeToken("42", TokenKind.Number), tokens);
        Assert.Contains(tokens, t => t.Text.Contains("_x") && t.Kind == TokenKind.Plain);
        Assert.Equal("var _x = \"hola\"; // saludo\nreturn 42;", string.Concat(tokens.Select(t => t.Text)));
    }

    [Fact]
    public void CodeTokenizer_HandlesPowerShellParametersAndUnknownLanguages()
    {
        var tokens = CodeTokenizer.Tokenize("Get-ChildItem -Path $env:USERPROFILE # lista", "powershell");
        Assert.Contains(new CodeToken("# lista", TokenKind.Comment), tokens);

        Assert.Single(CodeTokenizer.Tokenize("algo", "klingon"));
    }

    [Fact]
    public void SettingsApplier_UpdatesTheLiveSectionsInPlace()
    {
        var live = new CarvisSettings();
        var ollama = live.Ollama;
        var edited = SettingsApplier.Clone(live);
        edited.Ollama.ChatModel = "qwen3:14b";
        edited.Permissions.AllowedFolders.Add(@"D:\Clase");

        SettingsApplier.CopyInto(edited, live);

        Assert.Same(ollama, live.Ollama);
        Assert.Equal("qwen3:14b", ollama.ChatModel);
        Assert.Equal([@"D:\Clase"], live.Permissions.AllowedFolders);
    }
}
