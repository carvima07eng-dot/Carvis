using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using MicrosoftLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Carvis.App.Logging;

/// <summary>
/// Serilog rolling files in %LocalAppData%\Carvis\logs: one per day (and a new one past 10 MB),
/// old ones deleted after RetainDays. Paths and names are scrubbed before anything is written.
/// </summary>
public static class CarvisLog
{
    private const string Template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Category}: {Message:lj}{NewLine}{Exception}";

    public static SerilogLoggerProvider Create(string directory, MicrosoftLogLevel minimumLevel, int retainDays)
    {
        Directory.CreateDirectory(directory);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilog(minimumLevel))
            .Enrich.With<ShortCategory>()
            .WriteTo.File(
                new ScrubbingFormatter(new MessageTemplateTextFormatter(Template)),
                Path.Combine(directory, "carvis-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: Math.Max(1, retainDays),
                shared: true)
            .CreateLogger();
        Log.Logger = logger;
        return new SerilogLoggerProvider(logger, dispose: true);
    }

    private static LogEventLevel ToSerilog(MicrosoftLogLevel level) => level switch
    {
        MicrosoftLogLevel.Trace => LogEventLevel.Verbose,
        MicrosoftLogLevel.Debug => LogEventLevel.Debug,
        MicrosoftLogLevel.Warning => LogEventLevel.Warning,
        MicrosoftLogLevel.Error => LogEventLevel.Error,
        MicrosoftLogLevel.Critical or MicrosoftLogLevel.None => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };

    // "Carvis.Core.Chat.ChatService" → "ChatService", as in the old log files.
    private sealed class ShortCategory : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var source = logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value) && value is ScalarValue { Value: string text }
                ? text
                : "Carvis";
            var dot = source.LastIndexOf('.');
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Category", dot >= 0 ? source[(dot + 1)..] : source));
        }
    }

    // Renders the whole line (message and exception) and scrubs it, so nothing slips through a property.
    private sealed class ScrubbingFormatter(ITextFormatter inner) : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var line = new StringWriter();
            inner.Format(logEvent, line);
            output.Write(PrivacyScrubber.Scrub(line.ToString()));
        }
    }
}
