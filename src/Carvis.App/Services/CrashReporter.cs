using System.Runtime.InteropServices;
using System.Text;
using Carvis.Core.Configuration;

namespace Carvis.App.Services;

/// <summary>
/// When Carvis crashes, the error is written to %LocalAppData%\Carvis\crash; the next start offers
/// to copy it or open a GitHub issue with it. Paths and the user name are removed first.
/// </summary>
public static class CrashReporter
{
    private const int MaxLogLines = 40;
    private const int MaxIssueBody = 6000; // GitHub rejects very long URLs

    public static string CrashFile(AppPaths paths) => Path.Combine(paths.DataRoot, "crash", "last-crash.txt");

    public static void Save(AppPaths paths, Exception exception, string where)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine($"Carvis {Version} · {RuntimeInformation.OSDescription} · .NET {Environment.Version}");
            text.AppendLine($"Cuándo: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · Dónde: {where}");
            text.AppendLine();
            text.AppendLine(exception.ToString());
            text.AppendLine();
            text.AppendLine("Últimas líneas del registro:");
            foreach (var line in LastLogLines(paths))
                text.AppendLine(line);

            var file = CrashFile(paths);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Sanitize(text.ToString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else can be done while crashing.
        }
    }

    /// <summary>The report of the last crash, if Carvis crashed since it was last read.</summary>
    public static string? TakePending(AppPaths paths)
    {
        var file = CrashFile(paths);
        if (!File.Exists(file))
            return null;
        try
        {
            var text = File.ReadAllText(file);
            File.Move(file, Path.ChangeExtension(file, ".seen.txt"), overwrite: true);
            return text;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>A new GitHub issue from the bug form (.github/ISSUE_TEMPLATE/bug.yml), with its fields filled in.</summary>
    public static string NewIssueUrl(string? report)
    {
        var fields = new Dictionary<string, string>
        {
            ["template"] = "bug.yml",
            ["title"] = report is null ? "Fallo: " : "Carvis se cerró por un error",
            ["version"] = Version,
            ["windows"] = RuntimeInformation.OSDescription,
        };
        if (report is not null)
        {
            fields["what"] = "Carvis se cerró solo mientras…";
            fields["report"] = report.Length > MaxIssueBody ? report[..MaxIssueBody] + "\n…" : report;
        }
        var query = string.Join("&", fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"));
        return $"{UpdateService.RepositoryUrl}/issues/new?{query}";
    }

    /// <summary>Removes what identifies the user: the profile path and the user name.</summary>
    public static string Sanitize(string text) => Logging.PrivacyScrubber.Scrub(text);

    private static IEnumerable<string> LastLogLines(AppPaths paths)
    {
        try
        {
            var latest = new DirectoryInfo(paths.LogsDirectory).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (latest is null)
                return [];
            using var stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > MaxLogLines)
                    lines.Dequeue();
            }
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static string Version => typeof(CrashReporter).Assembly.GetName().Version?.ToString(3) ?? "?";
}
