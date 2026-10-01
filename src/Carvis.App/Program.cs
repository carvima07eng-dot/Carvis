using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Carvis.App;

internal static class Program
{
    private const string InstanceMutexName = "Carvis.SingleInstance";
    private const string ShowEventName = "Carvis.ShowWindow";

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Carvis is already running (probably hidden in the tray): ask it to show itself.
            SignalRunningInstance();
            return 0;
        }

        var bootstrap = Bootstrap.Create();
        var log = bootstrap.Log.CreateLogger("Program");
        log.LogInformation("Carvis {Version} starting", typeof(Program).Assembly.GetName().Version);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception, the app will close");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        App.Bootstrap = bootstrap;
        ListenForOtherInstances();

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            log.LogInformation("Carvis stopped");
            bootstrap.Log.Dispose();
        }
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Named wait handles only exist on Windows.
    private static void SignalRunningInstance()
    {
        if (OperatingSystem.IsWindows() && EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    private static void ListenForOtherInstances()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (showEvent.WaitOne())
                Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowWindow());
        })
        {
            IsBackground = true,
            Name = "Carvis single instance",
        };
        thread.Start();
    }
}
