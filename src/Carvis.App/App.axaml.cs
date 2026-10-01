using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Carvis.App.Configuration;
using Carvis.App.ViewModels;
using Carvis.App.Views;
using Carvis.Core;
using Carvis.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carvis.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _window;
    private MainWindowViewModel? _viewModel;

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

    private static ServiceProvider BuildServices(CarvisSettings settings)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddCarvisCore(settings);
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
