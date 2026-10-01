using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Carvis.App.Platform;

namespace Carvis.App.Views;

public partial class MainWindow : Window
{
    private const double AutoScrollTolerance = 40;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        MessagesScroll.PropertyChanged += OnMessagesScrollPropertyChanged;
        Deactivated += (_, _) =>
        {
            if (HideOnFocusLost)
                Hide();
        };
    }

    public bool HideOnFocusLost { get; set; } = true;

    /// <summary>Set before shutting down; otherwise closing the window only hides it.</summary>
    public bool AllowClose { get; set; }

    public void ShowAndFocus()
    {
        if (!IsVisible)
            CenterOnScreen();

        Show();
        Activate();
        WindowsNative.BringToFront(this);
        Dispatcher.UIThread.Post(() => PromptBox.Focus(), DispatcherPriority.Input);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    // The window grows downwards, so it is centred as if it had its maximum height.
    private void CenterOnScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)(Width * scale);
        var height = (int)(MaxHeight * scale);
        Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    // Keep following the answer while it streams, unless the user scrolled up to read.
    private void OnMessagesScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.ExtentProperty || e.OldValue is not Size oldExtent)
            return;

        var bottom = MessagesScroll.Offset.Y + MessagesScroll.Viewport.Height;
        if (bottom >= oldExtent.Height - AutoScrollTolerance)
            MessagesScroll.ScrollToEnd();
    }
}
