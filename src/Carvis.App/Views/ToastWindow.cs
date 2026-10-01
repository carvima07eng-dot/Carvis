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
        Width = 340;
        SizeToContent = SizeToContent.Height;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var close = new Button { Content = "✕", Padding = new Thickness(6, 2), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);

        var icon = new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Carvis/Assets/carvis.png"))), Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Top };
        var text = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 4, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Platform.ThemeColors.Brush("TextPrimary") });
        text.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Platform.ThemeColors.Brush("TextSecondary") });
        Grid.SetColumn(text, 1);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(close);

        Content = new Border
        {
            Background = Platform.ThemeColors.Brush("Background"),
            BorderBrush = Platform.ThemeColors.Brush(important ? "Accent" : "WindowBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 8, 12),
            Margin = new Thickness(8),
            BoxShadow = BoxShadows.Parse("0 6 20 0 #66000000"),
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
