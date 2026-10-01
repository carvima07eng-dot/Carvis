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

    private bool _hasBeenPositioned;
    private bool _hideOnFocusLost;

    public MainWindow()
    {
        // Must be registered before XAML sets the decorations, which is when the styles are applied.
        WindowsNative.KeepTaskbarBehaviour(this);
        InitializeComponent();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        TitleBar.PointerPressed += OnTitleBarPointerPressed;
        MessagesScroll.PropertyChanged += OnMessagesScrollPropertyChanged;
        Deactivated += (_, _) =>
        {
            if (HideOnFocusLost)
                Hide();
        };
    }

    /// <summary>
    /// Spotlight mode: always on top and hidden as soon as it loses focus.
    /// Otherwise it behaves like a normal app window.
    /// </summary>
    public bool HideOnFocusLost
    {
        get => _hideOnFocusLost;
        set
        {
            _hideOnFocusLost = value;
            Topmost = value;
        }
    }

    /// <summary>Set before shutting down; otherwise closing the window only hides it.</summary>
    public bool AllowClose { get; set; }

    /// <summary>True when the user is looking at the window right now.</summary>
    public bool IsInFront =>
        IsVisible && WindowState != WindowState.Minimized && (IsActive || HideOnFocusLost);

    public void ShowAndFocus()
    {
        if (!_hasBeenPositioned)
        {
            CenterOnScreen();
            _hasBeenPositioned = true;
        }

        // Win+D or "show desktop" may have minimized it while it was hidden.
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
        WindowsNative.BringToFront(this);
        Dispatcher.UIThread.Post(() => PromptBox.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Gets the window out of the way: hidden in Spotlight mode, minimized otherwise.</summary>
    public void Dismiss()
    {
        if (HideOnFocusLost)
            Hide();
        else
            WindowState = WindowState.Minimized;
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
            Dismiss();
            e.Handled = true;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnHideClick(object? sender, RoutedEventArgs e) => Hide();

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
