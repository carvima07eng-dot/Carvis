using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Carvis.App.Views.Controls;

/// <summary>Shows Markdown text. While an answer streams in, it re-renders at most every 60 ms.</summary>
public sealed class MarkdownView : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };

    public MarkdownView()
    {
        _renderTimer.Tick += (_, _) => RenderNow();
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != MarkdownProperty)
            return;

        if (Content is null)
            RenderNow();
        else if (!_renderTimer.IsEnabled)
            _renderTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void RenderNow()
    {
        _renderTimer.Stop();
        var markdown = Markdown;
        if (string.IsNullOrEmpty(markdown))
        {
            Content = null;
            return;
        }

        try
        {
            Content = MarkdownRenderer.Render(markdown);
        }
        catch (Exception)
        {
            // Never lose an answer because of a rendering problem.
            Content = new SelectableTextBlock { Text = markdown, TextWrapping = TextWrapping.Wrap };
        }
    }
}
