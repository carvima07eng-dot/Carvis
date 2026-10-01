using Avalonia;
using Avalonia.Media.Imaging;

namespace Carvis.App.ViewModels;

/// <summary>An image (capture, pasted or dropped) sent with the next message. Lives only in memory.</summary>
public sealed class ImageAttachmentViewModel
{
    // Vision models see big images as many tokens; 1600 px keeps text legible.
    private const int MaxSide = 1600;

    private ImageAttachmentViewModel(byte[] png, Bitmap preview, string label)
    {
        Png = png;
        Preview = preview;
        Label = label;
    }

    public byte[] Png { get; }
    public Bitmap Preview { get; }
    public string Label { get; }

    /// <summary>Any format Avalonia can read becomes a PNG of reasonable size.</summary>
    public static ImageAttachmentViewModel Create(byte[] data, string label)
    {
        using var input = new MemoryStream(data);
        var bitmap = new Bitmap(input);
        var size = bitmap.PixelSize;
        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(size.Width, size.Height));
        if (scale < 1)
        {
            var scaled = bitmap.CreateScaledBitmap(new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)), BitmapInterpolationMode.HighQuality);
            bitmap.Dispose();
            bitmap = scaled;
        }

        using var output = new MemoryStream();
        bitmap.Save(output);
        return new ImageAttachmentViewModel(output.ToArray(), bitmap, label);
    }

    public static ImageAttachmentViewModel Create(Bitmap bitmap, string label)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return Create(stream.ToArray(), label);
    }

    public static bool IsImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
}
