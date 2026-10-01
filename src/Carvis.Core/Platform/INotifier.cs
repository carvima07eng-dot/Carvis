namespace Carvis.Core.Platform;

/// <summary>Small pop-up notifications that work even when Carvis' window is hidden.</summary>
public interface INotifier
{
    void Notify(string title, string message, Action? onClick = null);
}

public sealed class NullNotifier : INotifier
{
    public void Notify(string title, string message, Action? onClick = null)
    {
    }
}
