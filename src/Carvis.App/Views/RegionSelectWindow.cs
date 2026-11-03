using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Carvis.App.Views;

/// <summary>
/// The frozen screenshot over the whole monitor; the user drags the part to keep.
/// Enter keeps the whole screen, Esc cancels.
/// </summary>
public sealed class RegionSelectWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle[] _shade = [Shade(), Shade(), Shade(), Shade()];
    private readonly Border _selection = new()
    {
        BorderBrush = Platform.ThemeColors.Brush("Accent"),
        BorderThickness = Platform.Tokens.Inset("Border.2"),
        IsVisible = false,
    };
    private readonly TaskCompletionSource<PixelRect?> _result = new();
    private readonly PixelSize _pixels;
    private Point? _start;

    private RegionSelectWindow(Bitmap screenshot, PixelPoint position, PixelSize pixels, double scaling)
    {
        _pixels = pixels;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Cursor = new Cursor(StandardCursorType.Cross);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = position;
        Width = pixels.Width / scaling;
        Height = pixels.Height / scaling;

        var image = new Image { Source = screenshot, Stretch = Stretch.Fill, Width = Width, Height = Height };
        _canvas.Children.Add(image);
        foreach (var shade in _shade)
            _canvas.Children.Add(shade);
        _canvas.Children.Add(_selection);
        var hint = new Border
        {
            Background = Platform.ThemeColors.Brush("SurfaceSolid"),
            CornerRadius = Platform.Tokens.Radius(8),
            Padding = Platform.Tokens.Inset("Inset.H16V8"),
            Margin = Platform.Tokens.Inset("Gap.T24"),
            BoxShadow = Platform.Tokens.Shadow("Flyout"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Arrastra para elegir una zona · Intro: toda la pantalla · Esc: cancelar",
                Foreground = Platform.ThemeColors.Brush("TextPrimary"),
            },
        };
        Content = new Panel { Children = { _canvas, hint } };
        UpdateShade(new Rect(0, 0, 0, 0), all: true);

        PointerPressed += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _selection.IsVisible = true;
        };
        PointerMoved += (_, e) =>
        {
            if (_start is { } start)
                Show(Normalize(start, e.GetPosition(_canvas)));
        };
        PointerReleased += (_, e) =>
        {
            if (_start is not { } start)
                return;
            var rect = Normalize(start, e.GetPosition(_canvas));
            _start = null;
            if (rect.Width < 8 || rect.Height < 8)
            {
                _selection.IsVisible = false;
                UpdateShade(rect, all: true);
                return;
            }
            Finish(ToPixels(rect));
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Finish(null);
            else if (e.Key == Key.Enter)
                Finish(new PixelRect(0, 0, _pixels.Width, _pixels.Height));
        };
        Closed += (_, _) => _result.TrySetResult(null);
    }

    /// <summary>The chosen rectangle in screenshot pixels, or null if cancelled.</summary>
    public static async Task<PixelRect?> SelectAsync(Bitmap screenshot, PixelPoint position, PixelSize pixels, double scaling)
    {
        var window = new RegionSelectWindow(screenshot, position, pixels, scaling);
        window.Show();
        window.Activate();
        Platform.WindowsNative.BringToFront(window);
        return await window._result.Task;
    }

    private void Finish(PixelRect? rect)
    {
        _result.TrySetResult(rect);
        Close();
    }

    private void Show(Rect rect)
    {
        Canvas.SetLeft(_selection, rect.X);
        Canvas.SetTop(_selection, rect.Y);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;
        UpdateShade(rect, all: false);
    }

    // Four dark bands around the selection leave it bright.
    private void UpdateShade(Rect rect, bool all)
    {
        if (all)
            rect = new Rect(0, 0, 0, 0);
        Place(_shade[0], 0, 0, Width, all ? Height : rect.Y);
        Place(_shade[1], 0, rect.Bottom, Width, all ? 0 : Height - rect.Bottom);
        Place(_shade[2], 0, rect.Y, rect.X, rect.Height);
        Place(_shade[3], rect.Right, rect.Y, Width - rect.Right, rect.Height);
    }

    private static void Place(Rectangle shade, double x, double y, double width, double height)
    {
        Canvas.SetLeft(shade, x);
        Canvas.SetTop(shade, y);
        shade.Width = Math.Max(0, width);
        shade.Height = Math.Max(0, height);
    }

    private PixelRect ToPixels(Rect rect)
    {
        var sx = _pixels.Width / Width;
        var sy = _pixels.Height / Height;
        var x = (int)Math.Clamp(rect.X * sx, 0, _pixels.Width - 1);
        var y = (int)Math.Clamp(rect.Y * sy, 0, _pixels.Height - 1);
        return new PixelRect(x, y,
            (int)Math.Clamp(rect.Width * sx, 1, _pixels.Width - x),
            (int)Math.Clamp(rect.Height * sy, 1, _pixels.Height - y));
    }

    private static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Rectangle Shade() => new() { Fill = Platform.ThemeColors.Brush("Scrim"), IsHitTestVisible = false };
}
