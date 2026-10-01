using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Carvis.App.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Carvis.App.Tests;

/// <summary>The real App (styles and resources); without a desktop lifetime it starts no services.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Carvis.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
