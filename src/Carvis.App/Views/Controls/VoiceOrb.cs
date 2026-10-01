using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Carvis.App.Platform;
using Carvis.Core.Voice;

namespace Carvis.App.Views.Controls;

/// <summary>
/// The voice indicator: listening, an orb that breathes with the real microphone level; speaking,
/// a small waveform; understanding, a turning arc. Without animations it stays still but still
/// shows the level.
/// </summary>
public sealed class VoiceOrb : Control
{
    public static readonly StyledProperty<VoiceState> StateProperty = AvaloniaProperty.Register<VoiceOrb, VoiceState>(nameof(State));
    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<VoiceOrb, double>(nameof(Level));
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<VoiceOrb, IBrush?>(nameof(Accent));

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private double _shown;
    private double _phase;

    static VoiceOrb()
    {
        AffectsRender<VoiceOrb>(StateProperty, AccentProperty);
    }

    public VoiceOrb()
    {
        _timer.Tick += (_, _) => Step();
    }

    public VoiceState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty || change.Property == IsVisibleProperty)
            UpdateTimer();
        if (change.Property == LevelProperty && !ThemeColors.MotionEnabled)
        {
            _shown = Level;
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void UpdateTimer()
    {
        var active = IsVisible && State is VoiceState.Listening or VoiceState.Speaking or VoiceState.Transcribing;
        if (active && (ThemeColors.MotionEnabled || State == VoiceState.Listening))
            _timer.Start();
        else
            _timer.Stop();
        InvalidateVisual();
    }

    private void Step()
    {
        // Rises fast, falls slowly: it follows the voice without flickering.
        var target = State == VoiceState.Listening ? Level : 0;
        _shown += (target - _shown) * (target > _shown ? 0.5 : 0.15);
        if (ThemeColors.MotionEnabled)
            _phase += 0.18;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var accent = Accent ?? Brushes.DodgerBlue;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var color = (accent as ISolidColorBrush)?.Color ?? Colors.DodgerBlue;

        switch (State)
        {
            case VoiceState.Listening:
                {
                    var halo = new SolidColorBrush(color, 0.25);
                    var core = size * 0.3;
                    context.DrawEllipse(halo, null, center, core + size * 0.2 * _shown, core + size * 0.2 * _shown);
                    context.DrawEllipse(accent, null, center, core, core);
                    break;
                }
            case VoiceState.Speaking:
                {
                    const int bars = 5;
                    var width = size / (bars * 2 - 1);
                    for (var i = 0; i < bars; i++)
                    {
                        var wave = ThemeColors.MotionEnabled ? 0.35 + 0.65 * Math.Abs(Math.Sin(_phase + i * 0.9)) : 0.4 + 0.15 * (i % 3);
                        var height = Math.Max(width, size * 0.8 * wave);
                        var x = center.X - size / 2 + i * width * 2;
                        context.DrawRectangle(accent, null, new Rect(x, center.Y - height / 2, width, height), width / 2, width / 2);
                    }
                    break;
                }
            case VoiceState.Transcribing:
                {
                    var pen = new Pen(accent, Math.Max(2, size * 0.1), lineCap: PenLineCap.Round);
                    var radius = size * 0.38;
                    var start = _phase;
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start)), false);
                        g.ArcTo(new Point(center.X + radius * Math.Cos(start + 4), center.Y + radius * Math.Sin(start + 4)),
                            new Size(radius, radius), 0, true, SweepDirection.Clockwise);
                    }
                    context.DrawGeometry(null, pen, geometry);
                    break;
                }
            default:
                context.DrawEllipse(new SolidColorBrush(color, 0.6), null, center, size * 0.2, size * 0.2);
                break;
        }
    }
}
