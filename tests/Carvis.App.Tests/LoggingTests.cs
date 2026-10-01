using Carvis.App.Logging;
using Microsoft.Extensions.Logging;

namespace Carvis.App.Tests;

public class LoggingTests
{
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Scrub_RemovesProfileFolderAndNames()
    {
        PrivacyScrubber.AddName("Lucía");

        var text = PrivacyScrubber.Scrub($"No encuentro {Path.Combine(Profile, "Apuntes", "tema1.pdf")}. Hola, Lucía.");

        Assert.DoesNotContain(Profile, text);
        Assert.Contains("%USERPROFILE%", text);
        Assert.Contains("Hola, <usuario>.", text);
    }

    [Fact]
    public void Log_WritesDailyFileWithoutPersonalPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "carvis-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var provider = CarvisLog.Create(directory, LogLevel.Information, retainDays: 3))
            {
                var logger = provider.CreateLogger("Carvis.Core.Indexing.DocumentIndexer");
                logger.LogInformation("Indexed {Path}", Path.Combine(Profile, "Documentos", "a.pdf"));
                logger.LogDebug("Not written");
            }

            var file = Assert.Single(Directory.GetFiles(directory, "carvis-*.log"));
            var text = File.ReadAllText(file);
            Assert.Contains("[INF] DocumentIndexer: Indexed", text);
            Assert.Contains("%USERPROFILE%", text);
            Assert.DoesNotContain(Profile + Path.DirectorySeparatorChar, text);
            Assert.DoesNotContain("Not written", text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
