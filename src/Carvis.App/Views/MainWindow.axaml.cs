using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Carvis.App.Platform;
using Carvis.App.Services;
using Carvis.App.ViewModels;
using Carvis.Core.Configuration;

namespace Carvis.App.Views;

public partial class MainWindow : Window
{
    private const double AutoScrollTolerance = 40;

    private readonly WindowStateStore? _stateStore;
    private readonly WindowSettings _settings;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _hasBeenPositioned;

    // Used by the XAML designer.
    public MainWindow() : this(null, new WindowSettings())
    {
    }

    public MainWindow(WindowStateStore? stateStore, WindowSettings settings)
    {
        _stateStore = stateStore;
        _settings = settings;

        InitializeComponent();
        ConfigureChrome();

        HideOnFocusLost = settings.HideOnFocusLost;
        FontSize = settings.FontSize;
        ThemeColors.Apply(settings);
        ApplyBackdrop(settings.Backdrop);
        UpdateMotion();
        ThemeColors.Changed += UpdateMotion;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        PromptBox.AddHandler(KeyDownEvent, OnPromptKeyDown, RoutingStrategies.Tunnel);
        TitleBar.PointerPressed += OnTitleBarPointerPressed;
        MessagesScroll.PropertyChanged += OnMessagesScrollPropertyChanged;
        Deactivated += (_, _) =>
        {
            if (HideOnFocusLost)
                Hide();
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PropertyChanged += (_, e) =>
                {
                    // Coming back from the history panel or opening a conversation: show its end.
                    if (e.PropertyName is nameof(MainWindowViewModel.ShowConversation) or nameof(MainWindowViewModel.CurrentConversationId))
                        ScrollToEndSoon();
                    if (e.PropertyName is nameof(MainWindowViewModel.HasMessages) or nameof(MainWindowViewModel.IsHistoryOpen))
                        UpdateLayoutMode();
                };
                viewModel.CopyToClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
                viewModel.FocusPromptRequested += () =>
                {
                    PromptBox.Focus();
                    PromptBox.CaretIndex = PromptBox.Text?.Length ?? 0;
                };
                UpdateLayoutMode();
            }
        };

        _saveTimer.Tick += (_, _) => SavePlacement();
        PositionChanged += (_, _) =>
        {
            if (_hasBeenPositioned)
                _saveTimer.Start();
        };
        Resized += (_, _) =>
        {
            // A height the user chose while there is a conversation is kept for next time.
            if (_expanded && SizeToContent == SizeToContent.Manual && double.IsFinite(Height))
                _expandedHeight = Height;
            if (_hasBeenPositioned)
                _saveTimer.Start();
        };
    }

    /// <summary>
    /// Spotlight mode: always on top and hidden as soon as it loses focus.
    /// Otherwise it behaves like a normal app window.
    /// </summary>
    public bool HideOnFocusLost
    {
        get => Topmost;
        set => Topmost = value;
    }

    /// <summary>Set before shutting down; otherwise closing the window only hides it.</summary>
    public bool AllowClose { get; set; }

    /// <summary>True when the user is looking at the window right now.</summary>
    public bool IsInFront =>
        IsVisible && WindowState != WindowState.Minimized && (IsActive || HideOnFocusLost);

    public void ShowAndFocus()
    {
        if (!IsVisible || !_hasBeenPositioned)
            PlaceOnMouseScreen();

        // Win+D or "show desktop" may have minimized it while it was hidden.
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        var appearing = !IsVisible;
        Show();
        Activate();
        WindowsNative.BringToFront(this);
        if (appearing && ThemeColors.MotionEnabled)
            AnimateIn();
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

    public void ApplySettings(WindowSettings settings)
    {
        HideOnFocusLost = settings.HideOnFocusLost;
        FontSize = settings.FontSize;
        ThemeColors.Apply(settings);
        ApplyBackdrop(settings.Backdrop);
    }

    public void SavePlacement()
    {
        _saveTimer.Stop();
        if (_settings.RememberPosition && _hasBeenPositioned && WindowState == WindowState.Normal)
            _stateStore?.Save(new WindowPlacement(Position.X, Position.Y, Finite(Width), _expandedHeight));
    }

    // Windows 11 draws the rounded corners, the shadow, the resize border and Mica itself;
    // elsewhere (tests on Linux) the window draws a frame of its own.
    private void ConfigureChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            SystemDecorations = SystemDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
            ResizeHandles.IsVisible = false;
        }
        else
        {
            SystemDecorations = SystemDecorations.None;
            RootBorder.Margin = Resource<Thickness>("Gap.Window");
            RootBorder.CornerRadius = Resource<CornerRadius>("Radius.8");
            RootBorder.BorderThickness = Resource<Thickness>("Border.1");
            RootBorder[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("StrokeStrong");
            RootBorder[!Border.BoxShadowProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Shadow.Window");
        }
    }

    private static T Resource<T>(string key) =>
        Avalonia.Application.Current!.TryGetResource(key, null, out var value) && value is T typed ? typed : default!;

    private void UpdateMotion() => Classes.Set("motion", ThemeColors.MotionEnabled);

    private const double DefaultExpandedHeight = 600;
    private double _expandedHeight = DefaultExpandedHeight;
    private bool _expanded;

    // Compact while there is nothing to show; a comfortable fixed height with a conversation.
    private void UpdateLayoutMode()
    {
        var expand = DataContext is MainWindowViewModel { HasMessages: true } or MainWindowViewModel { IsHistoryOpen: true };
        if (expand == _expanded)
            return;
        _expanded = expand;
        if (expand)
        {
            var height = Math.Min(_expandedHeight, MaxScreenHeight());
            SizeToContent = SizeToContent.Manual;
            RootGrid.RowDefinitions[3].Height = GridLength.Star;
            Height = height;
        }
        else
        {
            RootGrid.RowDefinitions[3].Height = GridLength.Auto;
            SizeToContent = SizeToContent.Height;
        }
    }

    private double MaxScreenHeight()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        return screen is null ? DefaultExpandedHeight : screen.WorkingArea.Height / screen.Scaling * 0.85;
    }

    // Width/Height are NaN while the window sizes itself to its content.
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    // A short fade and rise when the window appears.
    private void AnimateIn()
    {
        RootBorder.Transitions ??=
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
            new Avalonia.Animation.TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        ];
        RootBorder.Opacity = 0;
        RootBorder.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(8px)");
        Dispatcher.UIThread.Post(() =>
        {
            RootBorder.Opacity = 1;
            RootBorder.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Render);
    }

    private string _backdrop = "Solid";

    public void ApplyBackdrop(string backdrop)
    {
        _backdrop = backdrop;
        // Windows picks the first effect it supports: Mica on Windows 11, nothing on Windows 10.
        TransparencyLevelHint = backdrop switch
        {
            "Mica" => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            "Acrylic" => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            _ => OperatingSystem.IsWindows() ? [WindowTransparencyLevel.None] : [WindowTransparencyLevel.Transparent],
        };
        UpdateBackground();
    }

    // Over Mica the content sits on a clear layer; with no effect it needs a solid base.
    private void UpdateBackground()
    {
        var key = Backdrop.KeyFor(ActualTransparencyLevel);
        RootBorder[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
    }

    // The background is built from the theme colour: rebuild it when the theme changes.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty && RootBorder is not null)
            UpdateBackground();
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

    // First show: the saved place if it is still on a screen. Later: follow the mouse to its monitor.
    private void PlaceOnMouseScreen()
    {
        var mouse = WindowsNative.GetCursorPosition();
        var target = (mouse is { } point ? Screens.ScreenFromPoint(point) : null)
                     ?? Screens.ScreenFromWindow(this)
                     ?? Screens.Primary;
        if (target is null)
            return;

        if (!_hasBeenPositioned)
        {
            _hasBeenPositioned = true;
            var saved = _settings.RememberPosition ? _stateStore?.Load() : null;
            if (saved is not null && saved.Width >= MinWidth)
                Width = saved.Width;
            if (saved is not null && saved.Height >= MinHeight * 2)
                _expandedHeight = saved.Height;
            if (saved is not null && Screens.ScreenFromPoint(new PixelPoint(saved.X + 40, saved.Y + 20)) is { } savedScreen
                && (mouse is null || savedScreen.Equals(target)))
            {
                Position = new PixelPoint(saved.X, saved.Y);
                return;
            }
        }
        else if (Screens.ScreenFromWindow(this) is { } current && current.Equals(target))
        {
            return; // same monitor: keep where the user left it
        }

        CenterOn(target);
    }

    // The window grows downwards, so it is centred as if it had its maximum height.
    private void CenterOn(Screen screen)
    {
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)(Width * scale);
        var height = (int)(Math.Min(_expandedHeight, area.Height / scale * 0.85) * scale);
        Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && e.KeyModifiers == KeyModifiers.Control && RunShortcut(viewModel, e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape)
            return;

        // Esc first answers a pending confirmation; only then it hides the window.
        if (DataContext is MainWindowViewModel { PendingConfirmation: { } pending })
            pending.Deny();
        else
            Dismiss();
        e.Handled = true;
    }

    // Everything is reachable from the keyboard: Ctrl+N new, Ctrl+H history, Ctrl+, settings,
    // Ctrl+M talk, Ctrl+L back to the prompt.
    private bool RunShortcut(MainWindowViewModel viewModel, Key key)
    {
        switch (key)
        {
            case Key.N when viewModel.NewConversationCommand.CanExecute(null):
                viewModel.NewConversationCommand.Execute(null);
                return true;
            case Key.H:
                viewModel.ToggleHistoryCommand.Execute(null);
                return true;
            case Key.OemComma:
                viewModel.OpenSettingsCommand.Execute(null);
                return true;
            case Key.M:
                viewModel.ToggleVoice();
                return true;
            case Key.L:
                PromptBox.Focus();
                return true;
            default:
                return false;
        }
    }

    // Enter sends, Shift+Enter adds a line, arrow up recalls the last message.
    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (e.Key == Key.Enter && viewModel.PendingConfirmation is { } pending && string.IsNullOrWhiteSpace(viewModel.Input))
        {
            pending.Approve();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (viewModel.SendCommand.CanExecute(null))
                viewModel.SendCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && viewModel.RecallLastMessage())
        {
            PromptBox.CaretIndex = PromptBox.Text?.Length ?? 0;
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _ = PasteAsync();
        }
        else if (e.Key == Key.Escape && viewModel.IsSpeaking)
        {
            viewModel.StopSpeakingCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async Task PasteAsync()
    {
        if (!await TryPasteImageAsync())
            PromptBox.Paste();
    }

    private async void OnCopyMessageClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MessageViewModel message || Clipboard is null)
            return;

        await Clipboard.SetTextAsync(message.Content);
        await message.ShowCopiedAsync();
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    // Files dropped on the window are attached to the next message.
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || e.DataTransfer.TryGetFiles() is not { } files)
            return;
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                viewModel.AttachFile(path);
        }
        PromptBox.Focus();
    }

    private async void OnAttachClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Adjuntar archivos",
            AllowMultiple = true,
        });
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                viewModel.AttachFile(path);
        }
    }

    private void OnCaptureRegionClick(object? sender, RoutedEventArgs e) => Capture(Carvis.Core.Vision.CaptureArea.Region);
    private void OnCaptureScreenClick(object? sender, RoutedEventArgs e) => Capture(Carvis.Core.Vision.CaptureArea.Screen);
    private void OnCaptureWindowClick(object? sender, RoutedEventArgs e) => Capture(Carvis.Core.Vision.CaptureArea.Window);

    private void OnCopyTextClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current is App app)
            _ = app.CopyTextFromScreenAsync();
    }

    private static void Capture(Carvis.Core.Vision.CaptureArea area)
    {
        if (Avalonia.Application.Current is App app)
            _ = app.CaptureForChatAsync(area);
    }

    // Ctrl+V with a picture in the clipboard attaches it instead of pasting text.
    private async Task<bool> TryPasteImageAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel || Clipboard is not { } clipboard)
            return false;
        try
        {
            if (await clipboard.TryGetBitmapAsync() is not { } bitmap)
                return false;
            viewModel.AttachImage(ImageAttachmentViewModel.Create(bitmap, "Imagen pegada"));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private void OnModelFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.LoadModelsCommand.Execute(null);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // Edges resize the window (only where Windows doesn't draw a resize border).
    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string tag } || !Enum.TryParse<WindowEdge>(tag, out var edge)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (edge is not (WindowEdge.East or WindowEdge.West) && !_expanded)
        {
            _expanded = true;
            SizeToContent = SizeToContent.Manual;
            RootGrid.RowDefinitions[3].Height = GridLength.Star;
        }
        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnHideClick(object? sender, RoutedEventArgs e) => Hide();

    private void ScrollToEndSoon()
    {
        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
        // Virtualized items get their real height after a layout pass: settle once more.
        DispatcherTimer.RunOnce(() => MessagesScroll.ScrollToEnd(), TimeSpan.FromMilliseconds(120));
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
