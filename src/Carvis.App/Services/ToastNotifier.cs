using Avalonia.Threading;
using Carvis.App.Views;
using Carvis.Core.Platform;

namespace Carvis.App.Services;

/// <summary>Shows notifications as small windows; they stack up to four at a time.</summary>
public sealed class ToastNotifier : INotifier
{
    private const int MaxVisible = 4;
    private readonly List<ToastWindow> _open = [];

    public void Notify(string title, string message, Action? onClick = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_open.Count >= MaxVisible)
                _open[0].Close();

            var toast = new ToastWindow(title, message, onClick);
            toast.Closed += (_, _) =>
            {
                _open.Remove(toast);
                Reposition();
            };
            _open.Add(toast);
            toast.Show();
            Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Background);
        });
    }

    private void Reposition()
    {
        for (var i = 0; i < _open.Count; i++)
            _open[i].PlaceAt(i);
    }
}
