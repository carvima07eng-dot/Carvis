using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Carvis.App.Views;
using Carvis.Core.Configuration;
using Carvis.Core.Vision;
using Carvis.Windows;

namespace Carvis.App.Services;

/// <summary>Screenshots with Carvis out of the way. They stay in memory unless the user asked to keep them.</summary>
public sealed class ScreenCaptureService(VisionSettings settings) : IScreenCapture
{
    /// <summary>Carvis' main window, hidden while capturing.</summary>
    public MainWindow? Owner { get; set; }

    /// <summary>The window that was in front before Carvis was shown ("the active window").</summary>
    public IntPtr PreviousForeground { get; set; }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public Task<byte[]?> CaptureAsync(CaptureArea area, CancellationToken cancellationToken = default) =>
        Dispatcher.UIThread.InvokeAsync(() => CaptureOnUiAsync(area));

    private async Task<byte[]?> CaptureOnUiAsync(CaptureArea area)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Las capturas de pantalla solo funcionan en Windows.");

        var window = Owner;
        var wasShown = window is { IsVisible: true } && window.WindowState != Avalonia.Controls.WindowState.Minimized;
        var target = wasShown ? PreviousForeground : ScreenGrabber.ForegroundWindow();
        if (wasShown)
        {
            window!.Hide();
            await Task.Delay(250); // let Windows repaint what was behind
        }

        try
        {
            var bounds = area == CaptureArea.Window && ScreenGrabber.WindowRect(target) is { } windowRect
                ? windowRect
                : ScreenGrabber.MonitorUnderCursor();
            var raw = ScreenGrabber.Capture(bounds);

            if (area == CaptureArea.Region)
            {
                using var preview = ToBitmap(raw);
                var scaling = window?.Screens.ScreenFromPoint(new PixelPoint(bounds.X + 1, bounds.Y + 1))?.Scaling ?? 1;
                var chosen = await RegionSelectWindow.SelectAsync(preview, new PixelPoint(bounds.X, bounds.Y), new PixelSize(raw.Width, raw.Height), scaling);
                if (chosen is not { } rect)
                    return null;
                raw = Crop(raw, rect);
            }

            using var bitmap = ToBitmap(raw);
            using var png = new MemoryStream();
            bitmap.Save(png);
            var bytes = png.ToArray();
            if (settings.SaveCaptures)
                Save(bytes);
            return bytes;
        }
        finally
        {
            if (wasShown)
                window!.ShowAndFocus();
        }
    }

    private static WriteableBitmap ToBitmap(RawImage raw)
    {
        var bitmap = new WriteableBitmap(new PixelSize(raw.Width, raw.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < raw.Height; y++)
            Marshal.Copy(raw.Bgra, y * raw.Width * 4, buffer.Address + y * buffer.RowBytes, raw.Width * 4);
        return bitmap;
    }

    private static RawImage Crop(RawImage raw, PixelRect rect)
    {
        var pixels = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(raw.Bgra, ((rect.Y + y) * raw.Width + rect.X) * 4, pixels, y * rect.Width * 4, rect.Width * 4);
        return new RawImage(rect.Width, rect.Height, pixels);
    }

    private static void Save(byte[] png)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Carvis");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"Captura {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png"), png);
    }
}
