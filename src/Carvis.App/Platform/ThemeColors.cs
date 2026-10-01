using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Carvis.App.Platform;

/// <summary>Theme colours for controls built in code; same keys as App.axaml.</summary>
public static class ThemeColors
{
    public static IBrush Brush(string key)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        return Brushes.Gray;
    }

    public static Color Color(string key) => Brush(key) is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    /// <summary>Applies the theme and the accent colour from the settings.</summary>
    public static void Apply(Carvis.Core.Configuration.WindowSettings settings)
    {
        if (Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };

        // A custom accent overrides both themes (resources outside the theme dictionaries win).
        if (Avalonia.Media.Color.TryParse(settings.AccentColor, out var accent))
        {
            app.Resources["Accent"] = new SolidColorBrush(accent);
            app.Resources["AccentHover"] = new SolidColorBrush(Mix(accent, Colors.White, 0.3));
            app.Resources["AccentPressed"] = new SolidColorBrush(Mix(accent, Colors.Black, 0.2));
            app.Resources["OnAccent"] = new SolidColorBrush(Luminance(accent) > 0.5 ? Avalonia.Media.Color.Parse("#0B1220") : Colors.White);
        }
        else
        {
            foreach (var key in new[] { "Accent", "AccentHover", "AccentPressed", "OnAccent" })
                app.Resources.Remove(key);
        }
    }

    private static Color Mix(Color a, Color b, double amount) => Avalonia.Media.Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
}
