using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Carvis.App;

internal static class Program
{
    private const string InstanceMutexName = "Carvis.SingleInstance";
    private const string ShowEventName = "Carvis.ShowWindow";

    /// <summary>Passed when Carvis restarts itself: wait for the old instance to exit first.</summary>
    public const string RestartArgument = "--restart";

    [STAThread]
    public static int Main(string[] args)
    {
        // Installer hooks (install, update, uninstall) run here and exit straight away.
        var velopack = Velopack.VelopackApp.Build();
        if (OperatingSystem.IsWindows())
            velopack.OnBeforeUninstallFastCallback(_ => DisableStartWithWindows());
        velopack.Run();

        using var mutex = new Mutex(initiallyOwned: false, InstanceMutexName);
        var isFirstInstance = WaitForMutex(mutex, args.Contains(RestartArgument) ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
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
        {
            log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception, the app will close");
            if (e.ExceptionObject is Exception crash)
                Services.CrashReporter.Save(bootstrap.Paths, crash, "AppDomain");
        };
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
        catch (Exception ex)
        {
            log.LogCritical(ex, "Carvis crashed");
            Services.CrashReporter.Save(bootstrap.Paths, ex, "UI");
            throw;
        }
        finally
        {
            log.LogInformation("Carvis stopped");
            bootstrap.Log.Dispose();
        }
    }

    // Uninstalling must not leave "start with Windows" pointing to a deleted program.
    private static void DisableStartWithWindows()
    {
        if (OperatingSystem.IsWindows())
            Platform.WindowsStartup.SetEnabled(false);
    }

    private static bool WaitForMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed: the mutex is ours now.
            return true;
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
