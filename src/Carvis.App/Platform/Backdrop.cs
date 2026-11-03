using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Carvis.App.Platform;

/// <summary>
/// Mica (Windows 11), acrylic or a solid base. Windows uses the first effect it supports, so on
/// Windows 10 it falls back to solid; the root panel gets a clear layer only when an effect is on.
/// </summary>
public static class Backdrop
{
    public static void Apply(Window window, Panel root, string backdrop)
    {
        window.Background = Avalonia.Media.Brushes.Transparent;
        window.TransparencyLevelHint = backdrop switch
        {
            "Mica" => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            "Acrylic" => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            _ => [WindowTransparencyLevel.None],
        };
        Update(window, root);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ActualTransparencyLevelProperty)
                Update(window, root);
        };
    }

    public static string KeyFor(WindowTransparencyLevel level) =>
        level == WindowTransparencyLevel.Mica || level == WindowTransparencyLevel.AcrylicBlur ? "Layer" : "Background";

    private static void Update(Window window, Panel root) =>
        root[!Panel.BackgroundProperty] = new DynamicResourceExtension(KeyFor(window.ActualTransparencyLevel));
}
