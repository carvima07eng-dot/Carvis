namespace Carvis.Core.Platform;

/// <summary>Small pop-up notifications that work even when Carvis' window is hidden.</summary>
public interface INotifier
{
    /// <param name="important">Stays until the user closes it and plays a sound (reminders).</param>
    void Notify(string title, string message, Action? onClick = null, bool important = false);
}

public sealed class NullNotifier : INotifier
{
    public void Notify(string title, string message, Action? onClick = null, bool important = false)
    {
    }
}
