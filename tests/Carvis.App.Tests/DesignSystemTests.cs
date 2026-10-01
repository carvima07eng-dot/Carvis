using System.Text.RegularExpressions;

namespace Carvis.App.Tests;

/// <summary>Views take colours, spacing, radii and type sizes only from Styles/DesignSystem.axaml.</summary>
public sealed partial class DesignSystemTests
{
    private static readonly string AppFolder = Path.Combine(FindRepository(), "src", "Carvis.App");

    public static TheoryData<string> Views() => new(Directory.GetFiles(Path.Combine(AppFolder, "Views"), "*.axaml").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(Views))]
    public void ViewsUseNoLiteralColours(string view)
    {
        var text = File.ReadAllText(Path.Combine(AppFolder, "Views", view));
        var colours = HexColour().Matches(text).Select(m => m.Value).ToList();
        Assert.True(colours.Count == 0, $"{view} uses colours that aren't tokens: {string.Join(", ", colours)}");
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void ViewsUseNoLiteralSpacing(string view)
    {
        var text = File.ReadAllText(Path.Combine(AppFolder, "Views", view));
        var literals = SpacingAttribute().Matches(text).Select(m => m.Value).ToList();
        Assert.True(literals.Count == 0, $"{view} uses sizes that aren't tokens: {string.Join(", ", literals)}");
    }

    [Fact]
    public void EveryResourceUsedExists()
    {
        var defined = new HashSet<string>();
        foreach (var file in new[] { "DesignSystem.axaml", "Icons.axaml" })
            foreach (Match m in DefinedKey().Matches(File.ReadAllText(Path.Combine(AppFolder, "Styles", file))))
                defined.Add(m.Groups[1].Value);

        var files = Directory.GetFiles(Path.Combine(AppFolder, "Views"), "*.axaml")
            .Append(Path.Combine(AppFolder, "Styles", "Controls.axaml"));
        var missing = files
            .SelectMany(f => UsedKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(u => !defined.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Unknown resources: " + string.Join(", ", missing));
    }

    [Fact]
    public void ThemesDefineTheSameColours()
    {
        var text = File.ReadAllText(Path.Combine(AppFolder, "Styles", "DesignSystem.axaml"));
        // The text after the opening tag: the tag's own key is the theme's name.
        var themes = ThemeBlock().Matches(text)
            .Select(m => DefinedKey().Matches(m.Value[(m.Value.IndexOf('>') + 1)..]).Select(k => k.Groups[1].Value).ToHashSet())
            .ToList();
        Assert.Equal(3, themes.Count);
        Assert.True(themes[0].SetEquals(themes[1]) && themes[0].SetEquals(themes[2]), "Dark, Light and HighContrast must define the same keys.");
    }

    private static string FindRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carvis.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Carvis.sln not found");
    }

    [GeneratedRegex(@"#[0-9A-Fa-f]{6,8}\b")]
    private static partial Regex HexColour();

    // A number in these attributes (0 alone is allowed: it means "none").
    [GeneratedRegex(@"\b(Margin|Padding|Spacing|CornerRadius|FontSize|ColumnSpacing|RowSpacing|BorderThickness)=""(?!0"")[0-9][0-9.,\s]*""")]
    private static partial Regex SpacingAttribute();

    [GeneratedRegex(@"x:Key=""([^""{]+)""")]
    private static partial Regex DefinedKey();

    [GeneratedRegex(@"\{(?:Static|Dynamic)Resource ([A-Za-z0-9.]+)\}")]
    private static partial Regex UsedKey();

    [GeneratedRegex(@"<ResourceDictionary x:Key=""[^""]+"">.*?</ResourceDictionary>", RegexOptions.Singleline)]
    private static partial Regex ThemeBlock();
}
