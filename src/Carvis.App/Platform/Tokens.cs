using Avalonia;
using Avalonia.Media;

namespace Carvis.App.Platform;

/// <summary>The design system (Styles/DesignSystem.axaml) for controls built in code.</summary>
public static class Tokens
{
    public static double Space(int value) => Get("Space." + value, (double)value);
    public static Thickness Inset(string key) => Get(key, default(Thickness));
    public static CornerRadius Radius(int value) => Get("Radius." + value, new CornerRadius(value));
    public static double Type(string name) => Get("Type." + name, 14d);
    public static double Number(string key) => Get(key, 0d);
    public static FontFamily Font(string name) => Get("Font." + name, FontFamily.Default);
    public static BoxShadows Shadow(string name) =>
        Application.Current?.TryGetResource("Shadow." + name, Application.Current.ActualThemeVariant, out var value) == true && value is BoxShadows shadows
            ? shadows : default;

    private static T Get<T>(string key, T fallback) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is T typed ? typed : fallback;
}
