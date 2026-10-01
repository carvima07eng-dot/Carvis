using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Carvis.Core.Configuration;

namespace Carvis.App.Platform;

/// <summary>Theme, accent and motion; colours for controls built in code (keys of DesignSystem.axaml).</summary>
public static class ThemeColors
{
    /// <summary>Black, white and yellow, for low vision. Built on the dark Fluent theme.</summary>
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);

    private static readonly Color DefaultAccent = Avalonia.Media.Color.Parse("#0078D4"); // Windows blue
    private static WindowSettings? _settings;
    private static bool _listening;

    /// <summary>"Mica", "Acrylic" or "Solid", for every window.</summary>
    public static string Backdrop => _settings?.Backdrop ?? "Mica";

    /// <summary>Animations are on in Carvis and in Windows ("Animation effects").</summary>
    public static bool MotionEnabled => (_settings?.Animations ?? true) && WindowsNative.AnimationsEnabled();

    public static event Action? Changed;

    public static IBrush Brush(string key)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        return Brushes.Gray;
    }

    public static Color Color(string key) => Brush(key) is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    public static double Number(string key) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is double number ? number : 0;

    /// <summary>Applies the theme and the accent from the settings (or from Windows).</summary>
    public static void Apply(WindowSettings settings)
    {
        if (Application.Current is not { } app)
            return;
        _settings = settings;
        var platform = app.PlatformSettings?.GetColorValues();
        app.RequestedThemeVariant = settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "HighContrast" => HighContrast,
            "System" when platform?.ContrastPreference == ColorContrastPreference.High => HighContrast,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };

        if (!_listening)
        {
            _listening = true;
            app.ActualThemeVariantChanged += (_, _) => ApplyAccent(app);
            if (app.PlatformSettings is { } os)
                os.ColorValuesChanged += (_, _) => Apply(_settings!);
        }
        ApplyAccent(app);
        Changed?.Invoke();
    }

    // The accent of the user, else the one of Windows; a lighter shade on dark backgrounds.
    private static void ApplyAccent(Application app)
    {
        string[] keys = ["Accent",
            "AccentHover",
            "AccentPressed",
            "OnAccent",
            "AccentText",
            "SystemAccentColor",
            "SystemAccentColorLight1",
            "SystemAccentColorLight2",
            "SystemAccentColorLight3",
            "SystemAccentColorDark1",
            "SystemAccentColorDark2",
            "SystemAccentColorDark3"];
        foreach (var key in keys)
            app.Resources.Remove(key);
        if (app.ActualThemeVariant == HighContrast)
            return; // keeps its own yellow

        Color? chosen = Avalonia.Media.Color.TryParse(_settings?.AccentColor, out var custom) ? custom : null;
        if (chosen is null && OperatingSystem.IsWindows() && app.PlatformSettings?.GetColorValues() is { } values && values.AccentColor1.A > 0)
            chosen = values.AccentColor1;
        var accent = chosen ?? DefaultAccent;

        var dark = app.ActualThemeVariant == ThemeVariant.Dark;
        var fill = dark ? Mix(accent, Colors.White, 0.45) : Mix(accent, Colors.Black, 0.1);
        app.Resources["Accent"] = new SolidColorBrush(fill);
        app.Resources["AccentHover"] = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0xE6, fill.R, fill.G, fill.B));
        app.Resources["AccentPressed"] = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0xCC, fill.R, fill.G, fill.B));
        app.Resources["OnAccent"] = new SolidColorBrush(Luminance(fill) > 0.55 ? Colors.Black : Colors.White);
        app.Resources["AccentText"] = new SolidColorBrush(dark ? Mix(accent, Colors.White, 0.55) : Mix(accent, Colors.Black, 0.3));

        // Fluent's own controls (check boxes, sliders, toggles) follow the same accent.
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.2);
        app.Resources["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.35);
        app.Resources["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.5);
        app.Resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.1);
        app.Resources["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.25);
        app.Resources["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.4);
    }

    private static Color Mix(Color a, Color b, double amount) => Avalonia.Media.Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
}
