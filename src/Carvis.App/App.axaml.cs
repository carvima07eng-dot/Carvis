using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Carvis.App.Configuration;
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
    private ServiceProvider? _services;
    private MainWindow? _window;
    private MainWindowViewModel? _viewModel;
    private GlobalHotkeyService? _hotkey;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Carvis lives in the background; hiding the window must not end the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => _services?.Dispose();

            var settings = SettingsLoader.Load();
            _services = BuildServices(settings);
            _viewModel = _services.GetRequiredService<MainWindowViewModel>();
            _window = new MainWindow
            {
                DataContext = _viewModel,
                HideOnFocusLost = settings.Window.HideOnFocusLost,
            };

            StartHotkey(settings);

            if (!settings.Window.StartHidden)
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
        if (_window is { IsVisible: true, IsActive: true })
            _window.Hide();
        else
            ShowWindow();
    }

    private void StartHotkey(CarvisSettings settings)
    {
        if (_viewModel is null || _services is null)
            return;

        var warnings = new List<string>();
        if (!HotkeyGesture.TryParse(settings.Hotkey.ToggleWindow, out var gesture))
            warnings.Add($"El atajo «{settings.Hotkey.ToggleWindow}» no es válido; uso {gesture}.");

        _hotkey = _services.GetRequiredService<GlobalHotkeyService>();
        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(ToggleWindow);
        _hotkey.Failed += (_, message) => Dispatcher.UIThread.Post(() => _viewModel.HotkeyWarning = message);

        if (!_hotkey.TryStart(gesture, out var error))
            warnings.Add(error!);

        if (warnings.Count > 0)
            _viewModel.HotkeyWarning = string.Join(" ", warnings);
    }

    private static ServiceProvider BuildServices(CarvisSettings settings)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddCarvisCore(settings);
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
