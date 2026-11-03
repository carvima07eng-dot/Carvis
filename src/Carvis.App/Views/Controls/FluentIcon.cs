using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Carvis.App.Views.Controls;

/// <summary>
/// A Fluent UI System Icon. The geometry is drawn on its own 20 px grid scaled to <see cref="Size"/>,
/// so every icon keeps the same stroke weight (PathIcon would stretch each one to fill its box).
/// </summary>
public sealed class FluentIcon : Control
{
    private const double Grid = 20;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<FluentIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<FluentIcon, double>(nameof(Size), 16);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<FluentIcon>();

    static FluentIcon()
    {
        AffectsMeasure<FluentIcon>(SizeProperty);
        AffectsRender<FluentIcon>(DataProperty, SizeProperty, ForegroundProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null)
            return;
        var scale = Size / Grid;
        var offset = new Point((Bounds.Width - Size) / 2, (Bounds.Height - Size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
            context.DrawGeometry(Foreground, null, Data);
    }
}
