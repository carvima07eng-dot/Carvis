using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Carvis.App.Views.Controls;

/// <summary>One row of the settings, styled like the Windows Settings app (see Styles/Controls.axaml).</summary>
public sealed class SettingsCard : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingsCard, string?>(nameof(Header));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingsCard, string?>(nameof(Description));
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<SettingsCard, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<SettingsCard, object?>(nameof(Footer));

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Content under the row, full width (folder lists, longer explanations).</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
