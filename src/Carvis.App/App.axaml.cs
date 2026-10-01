using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Carvis.App.Platform;
using Carvis.App.Services;
using Carvis.App.ViewModels;
using Carvis.App.Views;
using Carvis.Core;
using Carvis.Core.Configuration;
using Carvis.Core.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carvis.App;

public partial class App : Application
{
    /// <summary>Starts in the tray; used by "Iniciar con Windows".</summary>
    public const string StartHiddenArgument = "--hidden";

    private ServiceProvider? _services;
    private MainWindow? _window;
    private MainWindowViewModel? _viewModel;
    private GlobalHotkeyService? _hotkey;
    private TrayIcon? _trayIcon;
    private ILogger? _logger;

    /// <summary>Set by Program before Avalonia starts; the designer runs without it.</summary>
    public static Bootstrap? Bootstrap { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Carvis lives in the background; hiding the window must not end the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();

            var bootstrap = Bootstrap ??= Bootstrap.Create();
            var settings = bootstrap.Settings;
            _services = BuildServices(bootstrap);
            _logger = _services.GetRequiredService<ILogger<App>>();
            Dispatcher.UIThread.UnhandledException += OnUiException;

            _viewModel = _services.GetRequiredService<MainWindowViewModel>();
            foreach (var warning in bootstrap.Warnings)
                _viewModel.AddNotice(warning);

            _window = new MainWindow(_services.GetRequiredService<WindowStateStore>(), settings.Window)
            {
                DataContext = _viewModel,
            };

            StartHotkey(settings);
            CreateTrayIcon(_viewModel.HotkeyText);

            // Launched by "Iniciar con Windows": stay in the tray.
            var startHidden = settings.Window.StartHidden || desktop.Args?.Contains(StartHiddenArgument) == true;
            if (!startHidden)
                Dispatcher.UIThread.Post(ShowWindow);

            _ = _viewModel.CheckStatusAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowWindow()
    {
        if (_window is null || _viewModel is null)
            return;

        _window.ShowAndFocus();
        if (!_viewModel.IsOllamaReady)
            _ = _viewModel.CheckStatusAsync();
    }

    public void ToggleWindow()
    {
        if (_window is null)
            return;

        if (_window.IsInFront)
            _window.Dismiss();
        else
            ShowWindow();
    }

    public void Exit()
    {
        if (_window is not null)
        {
            _window.SavePlacement();
            _window.AllowClose = true;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // A bug in one handler shouldn't take the whole assistant down.
    private void OnUiException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception on the UI thread");
        _viewModel?.AddNotice($"Ha ocurrido un error inesperado: {e.Exception.Message}. Los detalles están en el log.");
        e.Handled = true;
    }

    private void CreateTrayIcon(string hotkeyText)
    {
        var open = new NativeMenuItem("Abrir");
        open.Click += (_, _) => ShowWindow();

        var newConversation = new NativeMenuItem("Nueva conversación");
        newConversation.Click += (_, _) =>
        {
            if (_viewModel?.NewConversationCommand.CanExecute(null) == true)
                _viewModel.NewConversationCommand.Execute(null);
            ShowWindow();
        };

        var exit = new NativeMenuItem("Salir");
        exit.Click += (_, _) => Exit();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(newConversation);
        menu.Items.Add(new NativeMenuItemSeparator());
        if (OperatingSystem.IsWindows())
        {
            menu.Items.Add(CreateStartupMenuItem());
            menu.Items.Add(new NativeMenuItemSeparator());
        }
        menu.Items.Add(exit);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Carvis/Assets/carvis.ico"))),
            ToolTipText = $"Carvis ({hotkeyText})",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static NativeMenuItem CreateStartupMenuItem()
    {
        var item = new NativeMenuItem("Iniciar con Windows")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = WindowsStartup.IsEnabled(),
        };
        item.Click += (_, _) =>
        {
            WindowsStartup.SetEnabled(!item.IsChecked);
            item.IsChecked = WindowsStartup.IsEnabled();
        };
        return item;
    }

    private void Cleanup()
    {
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        _services?.Dispose();
    }

    private void StartHotkey(CarvisSettings settings)
    {
        if (_viewModel is null || _services is null)
            return;

        HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out var gesture);

        _hotkey = _services.GetRequiredService<GlobalHotkeyService>();
        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(ToggleWindow);
        _hotkey.Failed += (_, message) => Dispatcher.UIThread.Post(() => _viewModel.HotkeyWarning = message);

        if (!_hotkey.TryStart(gesture, out var error))
            _viewModel.HotkeyWarning = error;
    }

    private static ServiceProvider BuildServices(Bootstrap bootstrap)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddProvider(bootstrap.Log)
            .AddDebug()
            .SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton(bootstrap.Paths);
        services.AddSingleton(bootstrap.SettingsStore);
        services.AddCarvisCore(bootstrap.Settings);
        services.AddSingleton(new WindowStateStore(bootstrap.Paths.WindowStateFile));
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
