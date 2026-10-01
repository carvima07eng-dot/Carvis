using Microsoft.Extensions.Logging;

namespace Carvis.App.Logging;

/// <summary>Daily log files in %LocalAppData%\Carvis\logs, old ones deleted after a few days.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateOnly _currentDay;

    public FileLoggerProvider(string directory, LogLevel minimumLevel, int retainDays)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(directory);
        DeleteOldFiles(retainDays);
    }

    public string CurrentFile => Path.Combine(_directory, $"carvis-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortCategory(categoryName));

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel;

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        lock (_lock)
        {
            try
            {
                var writer = EnsureWriter();
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelName(level)}] {category}: {message}");
                if (exception is not null)
                    writer.WriteLine(exception);
                writer.Flush();
            }
            catch (IOException)
            {
                // Logging must never break the app.
            }
        }
    }

    private StreamWriter EnsureWriter()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_writer is null || today != _currentDay)
        {
            _writer?.Dispose();
            _currentDay = today;
            var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream);
        }
        return _writer;
    }

    private void DeleteOldFiles(int retainDays)
    {
        var limit = DateTime.Now.AddDays(-Math.Max(1, retainDays));
        foreach (var file in Directory.EnumerateFiles(_directory, "carvis-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < limit)
                    File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
