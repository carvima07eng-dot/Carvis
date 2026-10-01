using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private bool _manualHeight;

    // Used by the XAML designer.
    public MainWindow() : this(null, new WindowSettings())
    {
    }

    public MainWindow(WindowStateStore? stateStore, WindowSettings settings)
    {
        _stateStore = stateStore;
        _settings = settings;

        // Must be registered before XAML sets the decorations, which is when the styles are applied.
        WindowsNative.KeepTaskbarBehaviour(this);
        InitializeComponent();

        HideOnFocusLost = settings.HideOnFocusLost;
        FontSize = settings.FontSize;
        ApplyBackdrop(settings.Backdrop);

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
                };
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

    public void ApplySettings(WindowSettings settings)
    {
        HideOnFocusLost = settings.HideOnFocusLost;
        FontSize = settings.FontSize;
        ApplyBackdrop(settings.Backdrop);
    }

    public void SavePlacement()
    {
        _saveTimer.Stop();
        if (_settings.RememberPosition && _hasBeenPositioned && WindowState == WindowState.Normal)
            _stateStore?.Save(new WindowPlacement(Position.X, Position.Y, Finite(Width), _manualHeight ? Finite(Height) : 0));
    }

    // Width/Height are NaN while the window sizes itself to its content.
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    public void ApplyBackdrop(string backdrop)
    {
        (TransparencyLevelHint, RootBorder.Background) = backdrop switch
        {
            "Acrylic" => ([WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent], Brush.Parse("#CC0B1220")),
            "Mica" => ([WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent], Brush.Parse("#B30B1220")),
            _ => ((IReadOnlyList<WindowTransparencyLevel>)[WindowTransparencyLevel.Transparent], Brush.Parse("#0B1220")),
        };
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
            if (saved is not null && saved.Height >= MinHeight)
                UseManualHeight(saved.Height);
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
        var height = (int)((_manualHeight ? Height : MaxHeight) * scale);
        Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        // Esc first answers a pending confirmation; only then it hides the window.
        if (DataContext is MainWindowViewModel { PendingConfirmation: { } pending })
            pending.Deny();
        else
            Dismiss();
        e.Handled = true;
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

    // Edges resize the window; changing its height switches from automatic to fixed height.
    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string tag } || !Enum.TryParse<WindowEdge>(tag, out var edge)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (edge is not (WindowEdge.East or WindowEdge.West))
            UseManualHeight(Bounds.Height);
        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void UseManualHeight(double height)
    {
        if (!_manualHeight)
        {
            _manualHeight = true;
            SizeToContent = SizeToContent.Manual;
            MaxHeight = double.PositiveInfinity;
            RootGrid.RowDefinitions[3].Height = GridLength.Star;
            MessagesScroll.MaxHeight = double.PositiveInfinity;
            MessagesScroll.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            HistoryPanel.Height = double.NaN;
        }
        Height = height;
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
