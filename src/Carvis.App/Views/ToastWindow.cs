using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Carvis.App.Views;

/// <summary>A small notification in the bottom-right corner that doesn't steal focus.</summary>
public sealed class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    public ToastWindow(string title, string message, Action? onClick, bool important = false)
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Width = Platform.Tokens.Number("Toast.Width");
        SizeToContent = SizeToContent.Height;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var close = new Button { Content = new Controls.FluentIcon { Data = IconData("Dismiss") }, VerticalAlignment = VerticalAlignment.Top };
        close.Classes.Add("icon");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);

        var icon = new Controls.FluentIcon
        {
            Data = IconData(important ? "Alarm" : "Sparkle"),
            Size = Platform.Tokens.Type("Subtitle"),
            Foreground = Platform.ThemeColors.Brush("Accent"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var text = new StackPanel { Spacing = Platform.Tokens.Space(4), Margin = Platform.Tokens.Inset("Gap.L12") };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Platform.ThemeColors.Brush("TextPrimary") });
        text.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 4,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = Platform.Tokens.Type("Caption"),
            Foreground = Platform.ThemeColors.Brush("TextSecondary"),
        });
        Grid.SetColumn(text, 1);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(close);

        Content = new Border
        {
            Background = Platform.ThemeColors.Brush("SurfaceSolid"),
            BorderBrush = Platform.ThemeColors.Brush(important ? "Accent" : "Stroke"),
            BorderThickness = Platform.Tokens.Inset("Border.1"),
            CornerRadius = Platform.Tokens.Radius(8),
            Padding = Platform.Tokens.Inset("Inset.12"),
            Margin = Platform.Tokens.Inset("Gap.Window"),
            BoxShadow = Platform.Tokens.Shadow("Flyout"),
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
                return;
            onClick?.Invoke();
            Close();
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Close();
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) =>
        {
            if (!important)
                _timer.Start();
        };
        // Important ones (reminders) stay until closed.
        Opened += (_, _) =>
        {
            if (!important)
                _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private static Geometry? IconData(string key) => ViewModels.ToolIcons.ToGeometry.Convert(key, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture) as Geometry;

    /// <summary>Places the toast above the ones already shown.</summary>
    public void PlaceAt(int index)
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)(Width * scale);
        var height = (int)(Math.Max(Bounds.Height, 90) * scale);
        Position = new PixelPoint(area.Right - width - 8, area.Bottom - (height + 8) * (index + 1));
    }
}
