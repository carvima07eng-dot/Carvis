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
using Carvis.Voice;
using Carvis.Windows;
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
    private SettingsWindow? _settingsWindow;
    private OnboardingViewModel? _onboarding;

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

            if (settings.Permissions.EnablePlugins)
            {
                Carvis.Core.Tools.PluginLoader.LoadInto(_services.GetRequiredService<Carvis.Core.Tools.IToolRegistry>(), _services,
                    bootstrap.Paths.PluginsDirectory, _logger);
            }

            ThemeColors.Apply(settings.Window);
            _viewModel = _services.GetRequiredService<MainWindowViewModel>();
            StartReconnectTimer();
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsOllamaReady) && _viewModel.IsOllamaReady)
                    StartIndexing();
            };
            foreach (var warning in bootstrap.Warnings)
                _viewModel.AddNotice(warning);

            _window = new MainWindow(_services.GetRequiredService<WindowStateStore>(), settings.Window)
            {
                DataContext = _viewModel,
            };
            _services.GetRequiredService<AvaloniaClipboard>().Owner = _window;
            _services.GetRequiredService<ScreenCaptureService>().Owner = _window;
            _viewModel.VoiceActivated += () => Dispatcher.UIThread.Post(ShowWindow);
            StartReminders();
            _viewModel.SettingsRequested += OpenSettings;
            _viewModel.AnswerCompleted += OnAnswerCompleted;

            StartHotkey(settings);
            CreateTrayIcon(_viewModel.HotkeyText);

            if (!settings.FirstRunCompleted)
            {
                Dispatcher.UIThread.Post(ShowOnboarding);
            }
            else
            {
                // Launched by "Iniciar con Windows": stay in the tray.
                var startHidden = settings.Window.StartHidden || desktop.Args?.Contains(StartHiddenArgument) == true;
                if (!startHidden)
                    Dispatcher.UIThread.Post(ShowWindow);
                _ = _viewModel.CheckStatusAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowWindow()
    {
        if (_window is null || _viewModel is null)
            return;

        if (!_window.IsInFront && OperatingSystem.IsWindows() && _services is not null)
            _services.GetRequiredService<ScreenCaptureService>().PreviousForeground = ScreenGrabber.ForegroundWindow();
        _window.ShowAndFocus();
        if (!_viewModel.IsOllamaReady)
            _ = _viewModel.CheckStatusAsync();
    }

    public void ToggleWindow()
    {
        if (_window is null)
            return;
        if (_onboarding is not null)
        {
            _onboarding.OnHotkeyPressed();
            return;
        }

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

    private const string VoiceHotkey = "voice";
    private const string CaptureHotkey = "capture";

    // Push-to-talk (only with voice on) and screen capture.
    private void ApplyExtraHotkeys(CarvisSettings settings)
    {
        if (_hotkey is null || _viewModel is null)
            return;
        var problems = new List<string>();
        HotkeyGesture? voice = settings.Voice.Enabled && HotkeyGesture.TryParse(settings.Voice.PushToTalkHotkey, out var v) ? v : null;
        if (!_hotkey.TrySet(VoiceHotkey, voice, out var voiceError) && voiceError is not null)
            problems.Add(voiceError);
        HotkeyGesture? capture = HotkeyGesture.TryParse(settings.Vision.CaptureHotkey, out var c) ? c : null;
        if (!_hotkey.TrySet(CaptureHotkey, capture, out var captureError) && captureError is not null)
            problems.Add(captureError);
        if (problems.Count > 0)
            _viewModel.HotkeyWarning = string.Join(" ", problems);
    }

    private void OnBindingPressed(string name)
    {
        if (name == VoiceHotkey)
        {
            ShowWindow();
            _viewModel?.ToggleVoice();
        }
        else if (name == CaptureHotkey)
        {
            _ = CaptureForChatAsync(Carvis.Core.Vision.CaptureArea.Region);
        }
    }

    /// <summary>Captures the screen and attaches it to the next message.</summary>
    public async Task CaptureForChatAsync(Carvis.Core.Vision.CaptureArea area)
    {
        if (_services is null || _viewModel is null)
            return;
        try
        {
            var png = await _services.GetRequiredService<ScreenCaptureService>().CaptureAsync(area);
            if (png is null)
                return;
            _viewModel.AttachImage(ImageAttachmentViewModel.Create(png, "Captura"));
            ShowWindow();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Screen capture failed");
            _viewModel.AddNotice($"No he podido capturar la pantalla: {ex.Message}");
        }
    }

    private void StartReminders()
    {
        var scheduler = _services!.GetRequiredService<Carvis.Core.Scheduling.ReminderScheduler>();
        var routines = _services!.GetRequiredService<Carvis.Core.Storage.IRoutineStore>();
        var notifier = _services!.GetRequiredService<Carvis.Core.Platform.INotifier>();
        scheduler.ReminderDue += (reminder, late) => Dispatcher.UIThread.Post(() =>
        {
            var when = late ? $" (era para las {reminder.DueAt:HH:mm})" : string.Empty;
            if (reminder.Routine is { } name && routines.Find(name) is { } routine)
            {
                notifier.Notify("Rutina programada", $"Ejecutando «{routine.Name}»{when}", ShowWindow);
                _ = _viewModel!.RunToolsAsync($"⏰ Rutina programada «{routine.Name}»{when}", routine.Steps);
            }
            else
            {
                notifier.Notify("Recordatorio", reminder.Text + when, ShowWindow, important: true);
            }
        });
        scheduler.Start();
    }

    private bool _indexingStarted;

    // Documents are read in the background once Ollama answers (embeddings need it).
    private void StartIndexing(bool force = false)
    {
        if (_services is null || (_indexingStarted && !force))
            return;
        var settings = _services.GetRequiredService<CarvisSettings>();
        var index = _services.GetRequiredService<Carvis.Core.Indexing.IIndexService>();
        _indexingStarted = true;
        index.StartWatching();
        if (settings.Documents.Folders.Count > 0)
            _ = Task.Run(() => index.IndexAsync());
    }

    // If Ollama was closed or started late, find it again without the user doing anything.
    private void StartReconnectTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) =>
        {
            if (_viewModel is { IsOllamaReady: false, IsCheckingStatus: false, IsBusy: false })
                _ = _viewModel.CheckStatusAsync();
        };
        timer.Start();
    }

    /// <summary>Starts a new Carvis and closes this one (some settings need it).</summary>
    public void Restart()
    {
        if (Environment.ProcessPath is { } path)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, Program.RestartArgument) { UseShellExecute = false })?.Dispose();
        Exit();
    }

    private void ShowOnboarding()
    {
        if (_services is null || _viewModel is null)
            return;
        _onboarding = new OnboardingViewModel(
            _services.GetRequiredService<CarvisSettings>(), _services.GetRequiredService<SettingsStore>(),
            _services.GetRequiredService<Carvis.Core.Ollama.IOllamaHealthCheck>(), _services.GetRequiredService<Carvis.Core.Ollama.IModelManager>(),
            _services.GetRequiredService<Carvis.Core.Platform.IShell>(), _viewModel.HotkeyText);

        var window = new OnboardingWindow { DataContext = _onboarding };
        _onboarding.Finished += () => window.Close();
        window.Closed += (_, _) =>
        {
            _onboarding = null;
            ShowWindow();
        };
        window.Show();
        window.Activate();
    }

    private void OpenSettings()
    {
        if (_services is null)
            return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = ActivatorUtilities.CreateInstance<SettingsViewModel>(_services);
        viewModel.AttachVoice(_services.GetRequiredService<Carvis.Core.Voice.VoiceModels>(), _services.GetRequiredService<Carvis.Core.Voice.ModelDownloader>(),
            _services.GetRequiredService<Carvis.Core.Voice.VoiceAssistant>(), _services.GetRequiredService<Carvis.Core.Voice.IAudioInput>(),
            _services.GetRequiredService<Carvis.Core.Voice.IAudioOutput>());
        viewModel.Saved += OnSettingsSaved;
        _settingsWindow = new SettingsWindow { DataContext = viewModel };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnSettingsSaved(bool restartNeeded)
    {
        if (_services is null || _window is null || _viewModel is null)
            return;
        var settings = _services.GetRequiredService<CarvisSettings>();
        _window.ApplySettings(settings.Window);

        if (HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out var gesture) && _hotkey is not null)
            _viewModel.HotkeyWarning = _hotkey.TryChange(gesture, out var error) ? null : error;
        ApplyExtraHotkeys(settings);

        _viewModel.OnSettingsApplied();
        StartIndexing(force: true);
        if (restartNeeded)
            _viewModel.AddNotice("Algunos cambios se aplicarán cuando reinicies Carvis (icono de la bandeja → Reiniciar).");
    }

    // Long answers can finish while the window is hidden: let the user know.
    private void OnAnswerCompleted(string answer)
    {
        if (_window is null || _window.IsInFront || _services is null)
            return;
        var text = answer.Length > 160 ? answer[..160] + "…" : answer;
        _services.GetRequiredService<Carvis.Core.Platform.INotifier>().Notify("Carvis ha respondido", text, ShowWindow);
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

        var settingsItem = new NativeMenuItem("Ajustes");
        settingsItem.Click += (_, _) => OpenSettings();

        var restart = new NativeMenuItem("Reiniciar");
        restart.Click += (_, _) => Restart();

        var exit = new NativeMenuItem("Salir");
        exit.Click += (_, _) => Exit();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(newConversation);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        if (OperatingSystem.IsWindows())
        {
            menu.Items.Add(CreateStartupMenuItem());
            menu.Items.Add(new NativeMenuItemSeparator());
        }
        menu.Items.Add(restart);
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
        _hotkey.BindingPressed += name => Dispatcher.UIThread.Post(() => OnBindingPressed(name));
        ApplyExtraHotkeys(settings);
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
        services.AddCarvisCore(bootstrap.Settings, bootstrap.Paths);
        if (OperatingSystem.IsWindows())
            services.AddCarvisWindows();
        services.AddCarvisVoice();
        services.AddSingleton(new WindowStateStore(bootstrap.Paths.WindowStateFile));
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<Carvis.Core.Platform.INotifier, ToastNotifier>();
        services.AddSingleton<AvaloniaClipboard>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<Carvis.Core.Vision.IScreenCapture>(sp => sp.GetRequiredService<ScreenCaptureService>());
        services.AddSingleton<Carvis.Core.Platform.IClipboardService>(sp => sp.GetRequiredService<AvaloniaClipboard>());
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
