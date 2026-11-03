using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Carvis.Core.Platform;

namespace Carvis.App.Services;

/// <summary>The system clipboard through Avalonia; it must be used from the UI thread.</summary>
public sealed class AvaloniaClipboard : IClipboardService
{
    public TopLevel? Owner { get; set; }

    public Task<string?> GetTextAsync() =>
        Dispatcher.UIThread.InvokeAsync(async () => Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null);

    public Task SetTextAsync(string text) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = Clipboard ?? throw new InvalidOperationException("El portapapeles no está disponible.");
            await clipboard.SetTextAsync(text);
        });

    private IClipboard? Clipboard => Owner?.Clipboard;
}
