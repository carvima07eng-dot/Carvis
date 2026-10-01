using Avalonia;
using Avalonia.Threading;

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

        ListenForOtherInstances();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
