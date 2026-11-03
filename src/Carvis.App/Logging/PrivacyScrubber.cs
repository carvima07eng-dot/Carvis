using System.Text.RegularExpressions;

namespace Carvis.App.Logging;

/// <summary>Takes the user's profile folder and names out of text that leaves the app (logs, crash reports).</summary>
public static class PrivacyScrubber
{
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static Regex? _names = Build(null);

    /// <summary>The name Carvis calls the user by is scrubbed too, besides the Windows account name.</summary>
    public static void AddName(string? name) => _names = Build(name);

    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        if (Profile.Length > 3)
            text = text.Replace(Profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return _names is { } names ? names.Replace(text, "<usuario>") : text;
    }

    private static Regex? Build(string? name)
    {
        var names = new[] { Environment.UserName, name?.Trim() }
            .Where(n => n is { Length: >= 3 })
            .Select(n => Regex.Escape(n!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? null : new Regex($@"\b({string.Join("|", names)})\b", RegexOptions.IgnoreCase);
    }
}
